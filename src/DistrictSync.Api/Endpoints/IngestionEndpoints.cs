using DistrictSync.Application.Common;
using DistrictSync.Application.Ingestion;
using DistrictSync.Application.Sources;
using DistrictSync.Application.Tenancy;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Ingestion;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace DistrictSync.Api.Endpoints;

public static class IngestionEndpoints
{
    public static RouteGroupBuilder MapIngestion(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/ingestion").WithTags("Ingestion");

        group.MapGet("/batches", async (string? status, Guid? sourceSystemId, int? page, int? pageSize, IngestionService ingestion, CancellationToken ct) =>
            Results.Ok(await ingestion.ListAsync(EndpointHelpers.ParseEnum<IngestionBatchStatus>(status, "status"), sourceSystemId, EndpointHelpers.Page(page, pageSize), ct)))
            .RequireAuthorization();

        group.MapGet("/batches/{id:guid}", async (Guid id, IngestionService ingestion, CancellationToken ct) => Results.Ok(await ingestion.GetAsync(id, ct)))
            .RequireAuthorization();

        group.MapPost("/batches/csv", async (HttpContext http, Guid sourceSystemId, string entityType, IFormFile file, IngestionService ingestion, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                throw ValidationException.For("file", "The uploaded file is empty.");
            }

            await using var stream = file.OpenReadStream();
            var batch = await ingestion.ReceiveCsvAsync(sourceSystemId, entityType, stream, file.FileName, ct);
            var dto = Mappers.ToDto(batch, await ingestion.SourceCodesAsync(ct));
            return Results.Accepted($"/api/v1/ingestion/batches/{batch.Id}", dto);
        })
        .RequireAuthorization("analyst")
        .RequireRateLimiting("ingestion")
        .DisableAntiforgery()
        .WithMetadata(new DisableRequestSizeLimitAttribute())
        .WithSummary("Upload a CSV. The file is staged and processed asynchronously by a worker; progress streams over SignalR.");

        group.MapPost("/batches/{id:guid}/retry", async (Guid id, IngestionService ingestion, CancellationToken ct) =>
        {
            var batch = await ingestion.RetryAsync(id, ct);
            return Results.Accepted($"/api/v1/ingestion/batches/{batch.Id}", Mappers.ToDto(batch, await ingestion.SourceCodesAsync(ct)));
        }).RequireAuthorization("analyst");

        group.MapPost("/pulls", async (StartPullRequest request, IngestionService ingestion, CancellationToken ct) =>
        {
            var batch = await ingestion.StartPullAsync(request.SourceSystemId, request.EntityType, ct);
            return Results.Accepted($"/api/v1/ingestion/batches/{batch.Id}", Mappers.ToDto(batch, await ingestion.SourceCodesAsync(ct)));
        }).RequireAuthorization("analyst").RequireRateLimiting("ingestion");

        // Webhook: authenticated by per-source API key, not by JWT. The source row binds the key to its tenant.
        group.MapPost("/webhooks/{sourceSystemId:guid}", async (Guid sourceSystemId, HttpRequest request, WebhookIngestRequest body, SourceSystemService sources, IngestionService ingestion, TenantContext tenant, CancellationToken ct) =>
        {
            var apiKey = request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(apiKey))
            {
                return Results.Unauthorized();
            }

            var source = await sources.AuthenticateWebhookAsync(sourceSystemId, apiKey, ct);
            if (source is null)
            {
                return Results.Unauthorized();
            }

            tenant.SetSystemForTenant(source.TenantId, $"webhook:{source.Code}");
            return Results.Ok(await ingestion.IngestWebhookAsync(source, body, ct));
        })
        .AllowAnonymous()
        .RequireRateLimiting("ingestion")
        .WithSummary("Push records from an external system. Requires the source's X-Api-Key.");

        return api;
    }
}
