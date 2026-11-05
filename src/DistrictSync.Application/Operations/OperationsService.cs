using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Operations;

/// <summary>Replays a parked message onto the broker. Implemented in Infrastructure (needs the RabbitMQ channel).</summary>
public interface IDeadLetterReplayer
{
    Task ReplayAsync(Messaging.DeadLetterMessage message, CancellationToken ct);
}

public sealed class OperationsService(IAppDbContext db, ITenantContext tenant, IDeadLetterReplayer replayer, TimeProvider clock)
{
    public async Task<PagedResult<DeadLetterDto>> ListDeadLettersAsync(bool includeReplayed, PageRequest page, CancellationToken ct)
    {
        var query = db.DeadLetters.AsNoTracking().Where(m => m.TenantId == tenant.TenantId);
        if (!includeReplayed)
        {
            query = query.Where(m => m.ReplayedAt == null);
        }

        return await query.OrderByDescending(m => m.DeadLetteredAt).ToPagedResultAsync(page, Mappers.ToDto, ct);
    }

    public async Task ReplayDeadLetterAsync(Guid id, CancellationToken ct)
    {
        var message = await db.DeadLetters.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenant.TenantId, ct) ?? throw new NotFoundException("DeadLetter", id);
        if (message.ReplayedAt is not null)
        {
            throw ValidationException.For("id", "This message has already been replayed.");
        }

        await replayer.ReplayAsync(message, ct);
        message.MarkReplayed(tenant.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public async Task<OutboxHealthDto> GetOutboxHealthAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var pendingQuery = db.OutboxMessages.AsNoTracking().Where(m => m.TenantId == tenant.TenantId && m.ProcessedAt == null);
        var pending = await pendingQuery.CountAsync(ct);
        var failed = await pendingQuery.CountAsync(m => m.Attempts > 0, ct);
        var oldest = await pendingQuery.OrderBy(m => m.OccurredAt).Select(m => (DateTimeOffset?)m.OccurredAt).FirstOrDefaultAsync(ct);
        return new OutboxHealthDto(pending, failed, oldest is null ? null : (now - oldest.Value).TotalSeconds);
    }

    public async Task<PagedResult<WorkflowInstanceDto>> ListWorkflowsAsync(WorkflowStatus? status, PageRequest page, CancellationToken ct)
    {
        var query = db.WorkflowInstances.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }

        return await query.OrderBy(w => w.WakeUpAt).ToPagedResultAsync(page, Mappers.ToDto, ct);
    }
}
