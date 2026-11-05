using System.Runtime.CompilerServices;
using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Infrastructure.Persistence;

/// <summary>
/// Keyset-paginated ordered reader. Each page is one indexed range scan on
/// (TenantId, SourceSystemId, EntityType, MatchKey, ExternalId); no OFFSET, so page N costs the same as
/// page 1. The tie-break column is a string so both the ORDER BY and the resume predicate are evaluated
/// with the same SQL collation. Uses its own DbContext so two readers can be consumed in lock-step.
/// </summary>
public sealed class SqlSourceRecordStreamReader(IDbContextFactory<AppDbContext> factory) : ISourceRecordStreamReader
{
    public async IAsyncEnumerable<KeyedRecord> StreamAsync(TenantId tenantId, Guid sourceSystemId, string entityType, string? afterMatchKey, int pageSize, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var lastKey = afterMatchKey;
        var lastExternalId = string.Empty;
        var exclusiveKey = afterMatchKey is not null;

        while (true)
        {
            var query = db.SourceRecords.AsNoTracking().IgnoreQueryFilters()
                .Where(r => r.TenantId == tenantId && r.SourceSystemId == sourceSystemId && r.EntityType == entityType && !r.IsDeleted);

            if (lastKey is not null)
            {
                // Resume strictly after the checkpoint key; otherwise continue after (key, externalId) of the last row read.
                var key = lastKey;
                var ext = lastExternalId;
#pragma warning disable CA1309 // EF Core translates only the culture-free string.Compare(a, b) overload; the column collation decides ordering in SQL.
                query = exclusiveKey
                    ? query.Where(r => string.Compare(r.MatchKey, key) > 0)
                    : query.Where(r => string.Compare(r.MatchKey, key) > 0 || (r.MatchKey == key && string.Compare(r.ExternalId, ext) > 0));
#pragma warning restore CA1309
            }

            var page = await query
                .OrderBy(r => r.MatchKey).ThenBy(r => r.ExternalId)
                .Take(pageSize)
                .Select(r => new KeyedRecord(r.Id, r.MatchKey, r.ExternalId, r.PayloadJson, r.SourceUpdatedAt))
                .ToListAsync(ct);

            foreach (var record in page)
            {
                yield return record;
            }

            if (page.Count < pageSize)
            {
                yield break;
            }

            lastKey = page[^1].MatchKey;
            lastExternalId = page[^1].ExternalId;
            exclusiveKey = false;
        }
    }
}
