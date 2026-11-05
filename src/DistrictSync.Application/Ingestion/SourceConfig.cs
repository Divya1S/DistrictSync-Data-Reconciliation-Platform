using System.Text.Json;
using DistrictSync.Application.Common;

namespace DistrictSync.Application.Ingestion;

public enum LoadMode
{
    /// <summary>Each batch is a complete picture of the source; records absent from the batch are soft-deleted.</summary>
    Snapshot = 1,

    /// <summary>Each batch carries only changes; absence means nothing.</summary>
    Delta = 2,
}

public sealed record CsvConfig(string Delimiter = ",", bool HasHeader = true);

public sealed record RestPullConfig(
    string Url,
    string ItemsPath = "$",
    string PageParam = "page",
    string PageSizeParam = "pageSize",
    int PageSize = 500,
    int MaxPages = 100_000,
    string? AuthHeaderName = null,
    string? AuthHeaderValue = null);

/// <summary>
/// Per-source mapping stored as JSON on <c>SourceSystem.ConfigJson</c>. Tells the ingestion pipeline which
/// incoming column is the record's own id, which one is the cross-system match key, and how to rename
/// columns into the canonical field names the comparison profile and rules refer to.
/// </summary>
public sealed record SourceConfig(
    string ExternalIdField = "id",
    string MatchKeyField = "email",
    string? UpdatedAtField = null,
    string? DeletedField = null,
    LoadMode LoadMode = LoadMode.Snapshot,
    Dictionary<string, string>? FieldMap = null,
    string[]? IgnoreFields = null,
    CsvConfig? Csv = null,
    RestPullConfig? Rest = null)
{
    public static SourceConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            return new SourceConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<SourceConfig>(json, JsonDefaults.Options) ?? new SourceConfig();
        }
        catch (JsonException ex)
        {
            throw ValidationException.For("configJson", $"Invalid source configuration: {ex.Message}");
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonDefaults.Options);
}
