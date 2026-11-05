# ADR 0002: Transactional outbox for publishing integration events

**Status:** Accepted · **Date:** 2026-11-04

## Context
A SQL Server transaction and a RabbitMQ publish cannot be made atomic. Publishing before commit leaks
events for rolled-back work; publishing after commit loses events when the process dies in between.

## Decision
Aggregates raise **domain events**; `AppDbContext.SaveChangesAsync` dispatches them in-process to
`IDomainEventHandler<T>` relays that translate them into **integration events** (`Contracts`) and append
`OutboxMessages` rows to the *same* change set. One `SaveChanges` → one transaction → state and messages
commit together. The worker's `OutboxDispatcher` claims rows with a lease (`UPDATE TOP(n) … WITH (READPAST,
UPDLOCK)`), publishes with **publisher confirms**, and marks rows processed. Failures back off exponentially.

Progress ticks (`ingestion.progress`, `reconciliation.progress`) bypass the outbox: they are loss-tolerant
and high-frequency, so durability would be pure cost.

## Consequences
- Delivery is at-least-once; consumers must de-duplicate (ADR 0003).
- Several dispatcher instances can run; cross-instance ordering is not guaranteed and handlers don't assume it.
- The `traceparent` captured at enqueue time is carried in the message so consumer spans join the producer trace.
- Hand-rolled rather than MassTransit/Wolverine/NServiceBus because implementing the pattern is the point of
  the project; the interfaces (`IOutbox`, `IIntegrationEventHandler<T>`) would let a framework replace it.
