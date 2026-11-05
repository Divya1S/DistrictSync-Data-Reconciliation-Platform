using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Abstractions;

/// <summary>
/// Ambient identity of the current unit of work. In the API it is populated from the JWT; in the worker
/// from message headers. Everything tenant-scoped (query filters, write guards, quotas) reads from here.
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <summary>Throws when no tenant has been established; use <see cref="HasTenant"/> to probe.</summary>
    TenantId TenantId { get; }

    /// <summary>True for trusted background work (outbox dispatch, workflow polling) that legitimately spans tenants.</summary>
    bool IsSystem { get; }

    string UserId { get; }

    IReadOnlyCollection<string> Roles { get; }

    bool IsInRole(string role);
}
