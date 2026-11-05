using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Reconciliation;

public sealed class ReconciliationService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<ReconciliationRun> RequestRunAsync(CreateReconciliationRunRequest request, CancellationToken ct)
    {
        var sources = await db.SourceSystems.Where(s => s.Id == request.LeftSourceSystemId || s.Id == request.RightSourceSystemId).ToListAsync(ct);
        if (sources.All(s => s.Id != request.LeftSourceSystemId))
        {
            throw new NotFoundException("SourceSystem", request.LeftSourceSystemId);
        }

        if (sources.All(s => s.Id != request.RightSourceSystemId))
        {
            throw new NotFoundException("SourceSystem", request.RightSourceSystemId);
        }

        if (request.ComparisonProfileId is not null && !await db.ComparisonProfiles.AnyAsync(p => p.Id == request.ComparisonProfileId, ct))
        {
            throw new NotFoundException("ComparisonProfile", request.ComparisonProfileId);
        }

        var quotas = await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenant.TenantId).Select(t => t.Quotas).FirstAsync(ct);
        var active = await db.ReconciliationRuns.CountAsync(r => r.Status == ReconciliationRunStatus.Queued || r.Status == ReconciliationRunStatus.Running, ct);
        if (active >= quotas.MaxConcurrentReconciliations)
        {
            throw ValidationException.For("quota", $"Tenant already has {active} active reconciliation run(s); the limit is {quotas.MaxConcurrentReconciliations}.");
        }

        var run = ReconciliationRun.Request(tenant.TenantId, request.EntityType, request.LeftSourceSystemId, request.RightSourceSystemId, request.ComparisonProfileId, tenant.UserId, clock.GetUtcNow());
        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task CancelAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("ReconciliationRun", runId);
        run.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<ReconciliationRunDto>> ListRunsAsync(ReconciliationRunStatus? status, PageRequest page, CancellationToken ct)
    {
        var query = db.ReconciliationRuns.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        var codes = await SourceCodesAsync(ct);
        return await query.OrderByDescending(r => r.RequestedAt).ToPagedResultAsync(page, r => Mappers.ToDto(r, codes), ct);
    }

    public async Task<ReconciliationRunDto> GetRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct) ?? throw new NotFoundException("ReconciliationRun", runId);
        return Mappers.ToDto(run, await SourceCodesAsync(ct));
    }

    public async Task<PagedResult<DiscrepancyDto>> ListDiscrepanciesAsync(Guid? runId, DiscrepancyType? type, DiscrepancyStatus? status, Severity? severity, string? matchKey, PageRequest page, CancellationToken ct)
    {
        var query = db.Discrepancies.AsNoTracking().AsQueryable();
        if (runId is not null)
        {
            query = query.Where(d => d.RunId == runId);
        }

        if (type is not null)
        {
            query = query.Where(d => d.Type == type);
        }

        if (status is not null)
        {
            query = query.Where(d => d.Status == status);
        }

        if (severity is not null)
        {
            query = query.Where(d => d.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(matchKey))
        {
            var needle = matchKey.Trim().ToLowerInvariant();
            query = query.Where(d => d.MatchKey.Contains(needle));
        }

        return await query.OrderByDescending(d => d.Severity).ThenBy(d => d.MatchKey).ToPagedResultAsync(page, Mappers.ToDto, ct);
    }

    public async Task<DiscrepancyDetailDto> GetDiscrepancyAsync(Guid discrepancyId, CancellationToken ct)
    {
        var d = await db.Discrepancies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == discrepancyId, ct) ?? throw new NotFoundException("Discrepancy", discrepancyId);
        var ids = new[] { d.LeftRecordId, d.RightRecordId }.Where(x => x is not null).Select(x => x!.Value).ToList();
        var records = await db.SourceRecords.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        var caseId = await db.Cases.AsNoTracking().Where(c => c.DiscrepancyId == d.Id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        return new DiscrepancyDetailDto(
            Mappers.ToDto(d),
            records.Where(r => r.Id == d.LeftRecordId).Select(Mappers.ToDto).FirstOrDefault(),
            records.Where(r => r.Id == d.RightRecordId).Select(Mappers.ToDto).FirstOrDefault(),
            caseId);
    }

    public async Task<IReadOnlyList<ComparisonProfileDto>> ListProfilesAsync(CancellationToken ct) =>
        (await db.ComparisonProfiles.AsNoTracking().OrderBy(p => p.EntityType).ThenBy(p => p.Name).ToListAsync(ct)).Select(Mappers.ToDto).ToList();

    public async Task<ComparisonProfile> CreateProfileAsync(CreateComparisonProfileRequest request, CancellationToken ct)
    {
        var fields = request.Fields.Select(f =>
        {
            if (!Enum.TryParse<FieldNormalizer>(f.Normalizer, true, out var normalizer))
            {
                throw ValidationException.For("fields", $"Unknown normalizer '{f.Normalizer}' for field '{f.Name}'.");
            }

            return new ComparedField(f.Name.Trim(), normalizer, f.IsCritical);
        }).ToList();

        var profile = ComparisonProfile.Create(tenant.TenantId, request.Name, request.EntityType, fields, request.IsDefault, clock.GetUtcNow());
        if (request.IsDefault)
        {
            foreach (var existing in await db.ComparisonProfiles.Where(p => p.EntityType == profile.EntityType && p.IsDefault).ToListAsync(ct))
            {
                existing.ClearDefault();
            }
        }

        db.ComparisonProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<Dictionary<Guid, string>> SourceCodesAsync(CancellationToken ct) =>
        await db.SourceSystems.AsNoTracking().Select(s => new { s.Id, s.Code }).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
}
