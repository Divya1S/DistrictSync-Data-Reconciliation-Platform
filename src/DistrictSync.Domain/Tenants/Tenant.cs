using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Tenants;

public sealed class Tenant : AggregateRoot
{
    private Tenant() { }

    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantQuotas Quotas { get; private set; } = TenantQuotas.Default;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Tenant Create(string name, string slug, DateTimeOffset now, TenantQuotas? quotas = null)
    {
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNullOrWhiteSpace(slug);
        if (!slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
        {
            throw new DomainException("invalid_slug", "Slug may only contain lowercase letters, digits and hyphens.");
        }

        var tenantId = TenantId.New();
        return new Tenant
        {
            Id = tenantId.Value,
            TenantId = tenantId,
            Name = Guard.MaxLength(name.Trim(), 200),
            Slug = Guard.MaxLength(slug, 64),
            Quotas = quotas ?? TenantQuotas.Default,
            CreatedAt = now,
        };
    }

    public void Deactivate() => IsActive = false;

    public void UpdateQuotas(TenantQuotas quotas) => Quotas = quotas;
}

/// <summary>
/// Per-tenant processing limits. These are the knobs that stop one tenant's bulk load from starving
/// another tenant's workers (the "noisy neighbour" problem).
/// </summary>
public sealed record TenantQuotas(int MaxConcurrentReconciliations, int MaxConcurrentIngestions, int IngestionRequestsPerMinute)
{
    public static TenantQuotas Default { get; } = new(MaxConcurrentReconciliations: 2, MaxConcurrentIngestions: 4, IngestionRequestsPerMinute: 600);
}
