using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Ingestion;

public enum IngestionBatchStatus
{
    Received = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    /// <summary>An identical file (same checksum) was already ingested for this source; nothing to do.</summary>
    Duplicate = 5,
}

/// <summary>
/// One unit of bulk intake: a CSV upload, a REST pull, or a webhook burst. The batch is the
/// idempotency and progress-tracking boundary for ingestion.
/// </summary>
public sealed class IngestionBatch : AggregateRoot, ITenantScoped
{
    private IngestionBatch() { }

    public TenantId TenantId { get; private set; }
    public Guid SourceSystemId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public IngestionBatchStatus Status { get; private set; }

    /// <summary>Where the raw payload is staged (file path or blob URI); null once cleaned up.</summary>
    public string? StagingReference { get; private set; }

    /// <summary>SHA-256 of the raw payload; identical uploads are detected and short-circuited.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public string? FileName { get; private set; }
    public long SizeBytes { get; private set; }

    public int TotalRecords { get; private set; }
    public int AcceptedRecords { get; private set; }
    public int RejectedRecords { get; private set; }
    public int DuplicateRecords { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string SubmittedBy { get; private set; } = string.Empty;

    public static IngestionBatch Receive(
        TenantId tenantId,
        Guid sourceSystemId,
        string entityType,
        string stagingReference,
        string contentHash,
        string? fileName,
        long sizeBytes,
        string submittedBy,
        DateTimeOffset now,
        bool processedInline = false,
        Guid? id = null)
    {
        Guard.NotEmpty(sourceSystemId);
        Guard.NotNullOrWhiteSpace(entityType);
        Guard.NotNullOrWhiteSpace(stagingReference);
        Guard.NotNullOrWhiteSpace(contentHash);

        var batch = new IngestionBatch
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            SourceSystemId = sourceSystemId,
            EntityType = Guard.MaxLength(entityType.Trim(), 64),
            Status = IngestionBatchStatus.Received,
            StagingReference = stagingReference,
            ContentHash = contentHash,
            FileName = fileName is null ? null : Guard.MaxLength(fileName, 260),
            SizeBytes = sizeBytes,
            ReceivedAt = now,
            SubmittedBy = Guard.MaxLength(submittedBy, 200),
        };

        if (!processedInline)
        {
            batch.Raise(new IngestionBatchReceived(batch.TenantId, batch.Id, batch.SourceSystemId, batch.EntityType, now));
        }

        return batch;
    }

    public void MarkDuplicateOf(Guid originalBatchId, DateTimeOffset now)
    {
        EnsureState(IngestionBatchStatus.Received, nameof(MarkDuplicateOf));
        Status = IngestionBatchStatus.Duplicate;
        CompletedAt = now;
        FailureReason = $"Identical content already ingested in batch {originalBatchId:D}.";
    }

    public void Start(DateTimeOffset now)
    {
        // Re-processing after a worker crash is legal: a batch stuck in Processing is resumed, not rejected.
        if (Status is not (IngestionBatchStatus.Received or IngestionBatchStatus.Processing))
        {
            throw new InvalidStateTransitionException(nameof(IngestionBatch), Status.ToString(), nameof(Start));
        }

        Status = IngestionBatchStatus.Processing;
        StartedAt ??= now;
    }

    public void RecordProgress(int total, int accepted, int rejected, int duplicates)
    {
        TotalRecords = total;
        AcceptedRecords = accepted;
        RejectedRecords = rejected;
        DuplicateRecords = duplicates;
    }

    public void Complete(int total, int accepted, int rejected, int duplicates, DateTimeOffset now)
    {
        EnsureState(IngestionBatchStatus.Processing, nameof(Complete));
        RecordProgress(total, accepted, rejected, duplicates);
        Status = IngestionBatchStatus.Completed;
        CompletedAt = now;
        StagingReference = null;
        Raise(new IngestionBatchCompleted(TenantId, Id, SourceSystemId, EntityType, total, accepted, rejected, duplicates, now));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        Status = IngestionBatchStatus.Failed;
        FailureReason = Guard.MaxLength(reason, 2000);
        CompletedAt = now;
        Raise(new IngestionBatchFailed(TenantId, Id, SourceSystemId, reason, now));
    }

    /// <summary>Operator-initiated retry of a failed batch. Re-raises the received event so a worker picks it up again.</summary>
    public void Retry(DateTimeOffset now)
    {
        EnsureState(IngestionBatchStatus.Failed, nameof(Retry));
        if (StagingReference is null)
        {
            throw new DomainException("staging_gone", "The staged payload for this batch is no longer available.");
        }

        Status = IngestionBatchStatus.Received;
        FailureReason = null;
        CompletedAt = null;
        Raise(new IngestionBatchReceived(TenantId, Id, SourceSystemId, EntityType, now));
    }

    private void EnsureState(IngestionBatchStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException(nameof(IngestionBatch), Status.ToString(), action);
        }
    }
}

public sealed record IngestionBatchReceived(TenantId TenantId, Guid BatchId, Guid SourceSystemId, string EntityType, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record IngestionBatchCompleted(TenantId TenantId, Guid BatchId, Guid SourceSystemId, string EntityType, int Total, int Accepted, int Rejected, int Duplicates, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record IngestionBatchFailed(TenantId TenantId, Guid BatchId, Guid SourceSystemId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
