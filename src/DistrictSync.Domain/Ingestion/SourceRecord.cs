using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Ingestion;

/// <summary>
/// The normalised copy of one record as it exists in one source system. This is the high-volume table:
/// millions of rows per tenant are expected, so it is deliberately flat, append/upsert friendly and
/// indexed on (TenantId, EntityType, SourceSystemId, MatchKey) to support ordered streaming reads.
/// </summary>
public sealed class SourceRecord : Entity, ITenantScoped
{
    private SourceRecord() { }

    public TenantId TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public Guid BatchId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>The record's identifier inside its own source system (employee number, CRM id...).</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>
    /// The canonical, normalised key used to line records up across systems (e.g. lower-cased work e-mail).
    /// Reconciliation streams both sides ordered by this key and merges them in a single pass.
    /// </summary>
    public string MatchKey { get; private set; } = string.Empty;

    /// <summary>Normalised attributes as JSON (flat object, canonical field names).</summary>
    public string PayloadJson { get; private set; } = "{}";

    /// <summary>SHA-256 of PayloadJson; lets the upsert skip no-op writes and detect duplicates cheaply.</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    /// <summary>When the source system says it last changed the record; drives the "PreferNewest" strategy.</summary>
    public DateTimeOffset? SourceUpdatedAt { get; private set; }

    public DateTimeOffset IngestedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[]? RowVersion { get; private set; }

    public static SourceRecord Create(
        TenantId tenantId,
        Guid sourceSystemId,
        Guid batchId,
        string entityType,
        string externalId,
        string matchKey,
        string payloadJson,
        string payloadHash,
        DateTimeOffset? sourceUpdatedAt,
        DateTimeOffset now)
    {
        return new SourceRecord
        {
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            BatchId = batchId,
            EntityType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(entityType), 64),
            ExternalId = Guard.MaxLength(Guard.NotNullOrWhiteSpace(externalId), 128),
            MatchKey = Guard.MaxLength(Guard.NotNullOrWhiteSpace(matchKey), 256),
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            SourceUpdatedAt = sourceUpdatedAt,
            IngestedAt = now,
            LastSeenAt = now,
        };
    }

    /// <summary>
    /// Applies a newer observation of the same record. Returns false when nothing changed, so callers
    /// can count "unchanged" rows and avoid write amplification on repeated full loads.
    /// </summary>
    public bool Apply(Guid batchId, string matchKey, string payloadJson, string payloadHash, DateTimeOffset? sourceUpdatedAt, DateTimeOffset now)
    {
        LastSeenAt = now;
        BatchId = batchId;

        // Stale write protection: an older observation (per the source's own clock) never overwrites a newer one.
        if (sourceUpdatedAt is not null && SourceUpdatedAt is not null && sourceUpdatedAt < SourceUpdatedAt)
        {
            return false;
        }

        if (!IsDeleted && PayloadHash == payloadHash && MatchKey == matchKey)
        {
            return false;
        }

        MatchKey = matchKey;
        PayloadJson = payloadJson;
        PayloadHash = payloadHash;
        SourceUpdatedAt = sourceUpdatedAt ?? SourceUpdatedAt;
        IsDeleted = false;
        return true;
    }

    public void MarkDeleted(DateTimeOffset now)
    {
        IsDeleted = true;
        LastSeenAt = now;
    }
}
