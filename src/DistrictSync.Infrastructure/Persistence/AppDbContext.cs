using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Messaging;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Sources;
using DistrictSync.Domain.Tenants;
using DistrictSync.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Infrastructure.Persistence;

/// <summary>
/// The unit of work. Three cross-cutting guarantees live here:
/// <list type="number">
/// <item>Tenant isolation: every <see cref="ITenantScoped"/> entity carries a global query filter, and writes are
/// checked against the ambient tenant before they reach the database.</item>
/// <item>Transactional outbox: domain events raised by aggregates are dispatched before commit; relays append
/// outbox rows to the same change set, so business state and messages commit atomically.</item>
/// <item>Optimistic concurrency: rowversion tokens on aggregates turn lost updates into <see cref="DbUpdateConcurrencyException"/>.</item>
/// </list>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant, DomainEventDispatcher dispatcher, OutboxWriter outbox, TimeProvider clock)
    : DbContext(options), IAppDbContext
{
    public const string Schema = "ds";

    private readonly ITenantContext _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SourceSystem> SourceSystems => Set<SourceSystem>();
    public DbSet<IngestionBatch> IngestionBatches => Set<IngestionBatch>();
    public DbSet<SourceRecord> SourceRecords => Set<SourceRecord>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<ComparisonProfile> ComparisonProfiles => Set<ComparisonProfile>();
    public DbSet<Discrepancy> Discrepancies => Set<Discrepancy>();
    public DbSet<RuleSet> RuleSets => Set<RuleSet>();
    public DbSet<ResolutionCase> Cases => Set<ResolutionCase>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<DeadLetterMessage> DeadLetters => Set<DeadLetterMessage>();

    /// <summary>Exposed for query filters. EF re-evaluates the filter per query, so a scope that switches tenant is honoured.</summary>
    public TenantId CurrentTenantId => _tenant.HasTenant ? _tenant.TenantId : default;

    public bool BypassTenantFilter => _tenant.IsSystem && !_tenant.HasTenant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // One generic filter for every tenant-scoped entity. "System" scopes (outbox dispatcher, workflow
        // runner polling across tenants) bypass it; everything else is confined to the ambient tenant.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                var method = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => BypassTenantFilter || e.TenantId == CurrentTenantId);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdConverter>();
        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardTenantWrites();
        await dispatcher.DispatchAsync(this, cancellationToken);
        AppendOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public void ClearTracking() => ChangeTracker.Clear();

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            await action(ct);
            return;
        }

        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async token =>
        {
            await using var tx = await Database.BeginTransactionAsync(token);
            await action(token);
            await tx.CommitAsync(token);
        }, ct);
    }

    /// <summary>Defence in depth: a bug that forgets to set TenantId, or sets another tenant's, fails here instead of leaking data.</summary>
    private void GuardTenantWrites()
    {
        if (_tenant.IsSystem && !_tenant.HasTenant)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && entry.Entity.TenantId != _tenant.TenantId)
            {
                throw new TenantIsolationViolationException($"Attempted to write {entry.Metadata.ClrType.Name} belonging to tenant {entry.Entity.TenantId} from tenant {_tenant.TenantId}.");
            }
        }
    }

    private void AppendOutboxMessages()
    {
        var now = clock.GetUtcNow();
        foreach (var message in outbox.Drain(now))
        {
            OutboxMessages.Add(message);
        }
    }
}
