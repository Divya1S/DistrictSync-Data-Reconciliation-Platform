using DistrictSync.Application.Abstractions;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Infrastructure.Workflows;

/// <summary>
/// Atomic claim of due workflow instances. A single UPDATE with READPAST/UPDLOCK means N workers can poll
/// concurrently and each instance is handed to exactly one of them. An expired lease (worker crash) makes
/// the row claimable again.
/// </summary>
public sealed class SqlWorkflowStore(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IWorkflowStore
{
    public async Task<IReadOnlyList<Guid>> ClaimDueAsync(string owner, TimeSpan lease, int maxCount, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var until = now + lease;

        var sql = $"""
            UPDATE TOP (@p0) w
               SET LockOwner = @p1,
                   LockExpiresAt = @p2,
                   Status = 'Running',
                   Attempts = Attempts + 1,
                   UpdatedAt = @p3
            OUTPUT inserted.Id
              FROM [{AppDbContext.Schema}].[WorkflowInstances] w WITH (READPAST, UPDLOCK, ROWLOCK)
             WHERE w.Status IN ('Scheduled', 'Running')
               AND w.WakeUpAt <= @p3
               AND (w.LockExpiresAt IS NULL OR w.LockExpiresAt <= @p3)
            """;

        return await db.Database.SqlQueryRaw<Guid>(sql, maxCount, owner, until, now).ToListAsync(ct);
    }

    public async Task ExtendLeaseAsync(Guid instanceId, string owner, TimeSpan lease, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        await db.WorkflowInstances.IgnoreQueryFilters()
            .Where(w => w.Id == instanceId && w.LockOwner == owner)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.LockExpiresAt, now + lease).SetProperty(w => w.UpdatedAt, now), ct);
    }
}
