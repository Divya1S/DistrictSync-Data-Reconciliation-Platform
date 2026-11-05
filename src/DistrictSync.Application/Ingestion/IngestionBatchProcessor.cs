using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Messaging;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Application.Ingestion;

/// <summary>
/// Worker-side bulk ingestion. Streams the raw source, normalises, and upserts in fixed-size chunks, each
/// committed on its own and followed by a change-tracker reset, so memory stays flat for arbitrarily large
/// files. Safe to re-run: a crashed batch resumes with upsert semantics and finishes with the same result.
/// </summary>
public sealed class IngestionBatchProcessor(
    IAppDbContext db,
    IRawRecordSourceResolver sources,
    SourceRecordUpserter upserter,
    IStagingStore staging,
    IEphemeralEventPublisher ephemeral,
    TimeProvider clock,
    ILogger<IngestionBatchProcessor> logger)
{
    public const int ChunkSize = 2_000;
    private const int ProgressEveryChunks = 5;
    private const int MaxLoggedRejections = 25;

    public async Task ProcessAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.IngestionBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            logger.LogWarning("Ingestion batch {BatchId} not found; message ignored", batchId);
            return;
        }

        if (batch.Status is IngestionBatchStatus.Completed or IngestionBatchStatus.Failed or IngestionBatchStatus.Duplicate)
        {
            logger.LogInformation("Ingestion batch {BatchId} already in state {Status}; nothing to do", batchId, batch.Status);
            return;
        }

        var source = await db.SourceSystems.FirstAsync(s => s.Id == batch.SourceSystemId, ct);
        var config = SourceConfig.Parse(source.ConfigJson);
        var startedAt = clock.GetUtcNow();
        batch.Start(startedAt);
        await db.SaveChangesAsync(ct);

        var reader = sources.Resolve(batch, source);
        var tenantId = batch.TenantId;
        var entityType = batch.EntityType;
        var sourceId = batch.SourceSystemId;
        var stagingReference = batch.StagingReference;

        var total = 0;
        var rejected = 0;
        var totals = UpsertResult.Zero;
        var chunk = new List<NormalizedRecord>(ChunkSize);
        var chunksFlushed = 0;
        var loggedRejections = 0;

        try
        {
            await foreach (var raw in reader.ReadAsync(batch, source, ct))
            {
                total++;
                if (!RecordNormalizer.TryNormalize(raw, config, out var normalized, out var rejection))
                {
                    rejected++;
                    if (loggedRejections++ < MaxLoggedRejections)
                    {
                        logger.LogWarning("Batch {BatchId} line {Line}: rejected ({Reason})", batchId, rejection!.LineNumber, rejection.Reason);
                    }

                    continue;
                }

                chunk.Add(normalized!);
                if (chunk.Count >= ChunkSize)
                {
                    totals += await FlushAsync(chunk, ct);
                    chunksFlushed++;
                    await RecordProgressAsync(batchId, total, totals, rejected, chunksFlushed % ProgressEveryChunks == 0, ct);
                }
            }

            if (chunk.Count > 0)
            {
                totals += await FlushAsync(chunk, ct);
            }

            var softDeleted = 0;
            if (config.LoadMode == LoadMode.Snapshot && total > 0)
            {
                softDeleted = await upserter.SoftDeleteUnseenAsync(tenantId, sourceId, batchId, entityType, startedAt, ct);
            }

            batch = await ReloadAsync(batchId, ct);
            batch.Complete(total, totals.Inserted + totals.Updated + totals.Deleted + softDeleted, rejected, totals.Unchanged, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Batch {BatchId} completed: {Total} rows, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Deleted} deleted, {Rejected} rejected",
                batchId, total, totals.Inserted, totals.Updated, totals.Unchanged, totals.Deleted + softDeleted, rejected);

            if (stagingReference is not null && !stagingReference.StartsWith("rest://", StringComparison.Ordinal))
            {
                await staging.DeleteAsync(stagingReference, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch {BatchId} failed after {Total} rows", batchId, total);
            db.ClearTracking();
            batch = await ReloadAsync(batchId, ct);
            batch.Fail(ex.Message, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }

        async Task<UpsertResult> FlushAsync(List<NormalizedRecord> pending, CancellationToken token)
        {
            var result = await upserter.UpsertAsync(tenantId, sourceId, batchId, entityType, pending, token);
            pending.Clear();
            db.ClearTracking();
            return result;
        }
    }

    private async Task RecordProgressAsync(Guid batchId, int total, UpsertResult totals, int rejected, bool publish, CancellationToken ct)
    {
        var batch = await ReloadAsync(batchId, ct);
        batch.RecordProgress(total, totals.Inserted + totals.Updated + totals.Deleted, rejected, totals.Unchanged);
        await db.SaveChangesAsync(ct);
        db.ClearTracking();

        if (publish)
        {
            await ephemeral.PublishAsync(new IngestionProgressEvent(EventIds.Next(), batch.TenantId.Value, clock.GetUtcNow(), batchId, total, totals.Inserted + totals.Updated, rejected, totals.Unchanged), ct);
        }
    }

    private async Task<IngestionBatch> ReloadAsync(Guid batchId, CancellationToken ct) => await db.IngestionBatches.FirstAsync(b => b.Id == batchId, ct);
}
