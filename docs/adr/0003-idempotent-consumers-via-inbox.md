# ADR 0003: Idempotent consumers via an inbox table, with two handler modes

**Status:** Accepted · **Date:** 2026-11-04

## Context
At-least-once delivery means every handler will eventually see duplicates: broker redelivery after an
unacked crash, dispatcher re-publish after a crash between publish and mark-processed, operator replays.

## Decision
`InboxMessages` has primary key `(MessageId, Consumer)`. The `MessagePipeline` offers three modes:

| Mode | Behaviour | Used for |
|---|---|---|
| `Transactional` | inbox insert + handler + completion in **one** DB transaction | short handlers whose effects are all in the database → exactly-once business effect |
| `LongRunning` | claim committed first; handler runs with its own chunked transactions; completion afterwards | bulk ingestion, reconciliation runs — must be resumable (upserts, checkpoints) |
| `NoInbox` | no record | live dashboard pushes, where a duplicate is harmless |

Failures go through **tiered retry queues** (5s / 30s / 2m, TTL + dead-letter back to the work queue) and
then to a dead-letter exchange whose queue is drained into `DeadLetterMessages` for inspection and replay.

## Consequences
- A redelivered message for a completed row is acknowledged without side effects (`Duplicates` metric).
- Long-running jobs are designed to be re-run: `IngestionBatch.Start` accepts `Processing` again,
  `ReconciliationRun` resumes from its checkpoint key.
- Retry without the RabbitMQ delayed-message plugin: fanout tier exchange → TTL queue → default exchange
  routes the expired message back by its routing key (= the original queue name).
