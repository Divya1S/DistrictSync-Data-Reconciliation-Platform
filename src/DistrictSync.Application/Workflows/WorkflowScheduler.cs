using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Workflows;

/// <summary>Creates and nudges workflow instances as part of the caller's unit of work.</summary>
public sealed class WorkflowScheduler(IAppDbContext db, TimeProvider clock)
{
    public WorkflowInstance Schedule<TState>(TenantId tenantId, string workflowType, Guid subjectId, TState state, DateTimeOffset wakeUpAt)
    {
        var instance = WorkflowInstance.Schedule(tenantId, workflowType, subjectId, JsonSerializer.Serialize(state, JsonDefaults.Options), wakeUpAt, clock.GetUtcNow());
        db.WorkflowInstances.Add(instance);
        return instance;
    }

    /// <summary>Wake a sleeping workflow for the subject now (e.g. the case just changed state, so re-evaluate its SLA timer).</summary>
    public async Task NudgeAsync(string workflowType, Guid subjectId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var instances = await db.WorkflowInstances
            .Where(w => w.WorkflowType == workflowType && w.SubjectId == subjectId && w.Status == WorkflowStatus.Scheduled)
            .ToListAsync(ct);

        foreach (var instance in instances)
        {
            instance.Nudge(now);
        }
    }
}
