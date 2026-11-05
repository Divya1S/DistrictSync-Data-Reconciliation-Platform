using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules;
using DistrictSync.Application.Tenancy;
using DistrictSync.Application.Workflows;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Cases;

/// <summary>
/// Human workflow over resolution cases. Every mutation (a) checks the caller's ETag against the row
/// version, (b) runs the aggregate's state machine, (c) nudges the SLA workflow, and (d) relies on the
/// outbox for notifications: all committed together.
/// </summary>
public sealed class CaseService(IAppDbContext db, ITenantContext tenant, WorkflowScheduler scheduler, ReconciliationService reconciliation, TimeProvider clock)
{
    public async Task<PagedResult<CaseDto>> ListAsync(CaseState? state, CasePriority? priority, string? assigneeId, bool? overdue, PageRequest page, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.Cases.AsNoTracking().AsQueryable();
        if (state is not null)
        {
            query = query.Where(c => c.State == state);
        }

        if (priority is not null)
        {
            query = query.Where(c => c.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(assigneeId))
        {
            query = query.Where(c => c.AssigneeId == assigneeId);
        }

        if (overdue == true)
        {
            query = query.Where(c => c.DueAt < now && c.State != CaseState.Resolved && c.State != CaseState.Cancelled);
        }

        return await query.OrderByDescending(c => c.Priority).ThenBy(c => c.DueAt).ToPagedResultAsync(page, c => Mappers.ToDto(c, now), ct);
    }

    public async Task<CaseDetailDto> GetAsync(Guid caseId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().Include(x => x.History).FirstOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new NotFoundException("Case", caseId);
        var discrepancy = await reconciliation.GetDiscrepancyAsync(c.DiscrepancyId, ct);
        return new CaseDetailDto(Mappers.ToDto(c, clock.GetUtcNow()), discrepancy, Mappers.ToDto(c.Proposal), c.ApprovedBy, c.History.OrderBy(h => h.At).Select(Mappers.ToDto).ToList());
    }

    public Task<ResolutionCase> AssignAsync(Guid caseId, AssignCaseRequest request, string? etag, CancellationToken ct) =>
        MutateAsync(caseId, etag, (c, now) => c.Assign(request.AssigneeId, tenant.UserId, now), ct);

    public Task<ResolutionCase> ProposeAsync(Guid caseId, ProposeResolutionRequest request, string? etag, CancellationToken ct)
    {
        ResolutionStrategy? strategy = null;
        if (request.Strategy is not null)
        {
            if (!Enum.TryParse<ResolutionStrategy>(request.Strategy, true, out var s))
            {
                throw ValidationException.For("strategy", $"Unknown strategy '{request.Strategy}'.");
            }

            strategy = s;
        }

        var manual = request.ManualValues is { Count: > 0 } ? JsonSerializer.Serialize(request.ManualValues, JsonDefaults.Options) : null;
        return MutateAsync(caseId, etag, (c, now) => c.Propose(strategy, manual, request.Note, tenant.UserId, now), ct);
    }

    /// <summary>Approval schedules the apply-resolution workflow immediately; the case reaches Resolved once the write-back succeeds.</summary>
    public Task<ResolutionCase> ApproveAsync(Guid caseId, string? etag, CancellationToken ct)
    {
        if (!tenant.IsInRole(Roles.Approver) && !tenant.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Approving a resolution requires the approver role.");
        }

        return MutateAsync(caseId, etag, (c, now) =>
        {
            c.Approve(tenant.UserId, now);
            var proposal = c.Proposal!;
            scheduler.Schedule(
                c.TenantId,
                WorkflowTypes.ApplyResolution,
                c.DiscrepancyId,
                new ApplyResolutionState(c.DiscrepancyId, c.Id, proposal.Strategy, proposal.ManualValuesJson, null, null),
                now);
        }, ct);
    }

    public Task<ResolutionCase> RejectAsync(Guid caseId, RejectProposalRequest request, string? etag, CancellationToken ct) =>
        MutateAsync(caseId, etag, (c, now) => c.Reject(request.Reason, tenant.UserId, now), ct);

    public Task<ResolutionCase> CancelAsync(Guid caseId, CancelCaseRequest request, string? etag, CancellationToken ct) =>
        MutateAsync(caseId, etag, (c, now) => c.Cancel(request.Reason, tenant.UserId, now), ct);

    public Task<ResolutionCase> CommentAsync(Guid caseId, AddCommentRequest request, CancellationToken ct) =>
        MutateAsync(caseId, null, (c, now) => c.Comment(request.Note, tenant.UserId, now), ct);

    private async Task<ResolutionCase> MutateAsync(Guid caseId, string? etag, Action<ResolutionCase, DateTimeOffset> mutation, CancellationToken ct)
    {
        var c = await db.Cases.Include(x => x.History).FirstOrDefaultAsync(x => x.Id == caseId, ct) ?? throw new NotFoundException("Case", caseId);
        ETag.EnsureMatches(etag, c.RowVersion, "Case", caseId);

        var now = clock.GetUtcNow();
        mutation(c, now);
        await scheduler.NudgeAsync(WorkflowTypes.CaseSla, c.Id, ct);
        await db.SaveChangesAsync(ct);
        return c;
    }
}
