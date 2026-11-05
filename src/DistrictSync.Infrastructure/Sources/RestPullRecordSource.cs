using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Ingestion;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Infrastructure.Sources;

/// <summary>
/// Pages through a JSON REST endpoint (<c>?page=N&amp;pageSize=M</c>) and yields each item as a flat record.
/// Resilience (retries, timeouts, circuit breaker) comes from the host's standard HttpClient pipeline.
/// </summary>
public sealed class RestPullRecordSource(IHttpClientFactory httpClientFactory, ILogger<RestPullRecordSource> logger) : IRawRecordSource
{
    public const string ClientName = "rest-pull";

    public async IAsyncEnumerable<RawRecord> ReadAsync(IngestionBatch batch, SourceSystem source, [EnumeratorCancellation] CancellationToken ct)
    {
        var config = SourceConfig.Parse(source.ConfigJson).Rest ?? throw new InvalidOperationException($"Source {source.Code} has no REST configuration.");
        var client = httpClientFactory.CreateClient(ClientName);
        var line = 0L;

        for (var page = 1; page <= config.MaxPages; page++)
        {
            var separator = config.Url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var url = $"{config.Url}{separator}{Uri.EscapeDataString(config.PageParam)}={page}&{Uri.EscapeDataString(config.PageSizeParam)}={config.PageSize}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(config.AuthHeaderName) && !string.IsNullOrEmpty(config.AuthHeaderValue))
            {
                request.Headers.TryAddWithoutValidation(config.AuthHeaderName, config.AuthHeaderValue);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var root = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var items = SelectItems(root, config.ItemsPath);
            if (items is null || items.Count == 0)
            {
                yield break;
            }

            foreach (var item in items)
            {
                if (item is not JsonObject obj)
                {
                    continue;
                }

                line++;
                var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in obj)
                {
                    attributes[key] = value switch
                    {
                        null => null,
                        JsonValue v => v.ToString(),
                        _ => value.ToJsonString(),
                    };
                }

                yield return new RawRecord(null, attributes, null, false, line);
            }

            logger.LogDebug("REST pull {Source} page {Page}: {Count} items", source.Code, page, items.Count);
            if (items.Count < config.PageSize)
            {
                yield break;
            }
        }
    }

    /// <summary>Supports "$" (root array) and "$.a.b" (nested property holding the array).</summary>
    private static JsonArray? SelectItems(JsonNode? root, string path)
    {
        if (root is null)
        {
            return null;
        }

        if (path is "$" or "")
        {
            return root as JsonArray;
        }

        var node = root;
        foreach (var segment in path.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonObject obj ? obj[segment] : null;
            if (node is null)
            {
                return null;
            }
        }

        return node as JsonArray;
    }
}

public sealed class RawRecordSourceResolver(CsvRecordSource csv, RestPullRecordSource rest) : IRawRecordSourceResolver
{
    public IRawRecordSource Resolve(IngestionBatch batch, SourceSystem source) =>
        batch.StagingReference is not null && batch.StagingReference.StartsWith("rest://", StringComparison.Ordinal) ? rest : csv;
}
