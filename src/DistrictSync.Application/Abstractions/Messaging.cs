using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Common;

namespace DistrictSync.Application.Abstractions;

/// <summary>Handles a domain event in-process, before the unit of work commits. Typically relays to the outbox.</summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

/// <summary>
/// Writes integration events to the transactional outbox. Because the outbox row is part of the same
/// unit of work as the business change, the event is published if and only if the change committed.
/// </summary>
public interface IOutbox
{
    void Enqueue(IIntegrationEvent integrationEvent);
}

/// <summary>Publishes directly to the broker, bypassing the outbox. Only for ephemeral, loss-tolerant events (progress ticks).</summary>
public interface IEphemeralEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct);
}

public enum HandlerMode
{
    /// <summary>
    /// Inbox insert, handler work and inbox completion share one database transaction. Exactly-once
    /// business effect for handlers whose side effects are all in the database.
    /// </summary>
    Transactional = 1,

    /// <summary>
    /// For long jobs. The inbox claim is committed first, the handler runs with its own chunked
    /// transactions, and the inbox row is completed at the end. The handler must be idempotent/resumable
    /// because a crash mid-way leads to redelivery.
    /// </summary>
    LongRunning = 2,

    /// <summary>
    /// No inbox record. For handlers whose effect is idempotent or loss-tolerant by nature (pushing a live
    /// dashboard update); saves a database round-trip per message.
    /// </summary>
    NoInbox = 3,
}

public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    HandlerMode Mode => HandlerMode.Transactional;

    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
