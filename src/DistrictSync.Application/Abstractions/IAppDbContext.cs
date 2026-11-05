using DistrictSync.Application.Messaging;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Sources;
using DistrictSync.Domain.Tenants;
using DistrictSync.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Abstractions;

/// <summary>
/// The unit of work. Exposed as DbSets so use cases can compose efficient queries, while the provider,
/// mappings, interceptors and tenant filters stay in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<SourceSystem> SourceSystems { get; }
    DbSet<IngestionBatch> IngestionBatches { get; }
    DbSet<SourceRecord> SourceRecords { get; }
    DbSet<ReconciliationRun> ReconciliationRuns { get; }
    DbSet<ComparisonProfile> ComparisonProfiles { get; }
    DbSet<Discrepancy> Discrepancies { get; }
    DbSet<RuleSet> RuleSets { get; }
    DbSet<ResolutionCase> Cases { get; }
    DbSet<WorkflowInstance> WorkflowInstances { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<DeadLetterMessage> DeadLetters { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Detaches all tracked entities. Long-running jobs call this after each chunk to keep memory flat.</summary>
    void ClearTracking();

    /// <summary>Runs <paramref name="action"/> inside one database transaction (joins the ambient one if present).</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
