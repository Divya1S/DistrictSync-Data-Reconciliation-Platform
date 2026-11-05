using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Reconciliation;

public enum ReconciliationRunStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>Counters produced by a reconciliation pass. Owned by <see cref="ReconciliationRun"/>.</summary>
public sealed record ReconciliationStats(
    long LeftRecords,
    long RightRecords,
    long Matched,
    long MissingInLeft,
    long MissingInRight,
    long FieldConflicts,
    long Duplicates,
    long AutoResolved,
    long CasesOpened,
    long Ignored)
{
    public static ReconciliationStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public long TotalDiscrepancies => MissingInLeft + MissingInRight + FieldConflicts + Duplicates;
}

/// <summary>
/// A comparison of one entity type between two source systems. Runs are queued through the outbox,
/// executed by a worker as a streaming merge-join, and summarised here.
/// </summary>
public sealed class ReconciliationRun : AggregateRoot, ITenantScoped
{
    private ReconciliationRun() { }

    public TenantId TenantId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid LeftSourceSystemId { get; private set; }
    public Guid RightSourceSystemId { get; private set; }
    public Guid? ComparisonProfileId { get; private set; }
    public Guid? RuleSetId { get; private set; }
    public int? RuleSetVersion { get; private set; }
    public ReconciliationRunStatus Status { get; private set; }
    public ReconciliationStats Stats { get; private set; } = ReconciliationStats.Empty;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public string RequestedBy { get; private set; } = string.Empty;

    /// <summary>Resume marker: the last match key fully processed. Lets a crashed run continue instead of restarting.</summary>
    public string? Checkpoint { get; private set; }

    public static ReconciliationRun Request(
        TenantId tenantId,
        string entityType,
        Guid leftSourceSystemId,
        Guid rightSourceSystemId,
        Guid? comparisonProfileId,
        string requestedBy,
        DateTimeOffset now)
    {
        Guard.NotNullOrWhiteSpace(entityType);
        Guard.NotEmpty(leftSourceSystemId);
        Guard.NotEmpty(rightSourceSystemId);
        if (leftSourceSystemId == rightSourceSystemId)
        {
            throw new DomainException("same_source", "A reconciliation needs two different source systems.");
        }

        var run = new ReconciliationRun
        {
            TenantId = tenantId,
            EntityType = Guard.MaxLength(entityType.Trim(), 64),
            LeftSourceSystemId = leftSourceSystemId,
            RightSourceSystemId = rightSourceSystemId,
            ComparisonProfileId = comparisonProfileId,
            Status = ReconciliationRunStatus.Queued,
            RequestedAt = now,
            RequestedBy = Guard.MaxLength(requestedBy, 200),
        };

        run.Raise(new ReconciliationRequested(run.TenantId, run.Id, run.EntityType, leftSourceSystemId, rightSourceSystemId, now));
        return run;
    }

    public void Start(Guid? ruleSetId, int? ruleSetVersion, DateTimeOffset now)
    {
        if (Status is not (ReconciliationRunStatus.Queued or ReconciliationRunStatus.Running))
        {
            throw new InvalidStateTransitionException(nameof(ReconciliationRun), Status.ToString(), nameof(Start));
        }

        Status = ReconciliationRunStatus.Running;
        StartedAt ??= now;
        RuleSetId = ruleSetId;
        RuleSetVersion = ruleSetVersion;
    }

    public void SaveCheckpoint(string lastMatchKey, ReconciliationStats stats)
    {
        Checkpoint = lastMatchKey;
        Stats = stats;
    }

    public void Complete(ReconciliationStats stats, DateTimeOffset now)
    {
        if (Status != ReconciliationRunStatus.Running)
        {
            throw new InvalidStateTransitionException(nameof(ReconciliationRun), Status.ToString(), nameof(Complete));
        }

        Stats = stats;
        Status = ReconciliationRunStatus.Completed;
        CompletedAt = now;
        Checkpoint = null;
        Raise(new ReconciliationCompleted(TenantId, Id, EntityType, stats, now));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        Status = ReconciliationRunStatus.Failed;
        FailureReason = Guard.MaxLength(reason, 2000);
        CompletedAt = now;
        Raise(new ReconciliationFailed(TenantId, Id, reason, now));
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is ReconciliationRunStatus.Completed or ReconciliationRunStatus.Failed)
        {
            throw new InvalidStateTransitionException(nameof(ReconciliationRun), Status.ToString(), nameof(Cancel));
        }

        Status = ReconciliationRunStatus.Cancelled;
        CompletedAt = now;
    }
}

public sealed record ReconciliationRequested(TenantId TenantId, Guid RunId, string EntityType, Guid LeftSourceSystemId, Guid RightSourceSystemId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ReconciliationCompleted(TenantId TenantId, Guid RunId, string EntityType, ReconciliationStats Stats, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ReconciliationFailed(TenantId TenantId, Guid RunId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;
