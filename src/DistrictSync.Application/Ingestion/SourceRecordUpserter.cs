using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Ingestion;

public sealed record UpsertResult(int Inserted, int Updated, int Unchanged, int Deleted)
{
    public static UpsertResult operator +(UpsertResult a, UpsertResult b) => new(a.Inserted + b.Inserted, a.Updated + b.Updated, a.Unchanged + b.Unchanged, a.Deleted + b.Deleted);

    public static UpsertResult Zero { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Chunked upsert: one indexed lookup for the chunk's external ids, then batched inserts/updates in a
/// single SaveChanges. Hash comparison turns repeated full loads into near no-op writes.
/// </summary>
public sealed class SourceRecordUpserter(IAppDbContext db, TimeProvider clock)
{
    public async Task<UpsertResult> UpsertAsync(TenantId tenantId, Guid sourceSystemId, Guid batchId, string entityType, IReadOnlyList<NormalizedRecord> chunk, CancellationToken ct)
    {
        if (chunk.Count == 0)
        {
            return UpsertResult.Zero;
        }

        var now = clock.GetUtcNow();

        // Within one chunk the last observation of an external id wins.
        var latest = new Dictionary<string, NormalizedRecord>(StringComparer.Ordinal);
        foreach (var record in chunk)
        {
            latest[record.ExternalId] = record;
        }

        var ids = latest.Keys.ToList();
        var existing = await db.SourceRecords
            .Where(r => r.TenantId == tenantId && r.SourceSystemId == sourceSystemId && r.EntityType == entityType && ids.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.Ordinal, ct);

        int inserted = 0, updated = 0, unchanged = 0, deleted = 0;
        foreach (var (externalId, record) in latest)
        {
            if (existing.TryGetValue(externalId, out var current))
            {
                if (record.Deleted)
                {
                    if (!current.IsDeleted)
                    {
                        current.MarkDeleted(now);
                        deleted++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    continue;
                }

                if (current.Apply(batchId, record.MatchKey, record.PayloadJson, record.PayloadHash, record.UpdatedAt, now))
                {
                    updated++;
                }
                else
                {
                    unchanged++;
                }
            }
            else if (!record.Deleted)
            {
                db.SourceRecords.Add(SourceRecord.Create(tenantId, sourceSystemId, batchId, entityType, externalId, record.MatchKey, record.PayloadJson, record.PayloadHash, record.UpdatedAt, now));
                inserted++;
            }
            else
            {
                unchanged++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new UpsertResult(inserted, updated, unchanged, deleted);
    }

    /// <summary>Snapshot semantics: anything this batch did not touch has disappeared from the source.</summary>
    public async Task<int> SoftDeleteUnseenAsync(TenantId tenantId, Guid sourceSystemId, Guid batchId, string entityType, DateTimeOffset batchStartedAt, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.SourceRecords
            .Where(r => r.TenantId == tenantId && r.SourceSystemId == sourceSystemId && r.EntityType == entityType && !r.IsDeleted && r.BatchId != batchId && r.LastSeenAt < batchStartedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsDeleted, true).SetProperty(r => r.LastSeenAt, now), ct);
    }
}
