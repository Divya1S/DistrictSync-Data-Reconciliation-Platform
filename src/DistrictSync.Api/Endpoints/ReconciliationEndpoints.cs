using DistrictSync.Application.Common;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Reconciliation;

namespace DistrictSync.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static RouteGroupBuilder MapReconciliation(this RouteGroupBuilder api)
    {
        var runs = api.MapGroup("/reconciliation/runs").WithTags("Reconciliation").RequireAuthorization();

        runs.MapPost("/", async (CreateReconciliationRunRequest request, ReconciliationService service, CancellationToken ct) =>
        {
            var run = await service.RequestRunAsync(request, ct);
            return Results.Accepted($"/api/v1/reconciliation/runs/{run.Id}", Mappers.ToDto(run, await service.SourceCodesAsync(ct)));
        }).RequireAuthorization("analyst");

        runs.MapGet("/", async (string? status, int? page, int? pageSize, ReconciliationService service, CancellationToken ct) =>
            Results.Ok(await service.ListRunsAsync(EndpointHelpers.ParseEnum<ReconciliationRunStatus>(status, "status"), EndpointHelpers.Page(page, pageSize), ct)));

        runs.MapGet("/{id:guid}", async (Guid id, ReconciliationService service, CancellationToken ct) => Results.Ok(await service.GetRunAsync(id, ct)));

        runs.MapPost("/{id:guid}/cancel", async (Guid id, ReconciliationService service, CancellationToken ct) =>
        {
            await service.CancelAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("analyst");

        runs.MapGet("/{id:guid}/discrepancies", async (Guid id, string? type, string? status, string? severity, string? matchKey, int? page, int? pageSize, ReconciliationService service, CancellationToken ct) =>
            Results.Ok(await service.ListDiscrepanciesAsync(
                id,
                EndpointHelpers.ParseEnum<DiscrepancyType>(type, "type"),
                EndpointHelpers.ParseEnum<DiscrepancyStatus>(status, "status"),
                EndpointHelpers.ParseEnum<Severity>(severity, "severity"),
                matchKey,
                EndpointHelpers.Page(page, pageSize),
                ct)));

        var discrepancies = api.MapGroup("/discrepancies").WithTags("Reconciliation").RequireAuthorization();

        discrepancies.MapGet("/", async (Guid? runId, string? type, string? status, string? severity, string? matchKey, int? page, int? pageSize, ReconciliationService service, CancellationToken ct) =>
            Results.Ok(await service.ListDiscrepanciesAsync(
                runId,
                EndpointHelpers.ParseEnum<DiscrepancyType>(type, "type"),
                EndpointHelpers.ParseEnum<DiscrepancyStatus>(status, "status"),
                EndpointHelpers.ParseEnum<Severity>(severity, "severity"),
                matchKey,
                EndpointHelpers.Page(page, pageSize),
                ct)));

        discrepancies.MapGet("/{id:guid}", async (Guid id, ReconciliationService service, CancellationToken ct) => Results.Ok(await service.GetDiscrepancyAsync(id, ct)));

        var profiles = api.MapGroup("/comparison-profiles").WithTags("Reconciliation").RequireAuthorization();
        profiles.MapGet("/", async (ReconciliationService service, CancellationToken ct) => Results.Ok(await service.ListProfilesAsync(ct)));
        profiles.MapPost("/", async (CreateComparisonProfileRequest request, ReconciliationService service, CancellationToken ct) =>
        {
            var profile = await service.CreateProfileAsync(request, ct);
            return Results.Created($"/api/v1/comparison-profiles/{profile.Id}", Mappers.ToDto(profile));
        }).RequireAuthorization("admin");

        return api;
    }
}
