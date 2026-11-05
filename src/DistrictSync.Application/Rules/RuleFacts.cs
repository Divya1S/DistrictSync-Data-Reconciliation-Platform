using System.Text.Json;
using System.Text.Json.Nodes;
using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Sources;

namespace DistrictSync.Application.Rules;

/// <summary>Everything a rule may inspect about one discrepancy, assembled into a single JSON fact bag.</summary>
public sealed record DiscrepancyContext(
    Discrepancy Discrepancy,
    SourceSystem LeftSource,
    SourceSystem RightSource,
    KeyedRecord? LeftRecord,
    KeyedRecord? RightRecord);

public static class RuleFacts
{
    /// <summary>
    /// Fact layout (all names case-insensitive in rules):
    /// <list type="bullet">
    /// <item><c>discrepancy.type</c>, <c>.severity</c>, <c>.matchKey</c>, <c>.entityType</c>, <c>.differenceCount</c></item>
    /// <item><c>fields</c>: list of differing field names, e.g. <c>fields.only("phone","address")</c></item>
    /// <item><c>diff.&lt;field&gt;.left</c> / <c>.right</c> / <c>.critical</c></item>
    /// <item><c>left.&lt;field&gt;</c>, <c>right.&lt;field&gt;</c> plus <c>_exists</c>, <c>_updatedAt</c>, <c>_externalId</c></item>
    /// <item><c>source.left.code</c> / <c>.trustRank</c>, <c>source.right.*</c></item>
    /// </list>
    /// </summary>
    public static JsonObject Build(DiscrepancyContext ctx, DateTimeOffset now)
    {
        var d = ctx.Discrepancy;
        var diff = new JsonObject();
        foreach (var difference in d.Differences)
        {
            diff[difference.Field] = new JsonObject
            {
                ["left"] = difference.LeftValue,
                ["right"] = difference.RightValue,
                ["critical"] = difference.IsCritical,
            };
        }

        return new JsonObject
        {
            ["_now"] = now.ToString("O"),
            ["discrepancy"] = new JsonObject
            {
                ["id"] = d.Id.ToString("D"),
                ["type"] = d.Type.ToString(),
                ["severity"] = d.Severity.ToString(),
                ["matchKey"] = d.MatchKey,
                ["entityType"] = d.EntityType,
                ["differenceCount"] = d.Differences.Count,
                ["hasCriticalDifference"] = d.Differences.Any(x => x.IsCritical),
            },
            ["fields"] = new JsonArray(d.Differences.Select(x => (JsonNode)x.Field).ToArray()),
            ["diff"] = diff,
            ["left"] = RecordFacts(ctx.LeftRecord),
            ["right"] = RecordFacts(ctx.RightRecord),
            ["source"] = new JsonObject
            {
                ["left"] = SourceFacts(ctx.LeftSource),
                ["right"] = SourceFacts(ctx.RightSource),
            },
        };
    }

    private static JsonObject SourceFacts(SourceSystem s) => new()
    {
        ["id"] = s.Id.ToString("D"),
        ["code"] = s.Code,
        ["name"] = s.Name,
        ["kind"] = s.Kind.ToString(),
        ["trustRank"] = s.TrustRank,
    };

    private static JsonObject RecordFacts(KeyedRecord? record)
    {
        var obj = new JsonObject { ["_exists"] = record is not null };
        if (record is null)
        {
            return obj;
        }

        obj["_externalId"] = record.ExternalId;
        obj["_updatedAt"] = record.SourceUpdatedAt?.ToString("O");
        try
        {
            if (JsonNode.Parse(record.PayloadJson) is JsonObject payload)
            {
                foreach (var (key, value) in payload)
                {
                    obj[key] = value?.DeepClone();
                }
            }
        }
        catch (JsonException)
        {
            // A malformed payload should not break rule evaluation; the record simply exposes no fields.
        }

        return obj;
    }
}
