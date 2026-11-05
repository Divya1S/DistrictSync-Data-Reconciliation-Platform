namespace DistrictSync.Contracts.Api;

public sealed record DashboardSummaryDto(
    int IngestionInProgress,
    int IngestionCompletedToday,
    int IngestionFailedToday,
    long RecordsIngestedToday,
    int ReconciliationRunning,
    int ReconciliationCompletedToday,
    long OpenDiscrepancies,
    long AutoResolvedToday,
    long CasesOpen,
    long CasesOverdue,
    long CasesResolvedToday,
    int DeadLetters,
    int OutboxPending,
    IReadOnlyList<CountByKeyDto> OpenDiscrepanciesByType,
    IReadOnlyList<CountByKeyDto> OpenCasesByPriority,
    IReadOnlyList<DailyCountDto> DiscrepanciesLast14Days);

public sealed record CountByKeyDto(string Key, long Count);

public sealed record DailyCountDto(DateOnly Day, long Detected, long Resolved);

public sealed record DeadLetterDto(Guid Id, string Queue, string RoutingKey, string MessageType, string Reason, int Attempts, DateTimeOffset DeadLetteredAt, DateTimeOffset? ReplayedAt, string Payload);

public sealed record OutboxHealthDto(int Pending, int Failed, double? OldestPendingAgeSeconds);

public sealed record WorkflowInstanceDto(Guid Id, string WorkflowType, Guid SubjectId, string Status, DateTimeOffset WakeUpAt, string? LockOwner, DateTimeOffset? LockExpiresAt, int Attempts, string? LastError, DateTimeOffset UpdatedAt);

/// <summary>Payload for the SignalR <c>OperationsHub</c>. One shape for every live update keeps the client simple.</summary>
public sealed record LiveUpdate(string Kind, DateTimeOffset At, object Payload);
