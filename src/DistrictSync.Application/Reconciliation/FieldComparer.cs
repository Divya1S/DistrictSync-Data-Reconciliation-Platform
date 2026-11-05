using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DistrictSync.Domain.Reconciliation;

namespace DistrictSync.Application.Reconciliation;

public static partial class FieldNormalization
{
    public static string? Normalize(string? value, FieldNormalizer normalizer)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        switch (normalizer)
        {
            case FieldNormalizer.None:
                return value;
            case FieldNormalizer.Trim:
                return trimmed;
            case FieldNormalizer.CaseInsensitive:
                return trimmed.ToUpperInvariant();
            case FieldNormalizer.WhitespaceCollapse:
                return Whitespace().Replace(trimmed, " ").ToUpperInvariant();
            case FieldNormalizer.Email:
                return trimmed.ToLowerInvariant();
            case FieldNormalizer.PhoneDigits:
            {
                var digits = NonDigits().Replace(trimmed, string.Empty);
                return digits.Length > 10 && digits[0] == '1' ? digits[1..] : digits;
            }

            case FieldNormalizer.DateOnly:
                return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                    ? date.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : trimmed;
            case FieldNormalizer.Decimal:
                return decimal.TryParse(trimmed.Replace(",", string.Empty, StringComparison.Ordinal).TrimStart('$', '€', '£'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.####", CultureInfo.InvariantCulture)
                    : trimmed;
            default:
                return trimmed;
        }
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// Compares two canonical payloads field by field according to a comparison profile. Without a profile,
/// every field present on either side is compared with trim semantics and none is critical.
/// </summary>
public sealed class FieldComparer
{
    private readonly IReadOnlyList<ComparedField>? _fields;

    public FieldComparer(IReadOnlyList<ComparedField>? fields) => _fields = fields is { Count: > 0 } ? fields : null;

    public static FieldComparer CompareAll { get; } = new(null);

    public IReadOnlyList<FieldDifference> Compare(string leftPayloadJson, string rightPayloadJson)
    {
        var left = Parse(leftPayloadJson);
        var right = Parse(rightPayloadJson);
        var differences = new List<FieldDifference>();

        var fields = _fields ?? left.Select(kv => kv.Key).Union(right.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(name => new ComparedField(name, FieldNormalizer.Trim, false)).ToList();

        foreach (var field in fields)
        {
            var l = Get(left, field.Name);
            var r = Get(right, field.Name);
            if (!string.Equals(FieldNormalization.Normalize(l, field.Normalizer), FieldNormalization.Normalize(r, field.Normalizer), StringComparison.Ordinal))
            {
                differences.Add(new FieldDifference(field.Name, l, r, field.IsCritical));
            }
        }

        return differences;
    }

    private static JsonObject Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
    }

    private static string? Get(JsonObject obj, string name)
    {
        var match = obj.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
        return match.Key is null ? null : match.Value?.ToString();
    }
}
