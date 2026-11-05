using System.Globalization;
using System.Text.RegularExpressions;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;

namespace DistrictSync.Application.Ingestion;

public sealed record NormalizedRecord(string ExternalId, string MatchKey, IReadOnlyDictionary<string, string?> Attributes, string PayloadJson, string PayloadHash, DateTimeOffset? UpdatedAt, bool Deleted, long LineNumber);

public sealed record RejectedRecord(long LineNumber, string? ExternalId, string Reason);

/// <summary>
/// Turns heterogeneous raw rows into the canonical shape every downstream stage relies on: trimmed
/// values, mapped field names, a normalised match key and a content hash. Pure and allocation-conscious.
/// </summary>
public static partial class RecordNormalizer
{
    private static readonly string[] TruthyValues = ["true", "1", "yes", "y", "deleted", "inactive"];

    public static bool TryNormalize(RawRecord raw, SourceConfig config, out NormalizedRecord? normalized, out RejectedRecord? rejected)
    {
        normalized = null;
        rejected = null;

        var cleanedSource = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var cleanedValue = value is null ? null : CollapseWhitespace().Replace(value.Trim(), " ");
            cleanedSource[key.Trim()] = string.IsNullOrEmpty(cleanedValue) ? null : cleanedValue;

            var canonical = config.FieldMap is not null && config.FieldMap.TryGetValue(key, out var mapped) ? mapped : key.Trim();
            if (config.IgnoreFields is not null && config.IgnoreFields.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var cleaned = value is null ? null : CollapseWhitespace().Replace(value.Trim(), " ");
            attributes[canonical] = string.IsNullOrEmpty(cleaned) ? null : cleaned;
        }

        var externalId = raw.ExternalId ?? Lookup(cleanedSource, config.ExternalIdField) ?? Lookup(attributes, config.ExternalIdField);
        if (string.IsNullOrWhiteSpace(externalId))
        {
            rejected = new RejectedRecord(raw.LineNumber, null, $"Missing external id field '{config.ExternalIdField}'.");
            return false;
        }

        var matchKeyRaw = Lookup(cleanedSource, config.MatchKeyField) ?? Lookup(attributes, config.MatchKeyField);
        if (string.IsNullOrWhiteSpace(matchKeyRaw))
        {
            rejected = new RejectedRecord(raw.LineNumber, externalId, $"Missing match key field '{config.MatchKeyField}'.");
            return false;
        }

        var matchKey = NormalizeMatchKey(matchKeyRaw);
        if (matchKey.Length > 256)
        {
            rejected = new RejectedRecord(raw.LineNumber, externalId, "Match key exceeds 256 characters.");
            return false;
        }

        DateTimeOffset? updatedAt = raw.UpdatedAt;
        if (updatedAt is null && config.UpdatedAtField is not null)
        {
            var text = Lookup(cleanedSource, config.UpdatedAtField) ?? Lookup(attributes, config.UpdatedAtField);
            if (text is not null)
            {
                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    rejected = new RejectedRecord(raw.LineNumber, externalId, $"Unparseable timestamp '{text}' in '{config.UpdatedAtField}'.");
                    return false;
                }

                updatedAt = parsed;
            }
        }

        var deleted = raw.Deleted;
        if (!deleted && config.DeletedField is not null)
        {
            var flag = Lookup(cleanedSource, config.DeletedField) ?? Lookup(attributes, config.DeletedField);
            deleted = flag is not null && TruthyValues.Contains(flag, StringComparer.OrdinalIgnoreCase);
        }

        var payload = JsonDefaults.Canonicalize(attributes);
        normalized = new NormalizedRecord(externalId.Trim(), matchKey, attributes, payload, Hashing.Sha256Hex(payload), updatedAt, deleted, raw.LineNumber);
        return true;
    }

    /// <summary>Lower-case, trimmed, single-spaced. E-mail-like keys also drop a "mailto:" prefix.</summary>
    public static string NormalizeMatchKey(string value)
    {
        var key = CollapseWhitespace().Replace(value.Trim(), " ").ToLowerInvariant();
        return key.StartsWith("mailto:", StringComparison.Ordinal) ? key[7..] : key;
    }

    private static string? Lookup(Dictionary<string, string?> attributes, string field) => attributes.TryGetValue(field, out var v) ? v : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();
}
