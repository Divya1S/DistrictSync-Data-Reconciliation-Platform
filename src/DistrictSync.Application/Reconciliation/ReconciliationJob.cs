using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Messaging;
using DistrictSync.Application.Rules;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Application.Reconciliation;

/// <summary>
/// Worker-side orchestration of one reconciliation run: stream both sides, merge, evaluate rules on each
/// discrepancy, persist in checkpointed batches. A crash resumes from the last checkpointed key.
/// </summary>
public sealed class ReconciliationJob(
    IAppDbContext db,
    ISourceRecordStreamReader reader,
    IActiveRuleSetProvider ruleSets,
    RuleEngine ruleEngine,
    RuleActionApplier applier,
    IEphemeralEventPublisher ephemeral,
    TimeProvider clock,
    ILogger<ReconciliationJob> logger)
{
    public const int PageSize = 5_000;
    public const int FlushThreshold = 500;

    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            logger.LogWarning("Reconciliation run {RunId} not found; message ignored", runId);
            return;
        }

        if (run.Status is not (ReconciliationRunStatus.Queued or ReconciliationRunStatus.Running))
        {
            logger.LogInformation("Reconciliation run {RunId} is {Status}; nothing to do", runId, run.Status);
            return;
        }

        var tenantId = run.TenantId;
        var entityType = run.EntityType;
        var left = await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == run.LeftSourceSystemId, ct);
        var right = await db.SourceSystems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == run.RightSourceSystemId, ct);
        if (left is null || right is null)
        {
            run.Fail($"Source system {(left is null ? run.LeftSourceSystemId : run.RightSourceSystemId):D} does not exist or is not visible to this tenant.", clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return;
        }

        var comparer = await ResolveComparerAsync(run, ct);
        var ruleSet = await ruleSets.GetActiveAsync(tenantId, entityType, ct);

        run.Start(ruleSet?.Id, ruleSet?.Version, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        var checkpoint = run.Checkpoint;
        var stats = run.Stats;
        if (checkpoint is not null)
        {
            logger.LogInformation("Resuming run {RunId} after key {Checkpoint}", runId, checkpoint);
        }

        db.ClearTracking();

        var engine = new ReconciliationEngine(comparer);
        var pending = new List<DiscrepancyContext>(FlushThreshold);
        string? lastCompletedKey = checkpoint;
        string? currentKey = null;
        long matched = stats.Matched, missingLeft = stats.MissingInLeft, missingRight = stats.MissingInRight, conflicts = stats.FieldConflicts, duplicates = stats.Duplicates;
        long autoResolved = stats.AutoResolved, casesOpened = stats.CasesOpened, ignored = stats.Ignored;
        var leftBase = stats.LeftRecords;
        var rightBase = stats.RightRecords;

        try
        {
            var leftStream = reader.StreamAsync(tenantId, left.Id, entityType, checkpoint, PageSize, ct);
            var rightStream = reader.StreamAsync(tenantId, right.Id, entityType, checkpoint, PageSize, ct);

            await foreach (var outcome in engine.MergeAsync(leftStream, rightStream, ct))
            {
                if (currentKey is not null && !string.Equals(outcome.MatchKey, currentKey, StringComparison.Ordinal))
                {
                    // Key boundary: everything up to currentKey is fully known, so it is a safe checkpoint.
                    lastCompletedKey = currentKey;
                    if (pending.Count >= FlushThreshold)
                    {
                        await FlushAsync();
                    }
                }

                currentKey = outcome.MatchKey;
                var now = clock.GetUtcNow();

                switch (outcome)
                {
                    case MatchedOutcome:
                        matched++;
                        break;
                    case ConflictOutcome c:
                        conflicts++;
                        pending.Add(new DiscrepancyContext(
                            Discrepancy.Detect(tenantId, runId, entityType, DiscrepancyType.FieldConflict, c.MatchKey, left.Id, right.Id, c.Left.Id, c.Right.Id, c.Differences, now),
                            left, right, c.Left, c.Right));
                        break;
                    case MissingOutcome m when m.MissingIn == StreamSide.Left:
                        missingLeft++;
                        pending.Add(new DiscrepancyContext(
                            Discrepancy.Detect(tenantId, runId, entityType, DiscrepancyType.MissingInLeft, m.MatchKey, left.Id, right.Id, null, m.Present.Id, [], now),
                            left, right, null, m.Present));
                        break;
                    case MissingOutcome m:
                        missingRight++;
                        pending.Add(new DiscrepancyContext(
                            Discrepancy.Detect(tenantId, runId, entityType, DiscrepancyType.MissingInRight, m.MatchKey, left.Id, right.Id, m.Present.Id, null, [], now),
                            left, right, m.Present, null));
                        break;
                    case DuplicateOutcome d:
                        duplicates++;
                        var differences = d.Records.Select(r => new FieldDifference("_externalId", d.Side == StreamSide.Left ? r.ExternalId : null, d.Side == StreamSide.Right ? r.ExternalId : null, false)).ToList();
                        var first = d.Records[0];
                        pending.Add(new DiscrepancyContext(
                            Discrepancy.Detect(tenantId, runId, entityType, DiscrepancyType.Duplicate, d.MatchKey, left.Id, right.Id, d.Side == StreamSide.Left ? first.Id : null, d.Side == StreamSide.Right ? first.Id : null, differences, now),
                            left, right, d.Side == StreamSide.Left ? first : null, d.Side == StreamSide.Right ? first : null));
                        break;
                }
            }

            lastCompletedKey = currentKey ?? lastCompletedKey;
            await FlushAsync();

            run = await ReloadAsync();
            run.Complete(Snapshot(), clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Run {RunId} completed: {Stats}", runId, run.Stats);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reconciliation run {RunId} failed", runId);
            db.ClearTracking();
            run = await ReloadAsync();
            run.Fail(ex.Message, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        ReconciliationStats Snapshot() => new(
            leftBase + engine.LeftRead, rightBase + engine.RightRead, matched, missingLeft, missingRight, conflicts, duplicates, autoResolved, casesOpened, ignored);

        async Task FlushAsync()
        {
            var now = clock.GetUtcNow();
            foreach (var ctx in pending)
            {
                var facts = RuleFacts.Build(ctx, now);
                var evaluation = ruleEngine.Evaluate(ruleSet, facts);
                var applied = applier.Apply(ctx, evaluation);
                switch (applied.Kind)
                {
                    case AppliedActionKind.AutoResolved:
                        autoResolved++;
                        break;
                    case AppliedActionKind.CaseOpened:
                        casesOpened++;
                        break;
                    case AppliedActionKind.Ignored:
                        ignored++;
                        break;
                }

                db.Discrepancies.Add(ctx.Discrepancy);
            }

            var current = await ReloadAsync();
            current.SaveCheckpoint(lastCompletedKey ?? string.Empty, Snapshot());
            await db.SaveChangesAsync(ct);
            db.ClearTracking();
            pending.Clear();

            await ephemeral.PublishAsync(
                new ReconciliationProgressEvent(EventIds.Next(), tenantId.Value, now, runId, leftBase + engine.LeftRead, rightBase + engine.RightRead, missingLeft + missingRight + conflicts + duplicates, lastCompletedKey),
                ct);
        }

        async Task<ReconciliationRun> ReloadAsync() => await db.ReconciliationRuns.FirstAsync(r => r.Id == runId, ct);
    }

    private async Task<FieldComparer> ResolveComparerAsync(ReconciliationRun run, CancellationToken ct)
    {
        ComparisonProfile? profile = null;
        if (run.ComparisonProfileId is not null)
        {
            profile = await db.ComparisonProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == run.ComparisonProfileId, ct);
        }

        profile ??= await db.ComparisonProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.EntityType == run.EntityType && p.IsDefault, ct);
        return profile is null ? FieldComparer.CompareAll : new FieldComparer(profile.Fields.ToList());
    }
}
