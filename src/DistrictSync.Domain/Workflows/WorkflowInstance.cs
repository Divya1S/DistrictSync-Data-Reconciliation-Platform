using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Workflows;

public enum WorkflowStatus
{
    /// <summary>Waiting for <see cref="WorkflowInstance.WakeUpAt"/>; eligible to be claimed by any worker once due.</summary>
    Scheduled = 1,

    /// <summary>Claimed by a worker (see LockOwner / LockExpiresAt). If the worker dies the lease expires and the instance becomes claimable again.</summary>
    Running = 2,

    Completed = 3,

    /// <summary>Gave up after the maximum number of attempts; requires operator attention.</summary>
    Failed = 4,
}

/// <summary>
/// The persistence record behind every long-running or timer-driven process (case SLA escalation,
/// applying an approved resolution, retrying a write-back). Durability comes from storing the
/// state and the next wake-up time in SQL Server; crash recovery comes from lease expiry.
/// </summary>
public sealed class WorkflowInstance : Entity, ITenantScoped
{
    public const int MaxAttempts = 8;

    private WorkflowInstance() { }

    public TenantId TenantId { get; private set; }
    public string WorkflowType { get; private set; } = string.Empty;
    public Guid SubjectId { get; private set; }
    public string StateJson { get; private set; } = "{}";
    public WorkflowStatus Status { get; private set; }
    public DateTimeOffset WakeUpAt { get; private set; }
    public string? LockOwner { get; private set; }
    public DateTimeOffset? LockExpiresAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public byte[]? RowVersion { get; private set; }

    public static WorkflowInstance Schedule(TenantId tenantId, string workflowType, Guid subjectId, string stateJson, DateTimeOffset wakeUpAt, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        WorkflowType = Guard.MaxLength(Guard.NotNullOrWhiteSpace(workflowType), 100),
        SubjectId = Guard.NotEmpty(subjectId),
        StateJson = string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson,
        Status = WorkflowStatus.Scheduled,
        WakeUpAt = wakeUpAt,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public bool IsClaimable(DateTimeOffset now) => Status is WorkflowStatus.Scheduled or WorkflowStatus.Running
        && WakeUpAt <= now
        && (LockExpiresAt is null || LockExpiresAt <= now);

    public void Claim(string owner, TimeSpan leaseDuration, DateTimeOffset now)
    {
        if (!IsClaimable(now))
        {
            throw new InvalidStateTransitionException(nameof(WorkflowInstance), Status.ToString(), nameof(Claim));
        }

        Status = WorkflowStatus.Running;
        LockOwner = Guard.MaxLength(owner, 100);
        LockExpiresAt = now + leaseDuration;
        Attempts++;
        UpdatedAt = now;
    }

    public void ExtendLease(TimeSpan leaseDuration, DateTimeOffset now)
    {
        LockExpiresAt = now + leaseDuration;
        UpdatedAt = now;
    }

    /// <summary>Persist progress and go back to sleep until the next wake-up time.</summary>
    public void Sleep(string stateJson, DateTimeOffset wakeUpAt, DateTimeOffset now)
    {
        StateJson = stateJson;
        WakeUpAt = wakeUpAt;
        Status = WorkflowStatus.Scheduled;
        Attempts = 0;
        LastError = null;
        ReleaseLease(now);
    }

    /// <summary>Wake up early (e.g. the API received a signal for this workflow's subject).</summary>
    public void Nudge(DateTimeOffset now)
    {
        if (Status is WorkflowStatus.Scheduled && WakeUpAt > now)
        {
            WakeUpAt = now;
            UpdatedAt = now;
        }
    }

    public void Complete(string stateJson, DateTimeOffset now)
    {
        StateJson = stateJson;
        Status = WorkflowStatus.Completed;
        CompletedAt = now;
        ReleaseLease(now);
    }

    /// <summary>
    /// Records a failed attempt and schedules a retry with exponential backoff, or fails permanently.
    /// Returns true when the workflow will be retried.
    /// </summary>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        LastError = Guard.MaxLength(error, 4000);
        if (Attempts >= MaxAttempts)
        {
            Status = WorkflowStatus.Failed;
            ReleaseLease(now);
            return false;
        }

        var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Attempts), 900));
        WakeUpAt = now + backoff;
        Status = WorkflowStatus.Scheduled;
        ReleaseLease(now);
        return true;
    }

    private void ReleaseLease(DateTimeOffset now)
    {
        LockOwner = null;
        LockExpiresAt = null;
        UpdatedAt = now;
    }
}
