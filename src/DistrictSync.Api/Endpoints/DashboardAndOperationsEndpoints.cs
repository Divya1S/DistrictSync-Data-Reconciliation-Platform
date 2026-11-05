using DistrictSync.Application.Dashboard;
using DistrictSync.Application.Operations;
using DistrictSync.Domain.Workflows;

namespace DistrictSync.Api.Endpoints;

public static class DashboardAndOperationsEndpoints
{
    public static RouteGroupBuilder MapDashboard(this RouteGroupBuilder api)
    {
        api.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization()
            .MapGet("/summary", async (DashboardQueryService dashboard, CancellationToken ct) => Results.Ok(await dashboard.GetSummaryAsync(ct)));
        return api;
    }

    public static RouteGroupBuilder MapOperations(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/operations").WithTags("Operations").RequireAuthorization();

        group.MapGet("/dead-letters", async (bool? includeReplayed, int? page, int? pageSize, OperationsService ops, CancellationToken ct) =>
            Results.Ok(await ops.ListDeadLettersAsync(includeReplayed ?? false, EndpointHelpers.Page(page, pageSize), ct)));

        group.MapPost("/dead-letters/{id:guid}/replay", async (Guid id, OperationsService ops, CancellationToken ct) =>
        {
            await ops.ReplayDeadLetterAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("admin");

        group.MapGet("/outbox", async (OperationsService ops, CancellationToken ct) => Results.Ok(await ops.GetOutboxHealthAsync(ct)));

        group.MapGet("/workflows", async (string? status, int? page, int? pageSize, OperationsService ops, CancellationToken ct) =>
            Results.Ok(await ops.ListWorkflowsAsync(EndpointHelpers.ParseEnum<WorkflowStatus>(status, "status"), EndpointHelpers.Page(page, pageSize), ct)));

        return api;
    }
}
