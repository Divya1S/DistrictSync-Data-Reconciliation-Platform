using System.Text.Json;
using System.Text.Json.Serialization;

namespace DistrictSync.Application.Common;

public static class JsonDefaults
{
    /// <summary>Options for message payloads and stored JSON: compact, camelCase, enums as strings, tolerant of nulls.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>Canonical serialization for hashing: sorted keys, no whitespace, so equal content always hashes equal.</summary>
    public static string Canonicalize(IReadOnlyDictionary<string, string?> attributes)
    {
        var sorted = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            sorted[key] = value;
        }

        return JsonSerializer.Serialize(sorted, Options);
    }
}
