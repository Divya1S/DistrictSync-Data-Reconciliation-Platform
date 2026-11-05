using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Application.Workflows;

/// <summary>
/// Pushes the agreed outcome to the losing system via the write-back connector. Transient connector
/// failures throw <see cref="WorkflowRetryException"/> so the runner retries with backoff; the connector
/// call is idempotent on the discrepancy id, so a crash after the call but before persistence is harmless.
/// </summary>
public sealed class ApplyResolutionWorkflow(IAppDbContext db, IWriteBackConnector connector, ILogger<ApplyResolutionWorkflow> logger) : IWorkflowDefinition
{
    public string Type => WorkflowTypes.ApplyResolution;

    public async Task<WorkflowStep> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var state = context.State<ApplyResolutionState>();
        var d = await db.Discrepancies.FirstOrDefaultAsync(x => x.Id == state.DiscrepancyId, ct);
        if (d is null)
        {
            logger.LogWarning("Apply-resolution workflow {WorkflowId}: discrepancy {DiscrepancyId} not found", context.Instance.Id, state.DiscrepancyId);
            return WorkflowStep.Done(state);
        }

        ResolutionCase? resolutionCase = null;
        if (state.CaseId is not null)
        {
            resolutionCase = await db.Cases.Include(c => c.History).FirstOrDefaultAsync(c => c.Id == state.CaseId, ct);
            if (resolutionCase is null || resolutionCase.State != CaseState.Approved)
            {
                // Already resolved by a previous attempt, or cancelled meanwhile: nothing left to apply.
                return WorkflowStep.Done(state with { LastOutcome = "skipped: case not in Approved state" });
            }
        }
        else if (d.Status != DiscrepancyStatus.AutoResolved)
        {
            return WorkflowStep.Done(state with { LastOutcome = "skipped: discrepancy not auto-resolved" });
        }

        var left = await db.SourceSystems.AsNoTracking().FirstAsync(s => s.Id == d.LeftSourceSystemId, ct);
        var right = await db.SourceSystems.AsNoTracking().FirstAsync(s => s.Id == d.RightSourceSystemId, ct);
        var ids = new[] { d.LeftRecordId, d.RightRecordId }.Where(x => x is not null).Select(x => x!.Value).ToList();
        var records = await db.SourceRecords.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new KeyedRecord(r.Id, r.MatchKey, r.ExternalId, r.PayloadJson, r.SourceUpdatedAt)).ToListAsync(ct);
        var ctx = new DiscrepancyContext(d, left, right, records.FirstOrDefault(r => r.Id == d.LeftRecordId), records.FirstOrDefault(r => r.Id == d.RightRecordId));

        ResolutionOutcome outcome;
        if (state.ManualValuesJson is not null)
        {
            var target = state.TargetSourceSystemId ?? (ctx.LeftSource.TrustRank >= ctx.RightSource.TrustRank ? ctx.RightSource.Id : ctx.LeftSource.Id);
            outcome = ConflictResolver.Manual(ctx, state.ManualValuesJson, target);
        }
        else
        {
            outcome = ConflictResolver.Resolve(state.Strategy ?? Domain.Rules.ResolutionStrategy.PreferHigherTrust, ctx);
        }

        if (outcome.Action != WriteBackAction.NoOp && outcome.TargetSourceSystemId is not null)
        {
            var request = new WriteBackRequest(d.TenantId, outcome.TargetSourceSystemId.Value, d.EntityType, d.MatchKey, outcome.Action, outcome.PayloadJson, $"discrepancy:{d.Id:D}", outcome.Explanation);
            var result = await connector.ApplyAsync(request, ct);
            if (!result.Succeeded)
            {
                if (result.IsTransient)
                {
                    throw new WorkflowRetryException($"Write-back to {outcome.TargetSourceSystemId} failed transiently: {result.Message}");
                }

                logger.LogError("Write-back for discrepancy {DiscrepancyId} failed permanently: {Message}", d.Id, result.Message);
                if (resolutionCase is not null)
                {
                    resolutionCase.Comment($"Write-back failed permanently: {result.Message}", "system", context.Now);
                }

                return WorkflowStep.Done(state with { LastOutcome = $"failed: {result.Message}" });
            }
        }

        if (resolutionCase is not null)
        {
            resolutionCase.MarkResolved(outcome.Explanation, context.Now);
            if (d.Status is DiscrepancyStatus.CaseOpened or DiscrepancyStatus.Open)
            {
                d.Resolve(outcome.Explanation, context.Now);
            }
        }

        return WorkflowStep.Done(state with { TargetSourceSystemId = outcome.TargetSourceSystemId, LastOutcome = outcome.Explanation });
    }
}
