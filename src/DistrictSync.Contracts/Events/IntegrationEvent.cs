namespace DistrictSync.Contracts.Events;

/// <summary>
/// A message published to RabbitMQ. Integration events are the public contract between services; they
/// are deliberately flat records with primitive members so they can be versioned independently of the
/// domain model.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    Guid TenantId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record IntegrationEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt) : IIntegrationEvent;

// ----- Ingestion -----

public sealed record IngestionBatchReceivedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid BatchId, Guid SourceSystemId, string EntityType)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record IngestionProgressEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid BatchId, int Processed, int Accepted, int Rejected, int Duplicates)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record IngestionBatchCompletedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid BatchId, Guid SourceSystemId, string EntityType, int Total, int Accepted, int Rejected, int Duplicates)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record IngestionBatchFailedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid BatchId, Guid SourceSystemId, string Reason)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

// ----- Reconciliation -----

public sealed record ReconciliationRequestedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid RunId, string EntityType, Guid LeftSourceSystemId, Guid RightSourceSystemId)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record ReconciliationProgressEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid RunId, long LeftRead, long RightRead, long Discrepancies, string? LastKey)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record ReconciliationStatsDto(long LeftRecords, long RightRecords, long Matched, long MissingInLeft, long MissingInRight, long FieldConflicts, long Duplicates, long AutoResolved, long CasesOpened, long Ignored);

public sealed record ReconciliationCompletedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid RunId, string EntityType, ReconciliationStatsDto Stats)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record ReconciliationFailedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid RunId, string Reason)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

// ----- Rules -----

public sealed record RuleSetPublishedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid RuleSetId, Guid FamilyId, string EntityType, int Version)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

// ----- Cases -----

public sealed record CaseOpenedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid CaseId, Guid DiscrepancyId, Guid RunId, string Priority, string? AssignedRole, DateTimeOffset DueAt)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record CaseStateChangedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid CaseId, string FromState, string ToState, string Actor)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record CaseApprovedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid CaseId, Guid DiscrepancyId, string ApprovedBy)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

public sealed record CaseEscalatedEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid CaseId, int EscalationLevel, DateTimeOffset NewDueAt)
    : IntegrationEvent(EventId, TenantId, OccurredAt);

// ----- Operations -----

public sealed record MessageDeadLetteredEvent(Guid EventId, Guid TenantId, DateTimeOffset OccurredAt, Guid DeadLetterId, string Queue, string MessageType, string Reason)
    : IntegrationEvent(EventId, TenantId, OccurredAt);
