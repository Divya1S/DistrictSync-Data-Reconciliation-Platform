using DistrictSync.Application.Common;
using DistrictSync.Application.Rules;
using DistrictSync.Contracts.Api;

namespace DistrictSync.Api.Endpoints;

public static class RuleEndpoints
{
    public static RouteGroupBuilder MapRules(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/rules").WithTags("Rules").RequireAuthorization();

        group.MapGet("/sets", async (RuleSetService rules, CancellationToken ct) => Results.Ok(await rules.ListAsync(ct)));
        group.MapGet("/sets/{id:guid}", async (Guid id, RuleSetService rules, CancellationToken ct) => Results.Ok(await rules.GetAsync(id, ct)));

        group.MapPost("/sets", async (CreateRuleSetRequest request, RuleSetService rules, CancellationToken ct) =>
        {
            var set = await rules.CreateDraftAsync(request, ct);
            return Results.Created($"/api/v1/rules/sets/{set.Id}", Mappers.ToDto(set));
        }).RequireAuthorization("admin");

        group.MapPost("/sets/{id:guid}/rules", async (Guid id, UpsertRuleRequest request, RuleSetService rules, CancellationToken ct) =>
        {
            var rule = await rules.AddRuleAsync(id, request, ct);
            return Results.Created($"/api/v1/rules/sets/{id}/rules/{rule.Id}", Mappers.ToDto(rule));
        }).RequireAuthorization("admin");

        group.MapPut("/sets/{id:guid}/rules/{ruleId:guid}", async (Guid id, Guid ruleId, UpsertRuleRequest request, RuleSetService rules, CancellationToken ct) =>
        {
            await rules.UpdateRuleAsync(id, ruleId, request, ct);
            return Results.NoContent();
        }).RequireAuthorization("admin");

        group.MapDelete("/sets/{id:guid}/rules/{ruleId:guid}", async (Guid id, Guid ruleId, RuleSetService rules, CancellationToken ct) =>
        {
            await rules.RemoveRuleAsync(id, ruleId, ct);
            return Results.NoContent();
        }).RequireAuthorization("admin");

        group.MapPost("/sets/{id:guid}/publish", async (Guid id, PublishRuleSetRequest request, RuleSetService rules, CancellationToken ct) =>
            Results.Ok(Mappers.ToDto(await rules.PublishAsync(id, request, ct)))).RequireAuthorization("admin");

        group.MapPost("/sets/{id:guid}/next-version", async (Guid id, RuleSetService rules, CancellationToken ct) =>
        {
            var next = await rules.CreateNextVersionAsync(id, ct);
            return Results.Created($"/api/v1/rules/sets/{next.Id}", Mappers.ToDto(next));
        }).RequireAuthorization("admin");

        group.MapPost("/validate", (ValidateRuleRequest request) => Results.Ok(RuleSetService.Validate(request.Condition ?? string.Empty)))
            .WithSummary("Parse a rule condition and report syntax errors and referenced facts without saving anything.");

        group.MapPost("/sets/{id:guid}/dry-run", async (Guid id, DryRunRequest request, RuleSetService rules, CancellationToken ct) =>
            Results.Ok(await rules.DryRunAsync(id, request.DiscrepancyId, ct)))
            .WithSummary("Evaluate a rule set against an existing discrepancy and show which rule would fire.");

        return api;
    }
}
