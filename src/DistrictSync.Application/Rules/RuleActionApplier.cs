using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Workflows;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;

namespace DistrictSync.Application.Rules;

public enum AppliedActionKind
{
    AutoResolved = 1,
    CaseOpened = 2,
    Ignored = 3,
}

public sealed record AppliedAction(AppliedActionKind Kind, Guid? CaseId, Guid? RuleId);

/// <summary>State carried by the apply-resolution workflow.</summary>
public sealed record ApplyResolutionState(Guid DiscrepancyId, Guid? CaseId, ResolutionStrategy? Strategy, string? ManualValuesJson, Guid? TargetSourceSystemId, string? LastOutcome);

/// <summary>State carried by the case SLA workflow.</summary>
public sealed record CaseSlaState(Guid CaseId, int EscalationsObserved);

/// <summary>
/// Turns a rule decision into state changes: auto-resolutions, cases plus their SLA timers, or ignores.
/// Everything happens inside the caller's unit of work, so the discrepancy status, the case, the
/// workflow timer and the outbox events commit together or not at all.
/// </summary>
public sealed class RuleActionApplier(IAppDbContext db, WorkflowScheduler scheduler, TimeProvider clock)
{
    public AppliedAction Apply(DiscrepancyContext ctx, RuleEvaluation evaluation)
    {
        var now = clock.GetUtcNow();
        var discrepancy = ctx.Discrepancy;
        var ruleId = evaluation.Matched?.Id;
        var action = evaluation.Action;

        switch (action.Kind)
        {
            case RuleActionKind.Ignore:
                discrepancy.Ignore(ruleId, action.Note ?? "Ignored by rule.", now);
                return new AppliedAction(AppliedActionKind.Ignored, null, ruleId);

            case RuleActionKind.AutoResolve:
            {
                var strategy = action.Strategy ?? ResolutionStrategy.PreferHigherTrust;
                var outcome = ConflictResolver.Resolve(strategy, ctx);
                if (outcome.Action == WriteBackAction.NoOp)
                {
                    // The rule wanted automation but the data does not allow a safe decision: fail over to a human.
                    return OpenCase(ctx, CasePriority.High, action.AssignToRole, ruleId, $"Auto-resolution not possible ({outcome.Explanation}).", now);
                }

                discrepancy.AutoResolve(ruleId ?? Guid.Empty, outcome.Explanation, now);
                scheduler.Schedule(
                    discrepancy.TenantId,
                    WorkflowTypes.ApplyResolution,
                    discrepancy.Id,
                    new ApplyResolutionState(discrepancy.Id, null, strategy, null, outcome.TargetSourceSystemId, null),
                    now);
                return new AppliedAction(AppliedActionKind.AutoResolved, null, ruleId);
            }

            case RuleActionKind.OpenCase:
            default:
                return OpenCase(ctx, action.Priority ?? CasePriority.Normal, action.AssignToRole, ruleId, action.Note, now);
        }
    }

    private AppliedAction OpenCase(DiscrepancyContext ctx, CasePriority priority, string? role, Guid? ruleId, string? note, DateTimeOffset now)
    {
        var d = ctx.Discrepancy;
        var title = d.Type switch
        {
            DiscrepancyType.FieldConflict => $"{d.EntityType} {d.MatchKey}: {string.Join(", ", d.Differences.Select(x => x.Field))} differ between {ctx.LeftSource.Code} and {ctx.RightSource.Code}",
            DiscrepancyType.MissingInLeft => $"{d.EntityType} {d.MatchKey}: present in {ctx.RightSource.Code}, missing in {ctx.LeftSource.Code}",
            DiscrepancyType.MissingInRight => $"{d.EntityType} {d.MatchKey}: present in {ctx.LeftSource.Code}, missing in {ctx.RightSource.Code}",
            DiscrepancyType.Duplicate => $"{d.EntityType} {d.MatchKey}: duplicate records within one source",
            _ => $"{d.EntityType} {d.MatchKey}: {d.Type}",
        };

        var resolutionCase = ResolutionCase.Open(d.TenantId, d.Id, d.RunId, title.Length > 300 ? title[..300] : title, priority, role, ruleId, now);
        if (note is not null)
        {
            resolutionCase.Comment(note, "system", now);
        }

        d.EscalateToCase(ruleId);
        db.Cases.Add(resolutionCase);
        scheduler.Schedule(d.TenantId, WorkflowTypes.CaseSla, resolutionCase.Id, new CaseSlaState(resolutionCase.Id, 0), resolutionCase.DueAt);
        return new AppliedAction(AppliedActionKind.CaseOpened, resolutionCase.Id, ruleId);
    }
}
