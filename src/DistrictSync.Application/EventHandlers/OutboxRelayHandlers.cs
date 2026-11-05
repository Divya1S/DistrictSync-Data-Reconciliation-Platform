using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Messaging;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;

namespace DistrictSync.Application.EventHandlers;

/// <summary>
/// Translates domain events into integration events on the outbox. This is the seam between the
/// in-process domain model and the published message contract: the mapping is explicit so the wire
/// format can stay stable while the domain evolves.
/// </summary>
public sealed class OutboxRelayHandlers(IOutbox outbox) :
    IDomainEventHandler<IngestionBatchReceived>,
    IDomainEventHandler<IngestionBatchCompleted>,
    IDomainEventHandler<IngestionBatchFailed>,
    IDomainEventHandler<ReconciliationRequested>,
    IDomainEventHandler<ReconciliationCompleted>,
    IDomainEventHandler<ReconciliationFailed>,
    IDomainEventHandler<RuleSetPublished>,
    IDomainEventHandler<CaseOpened>,
    IDomainEventHandler<CaseStateChanged>,
    IDomainEventHandler<CaseApproved>,
    IDomainEventHandler<CaseEscalated>
{
    public Task HandleAsync(IngestionBatchReceived e, CancellationToken ct)
    {
        outbox.Enqueue(new IngestionBatchReceivedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.BatchId, e.SourceSystemId, e.EntityType));
        return Task.CompletedTask;
    }

    public Task HandleAsync(IngestionBatchCompleted e, CancellationToken ct)
    {
        outbox.Enqueue(new IngestionBatchCompletedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.BatchId, e.SourceSystemId, e.EntityType, e.Total, e.Accepted, e.Rejected, e.Duplicates));
        return Task.CompletedTask;
    }

    public Task HandleAsync(IngestionBatchFailed e, CancellationToken ct)
    {
        outbox.Enqueue(new IngestionBatchFailedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.BatchId, e.SourceSystemId, e.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReconciliationRequested e, CancellationToken ct)
    {
        outbox.Enqueue(new ReconciliationRequestedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.RunId, e.EntityType, e.LeftSourceSystemId, e.RightSourceSystemId));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReconciliationCompleted e, CancellationToken ct)
    {
        outbox.Enqueue(new ReconciliationCompletedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.RunId, e.EntityType, Mappers.ToDto(e.Stats)));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReconciliationFailed e, CancellationToken ct)
    {
        outbox.Enqueue(new ReconciliationFailedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.RunId, e.Reason));
        return Task.CompletedTask;
    }

    public Task HandleAsync(RuleSetPublished e, CancellationToken ct)
    {
        outbox.Enqueue(new RuleSetPublishedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.RuleSetId, e.FamilyId, e.EntityType, e.Version));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CaseOpened e, CancellationToken ct)
    {
        outbox.Enqueue(new CaseOpenedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.CaseId, e.DiscrepancyId, e.RunId, e.Priority.ToString(), e.AssignedRole, e.DueAt));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CaseStateChanged e, CancellationToken ct)
    {
        outbox.Enqueue(new CaseStateChangedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.CaseId, e.From.ToString(), e.To.ToString(), e.Actor));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CaseApproved e, CancellationToken ct)
    {
        outbox.Enqueue(new CaseApprovedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.CaseId, e.DiscrepancyId, e.ApprovedBy));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CaseEscalated e, CancellationToken ct)
    {
        outbox.Enqueue(new CaseEscalatedEvent(EventIds.Next(), e.TenantId.Value, e.OccurredAt, e.CaseId, e.EscalationLevel, e.NewDueAt));
        return Task.CompletedTask;
    }
}
