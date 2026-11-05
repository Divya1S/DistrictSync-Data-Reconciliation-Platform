namespace DistrictSync.Domain.Common;

/// <summary>Marks an entity whose rows belong to exactly one tenant and must never be visible across tenants.</summary>
public interface ITenantScoped
{
    TenantId TenantId { get; }
}
