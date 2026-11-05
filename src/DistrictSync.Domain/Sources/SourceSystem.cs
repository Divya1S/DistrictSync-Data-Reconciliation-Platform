using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Sources;

public enum SourceSystemKind
{
    /// <summary>Records are pulled from a paged REST endpoint by the worker.</summary>
    RestApi = 1,

    /// <summary>Records arrive as CSV uploads (bulk, batch-oriented).</summary>
    CsvUpload = 2,

    /// <summary>Records are pushed to us one or a few at a time through an authenticated webhook.</summary>
    Webhook = 3,
}

/// <summary>
/// An external system of record (HR, payroll, ERP, directory...) that a tenant reconciles against.
/// </summary>
public sealed class SourceSystem : AggregateRoot, ITenantScoped
{
    private SourceSystem() { }

    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public SourceSystemKind Kind { get; private set; }

    /// <summary>
    /// Higher trust wins ties during conflict resolution ("PreferHigherTrust" strategy). This encodes
    /// the organisation's view of which system is authoritative for an entity.
    /// </summary>
    public int TrustRank { get; private set; }

    /// <summary>Connector-specific configuration (endpoint, paging, field mapping), stored as JSON.</summary>
    public string ConfigJson { get; private set; } = "{}";

    /// <summary>SHA-256 of the webhook API key. The plaintext is shown once at creation and never stored.</summary>
    public string? ApiKeyHash { get; private set; }

    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    public static SourceSystem Create(TenantId tenantId, string name, string code, SourceSystemKind kind, int trustRank, string configJson, DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(name);
        Guard.NotNullOrWhiteSpace(code);
        Guard.InRange(trustRank, 0, 100);

        return new SourceSystem
        {
            TenantId = tenantId,
            Name = Guard.MaxLength(name.Trim(), 200),
            Code = Guard.MaxLength(code.Trim().ToUpperInvariant(), 32),
            Kind = kind,
            TrustRank = trustRank,
            ConfigJson = string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson,
            CreatedAt = now,
        };
    }

    public void RotateApiKey(string apiKeyHash) => ApiKeyHash = Guard.NotNullOrWhiteSpace(apiKeyHash);

    public void UpdateConfig(string configJson, int trustRank)
    {
        ConfigJson = string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson;
        TrustRank = Guard.InRange(trustRank, 0, 100);
    }

    public void Disable() => IsEnabled = false;

    public void Enable() => IsEnabled = true;
}
