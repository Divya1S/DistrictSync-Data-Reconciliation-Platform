using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Ingestion;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.EventHandlers;

/// <summary>Long-running: claims the inbox row first, then streams the file with its own chunked transactions.</summary>
public sealed class IngestionBatchReceivedHandler(IngestionBatchProcessor processor, ITenantWorkLimiter limiter, IAppDbContext db) : IIntegrationEventHandler<IngestionBatchReceivedEvent>
{
    public HandlerMode Mode => HandlerMode.LongRunning;

    public async Task HandleAsync(IngestionBatchReceivedEvent e, CancellationToken ct)
    {
        var tenantId = new TenantId(e.TenantId);
        var quotas = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.Quotas).FirstOrDefaultAsync(ct) ?? TenantQuotas.Default;
        using var slot = await limiter.AcquireAsync(tenantId, "ingestion", quotas.MaxConcurrentIngestions, ct);
        await processor.ProcessAsync(e.BatchId, ct);
    }
}

public sealed class ReconciliationRequestedHandler(ReconciliationJob job, ITenantWorkLimiter limiter, IAppDbContext db) : IIntegrationEventHandler<ReconciliationRequestedEvent>
{
    public HandlerMode Mode => HandlerMode.LongRunning;

    public async Task HandleAsync(ReconciliationRequestedEvent e, CancellationToken ct)
    {
        var tenantId = new TenantId(e.TenantId);
        var quotas = await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.Quotas).FirstOrDefaultAsync(ct) ?? TenantQuotas.Default;
        using var slot = await limiter.AcquireAsync(tenantId, "reconciliation", quotas.MaxConcurrentReconciliations, ct);
        await job.RunAsync(e.RunId, ct);
    }
}

/// <summary>Every worker instance drops its cached copy so the new version applies fleet-wide without a restart.</summary>
public sealed class RuleSetPublishedHandler(IActiveRuleSetProvider provider) : IIntegrationEventHandler<RuleSetPublishedEvent>
{
    public Task HandleAsync(RuleSetPublishedEvent e, CancellationToken ct) => provider.InvalidateAsync(new TenantId(e.TenantId), e.EntityType, ct);
}
