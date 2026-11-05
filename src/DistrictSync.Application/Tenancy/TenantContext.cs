using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Tenancy;

/// <summary>Scoped, mutable implementation populated once per request / message by middleware.</summary>
public sealed class TenantContext : ITenantContext
{
    private TenantId? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public TenantId TenantId => _tenantId ?? throw new InvalidOperationException("No tenant has been established for this scope.");

    public bool IsSystem { get; private set; }

    public string UserId { get; private set; } = "anonymous";

    public IReadOnlyCollection<string> Roles { get; private set; } = [];

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public void Set(TenantId tenantId, string userId, IReadOnlyCollection<string>? roles = null)
    {
        _tenantId = tenantId;
        UserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId;
        Roles = roles ?? [];
        IsSystem = false;
    }

    /// <summary>Trusted background work. Query filters are bypassed; writes must set TenantId explicitly.</summary>
    public void SetSystem(string processName)
    {
        _tenantId = null;
        UserId = processName;
        Roles = ["system"];
        IsSystem = true;
    }

    /// <summary>System processes that act on behalf of one tenant (e.g. a worker handling a tenant's message).</summary>
    public void SetSystemForTenant(TenantId tenantId, string processName)
    {
        _tenantId = tenantId;
        UserId = processName;
        Roles = ["system"];
        IsSystem = false;
    }
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Analyst = "analyst";
    public const string Approver = "approver";
    public const string Viewer = "viewer";
    public const string System = "system";
}
