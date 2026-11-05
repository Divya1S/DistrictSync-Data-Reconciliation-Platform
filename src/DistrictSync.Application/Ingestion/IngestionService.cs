using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Ingestion;

/// <summary>API-facing ingestion use cases. Heavy lifting (CSV, REST) is deferred to the worker through the outbox.</summary>
public sealed class IngestionService(IAppDbContext db, IStagingStore staging, ITenantContext tenant, SourceRecordUpserter upserter, TimeProvider clock)
{
    public const int MaxWebhookRecords = 1_000;

    public async Task<IngestionBatch> ReceiveCsvAsync(Guid sourceSystemId, string entityType, Stream content, string? fileName, CancellationToken ct)
    {
        var source = await db.SourceSystems.FirstOrDefaultAsync(s => s.Id == sourceSystemId, ct) ?? throw new NotFoundException("SourceSystem", sourceSystemId);
        if (!source.IsEnabled)
        {
            throw ValidationException.For("sourceSystemId", "Source system is disabled.");
        }

        var batchId = Guid.CreateVersion7();
        var reference = await staging.StoreAsync(tenant.TenantId, batchId, content, ct);

        string hash;
        long size;
        await using (var staged = await staging.OpenReadAsync(reference, ct))
        {
            size = staged.CanSeek ? staged.Length : 0;
            hash = await Hashing.Sha256HexAsync(staged, ct);
        }

        var now = clock.GetUtcNow();
        var batch = IngestionBatch.Receive(tenant.TenantId, source.Id, entityType, reference, hash, fileName, size, tenant.UserId, now, id: batchId);

        var original = await db.IngestionBatches
            .Where(b => b.SourceSystemId == source.Id && b.ContentHash == hash && b.Status == IngestionBatchStatus.Completed)
            .OrderByDescending(b => b.CompletedAt)
            .FirstOrDefaultAsync(ct);

        if (original is not null)
        {
            batch.ClearDomainEvents();
            batch.MarkDuplicateOf(original.Id, now);
            await staging.DeleteAsync(reference, ct);
        }

        db.IngestionBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    public async Task<IngestionBatch> StartPullAsync(Guid sourceSystemId, string entityType, CancellationToken ct)
    {
        var source = await db.SourceSystems.FirstOrDefaultAsync(s => s.Id == sourceSystemId, ct) ?? throw new NotFoundException("SourceSystem", sourceSystemId);
        if (source.Kind != SourceSystemKind.RestApi)
        {
            throw ValidationException.For("sourceSystemId", "Only REST API sources can be pulled.");
        }

        var config = SourceConfig.Parse(source.ConfigJson);
        if (config.Rest is null || string.IsNullOrWhiteSpace(config.Rest.Url))
        {
            throw ValidationException.For("sourceSystemId", "Source has no REST pull configuration (config.rest.url).");
        }

        var now = clock.GetUtcNow();
        var batch = IngestionBatch.Receive(tenant.TenantId, source.Id, entityType, StagingSchemes.RestPull, Hashing.Sha256Hex($"{source.Id:N}:{now.UtcTicks}"), null, 0, tenant.UserId, now);
        db.IngestionBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    /// <summary>Webhook pushes are small, so they are normalised and upserted synchronously inside the request.</summary>
    public async Task<WebhookIngestResponse> IngestWebhookAsync(SourceSystem source, WebhookIngestRequest request, CancellationToken ct)
    {
        if (request.Records.Count == 0)
        {
            throw ValidationException.For("records", "At least one record is required.");
        }

        if (request.Records.Count > MaxWebhookRecords)
        {
            throw ValidationException.For("records", $"At most {MaxWebhookRecords} records per request; use CSV upload for bulk loads.");
        }

        var config = SourceConfig.Parse(source.ConfigJson);
        var now = clock.GetUtcNow();
        var payloadHash = Hashing.Sha256Hex($"{source.Id:N}:{now.UtcTicks}:{request.Records.Count}");
        var batch = IngestionBatch.Receive(tenant.TenantId, source.Id, request.EntityType, StagingSchemes.Inline, payloadHash, null, 0, tenant.UserId, now, processedInline: true);
        batch.Start(now);
        db.IngestionBatches.Add(batch);

        var normalized = new List<NormalizedRecord>(request.Records.Count);
        var errors = new List<string>();
        var line = 0L;
        foreach (var record in request.Records)
        {
            line++;
            var raw = new RawRecord(record.ExternalId, record.Attributes, record.UpdatedAt, record.Deleted, line);
            if (record.MatchKey is not null)
            {
                raw = raw with { Attributes = new Dictionary<string, string?>(record.Attributes, StringComparer.OrdinalIgnoreCase) { [config.MatchKeyField] = record.MatchKey } };
            }

            if (RecordNormalizer.TryNormalize(raw, config, out var ok, out var rejected))
            {
                normalized.Add(ok!);
            }
            else
            {
                errors.Add($"record {line} ({rejected!.ExternalId ?? "?"}): {rejected.Reason}");
            }
        }

        var result = await upserter.UpsertAsync(tenant.TenantId, source.Id, batch.Id, request.EntityType, normalized, ct);
        batch.Complete(request.Records.Count, result.Inserted + result.Updated + result.Deleted, errors.Count, result.Unchanged, now);
        await db.SaveChangesAsync(ct);

        return new WebhookIngestResponse(batch.Id, result.Inserted, result.Updated + result.Deleted, result.Unchanged, errors.Count, errors.Take(50).ToList());
    }

    public async Task<IngestionBatch> RetryAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.IngestionBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new NotFoundException("IngestionBatch", batchId);
        batch.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return batch;
    }

    public async Task<PagedResult<IngestionBatchDto>> ListAsync(IngestionBatchStatus? status, Guid? sourceSystemId, PageRequest page, CancellationToken ct)
    {
        var query = db.IngestionBatches.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(b => b.Status == status);
        }

        if (sourceSystemId is not null)
        {
            query = query.Where(b => b.SourceSystemId == sourceSystemId);
        }

        var codes = await SourceCodesAsync(ct);
        return await query.OrderByDescending(b => b.ReceivedAt).ToPagedResultAsync(page, b => Mappers.ToDto(b, codes), ct);
    }

    public async Task<IngestionBatchDto> GetAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.IngestionBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new NotFoundException("IngestionBatch", batchId);
        return Mappers.ToDto(batch, await SourceCodesAsync(ct));
    }

    public async Task<Dictionary<Guid, string>> SourceCodesAsync(CancellationToken ct) =>
        await db.SourceSystems.AsNoTracking().Select(s => new { s.Id, s.Code }).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
}
