using DistrictSync.Application.Abstractions;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Dashboard;

/// <summary>Aggregates for the operations dashboard. A handful of indexed count queries; no entity materialisation.</summary>
public sealed class DashboardQueryService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var since = today.AddDays(-13);
        var tenantId = tenant.TenantId;

        var ingestionInProgress = await db.IngestionBatches.CountAsync(b => b.Status == IngestionBatchStatus.Received || b.Status == IngestionBatchStatus.Processing, ct);
        var ingestionCompletedToday = await db.IngestionBatches.CountAsync(b => b.Status == IngestionBatchStatus.Completed && b.CompletedAt >= today, ct);
        var ingestionFailedToday = await db.IngestionBatches.CountAsync(b => b.Status == IngestionBatchStatus.Failed && b.CompletedAt >= today, ct);
        var recordsToday = await db.IngestionBatches.Where(b => b.CompletedAt >= today).SumAsync(b => (long)b.AcceptedRecords, ct);

        var reconRunning = await db.ReconciliationRuns.CountAsync(r => r.Status == ReconciliationRunStatus.Queued || r.Status == ReconciliationRunStatus.Running, ct);
        var reconCompletedToday = await db.ReconciliationRuns.CountAsync(r => r.Status == ReconciliationRunStatus.Completed && r.CompletedAt >= today, ct);

        var openDiscrepancies = await db.Discrepancies.LongCountAsync(d => d.Status == DiscrepancyStatus.Open || d.Status == DiscrepancyStatus.CaseOpened, ct);
        var autoResolvedToday = await db.Discrepancies.LongCountAsync(d => d.Status == DiscrepancyStatus.AutoResolved && d.ResolvedAt >= today, ct);

        var casesOpen = await db.Cases.LongCountAsync(c => c.State != CaseState.Resolved && c.State != CaseState.Cancelled, ct);
        var casesOverdue = await db.Cases.LongCountAsync(c => c.State != CaseState.Resolved && c.State != CaseState.Cancelled && c.DueAt < now, ct);
        var casesResolvedToday = await db.Cases.LongCountAsync(c => c.State == CaseState.Resolved && c.ClosedAt >= today, ct);

        var deadLetters = await db.DeadLetters.CountAsync(m => m.TenantId == tenantId && m.ReplayedAt == null, ct);
        var outboxPending = await db.OutboxMessages.CountAsync(m => m.TenantId == tenantId && m.ProcessedAt == null, ct);

        var byType = await db.Discrepancies
            .Where(d => d.Status == DiscrepancyStatus.Open || d.Status == DiscrepancyStatus.CaseOpened)
            .GroupBy(d => d.Type)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var byPriority = await db.Cases
            .Where(c => c.State != CaseState.Resolved && c.State != CaseState.Cancelled)
            .GroupBy(c => c.Priority)
            .Select(g => new { Key = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var detected = await db.Discrepancies
            .Where(d => d.DetectedAt >= since)
            .GroupBy(d => d.DetectedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var resolved = await db.Discrepancies
            .Where(d => d.ResolvedAt != null && d.ResolvedAt >= since)
            .GroupBy(d => d.ResolvedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        var days = Enumerable.Range(0, 14).Select(i => since.AddDays(i).UtcDateTime.Date).Select(day => new DailyCountDto(
            DateOnly.FromDateTime(day),
            detected.FirstOrDefault(x => x.Day == day)?.Count ?? 0,
            resolved.FirstOrDefault(x => x.Day == day)?.Count ?? 0)).ToList();

        return new DashboardSummaryDto(
            ingestionInProgress, ingestionCompletedToday, ingestionFailedToday, recordsToday,
            reconRunning, reconCompletedToday,
            openDiscrepancies, autoResolvedToday,
            casesOpen, casesOverdue, casesResolvedToday,
            deadLetters, outboxPending,
            byType.Select(x => new CountByKeyDto(x.Key.ToString(), x.Count)).ToList(),
            byPriority.Select(x => new CountByKeyDto(x.Key.ToString(), x.Count)).ToList(),
            days);
    }
}
