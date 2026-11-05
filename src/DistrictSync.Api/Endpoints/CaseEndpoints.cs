using DistrictSync.Application.Cases;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Rules;

namespace DistrictSync.Api.Endpoints;

public static class CaseEndpoints
{
    public static RouteGroupBuilder MapCases(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/cases").WithTags("Cases").RequireAuthorization();

        group.MapGet("/", async (string? state, string? priority, string? assigneeId, bool? overdue, int? page, int? pageSize, CaseService cases, CancellationToken ct) =>
            Results.Ok(await cases.ListAsync(
                EndpointHelpers.ParseEnum<CaseState>(state, "state"),
                EndpointHelpers.ParseEnum<CasePriority>(priority, "priority"),
                assigneeId,
                overdue,
                EndpointHelpers.Page(page, pageSize),
                ct)));

        group.MapGet("/{id:guid}", async (Guid id, HttpResponse response, CaseService cases, CancellationToken ct) =>
        {
            var detail = await cases.GetAsync(id, ct);
            EndpointHelpers.SetETag(response, detail.Case.ETag);
            return Results.Ok(detail);
        });

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignCaseRequest request, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.AssignAsync(id, request, EndpointHelpers.IfMatch(http.Request), ct), clock)).RequireAuthorization("analyst");

        group.MapPost("/{id:guid}/propose", async (Guid id, ProposeResolutionRequest request, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.ProposeAsync(id, request, EndpointHelpers.IfMatch(http.Request), ct), clock)).RequireAuthorization("analyst");

        group.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.ApproveAsync(id, EndpointHelpers.IfMatch(http.Request), ct), clock)).RequireAuthorization("approver");

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectProposalRequest request, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.RejectAsync(id, request, EndpointHelpers.IfMatch(http.Request), ct), clock)).RequireAuthorization("approver");

        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelCaseRequest request, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.CancelAsync(id, request, EndpointHelpers.IfMatch(http.Request), ct), clock)).RequireAuthorization("analyst");

        group.MapPost("/{id:guid}/comments", async (Guid id, AddCommentRequest request, HttpContext http, CaseService cases, TimeProvider clock, CancellationToken ct) =>
            Respond(http, await cases.CommentAsync(id, request, ct), clock));

        return api;
    }

    private static IResult Respond(HttpContext http, ResolutionCase resolutionCase, TimeProvider clock)
    {
        var dto = Mappers.ToDto(resolutionCase, clock.GetUtcNow());
        EndpointHelpers.SetETag(http.Response, dto.ETag);
        return Results.Ok(dto);
    }
}
