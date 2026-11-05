using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Tenants;

public sealed class TenantService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<TenantDto> GetCurrentAsync(CancellationToken ct) =>
        Mappers.ToDto(await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenant.TenantId, ct) ?? throw new NotFoundException("Tenant", tenant.TenantId));

    public async Task<Tenant?> FindBySlugAsync(string slug, CancellationToken ct) =>
        await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive, ct);

    public async Task<Tenant> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        if (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == request.Slug, ct))
        {
            throw ValidationException.For("slug", $"Tenant slug '{request.Slug}' is already taken.");
        }

        var created = Tenant.Create(request.Name, request.Slug, clock.GetUtcNow());
        db.Tenants.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<bool> ExistsAsync(TenantId tenantId, CancellationToken ct) =>
        await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId && t.IsActive, ct);
}
