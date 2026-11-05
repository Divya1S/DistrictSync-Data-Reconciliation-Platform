# ADR 0007: Durable workflows as lease-claimed rows

**Status:** Accepted · **Date:** 2026-11-04

## Context
Cases have SLA timers measured in days and resolutions that call external systems that may be down.
Worker processes restart, scale, and crash; in-memory timers and `Task.Delay` do not survive any of that.

## Decision
A `WorkflowInstances` table holds the state (`StateJson`), the next wake-up time and a **lease**
(`LockOwner`, `LockExpiresAt`). `WorkflowRunner` claims due rows atomically (`UPDATE … OUTPUT` with
`READPAST`), runs the matching `IWorkflowDefinition`, heart-beats the lease while working, and persists the
step result (`Sleep(until)` or `Complete`) together with any domain events the step produced. Failures are
recorded with exponential backoff up to `MaxAttempts`; then the instance is `Failed` for operator attention.

Two definitions ship: `case-sla` (escalate when overdue, re-arm the timer) and `apply-resolution` (push the
agreed outcome through `IWriteBackConnector`, idempotent on the discrepancy id, then resolve the case).

## Consequences
- A crashed worker's work resumes on another worker after the lease lapses: verified by
  `Workflow_abandoned_by_a_crashed_worker_is_picked_up_after_its_lease_expires`.
- API actions **nudge** sleeping instances (set `WakeUpAt = now`) so state changes re-evaluate timers promptly.
- Definitions must be idempotent; the runner may execute a step twice if it dies between work and persistence.
- Chosen over Temporal/Durable Task/Elsa to keep the mechanism visible; the `IWorkflowDefinition` seam is small
  enough to port.
