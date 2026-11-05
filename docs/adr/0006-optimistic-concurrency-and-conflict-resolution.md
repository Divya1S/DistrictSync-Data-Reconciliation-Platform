# ADR 0006: Optimistic concurrency everywhere; explicit strategies for inter-system conflicts

**Status:** Accepted · **Date:** 2026-11-04

## Context
Two kinds of conflicting writes exist: (a) two users/workers updating the same row, and (b) two external
systems disagreeing about the same business fact.

## Decision
(a) Every aggregate carries a SQL Server `rowversion`. EF Core turns a lost update into
`DbUpdateConcurrencyException` → HTTP 409. The API additionally exposes the version as an **ETag**; mutating
case endpoints accept `If-Match` and answer **412** when the client's view is stale, so the UI can reload
instead of silently overwriting.

(b) `ConflictResolver` is a pure function of a **strategy** (`PreferLeft`, `PreferRight`, `PreferNewest`,
`PreferHigherTrust`) and the two observations. `PreferNewest` uses the source systems' own timestamps and
falls back to trust rank on ties; equal trust is "undecidable" and fails over to a human case. Stale
observations never overwrite newer ones at ingestion either (`SourceRecord.Apply`).

## Consequences
- No pessimistic locks in request paths; retries are the client's decision.
- Automation never guesses: when the data cannot decide, a case is opened with priority High.
- Rules choose strategies declaratively (`AutoResolve PreferNewest`), so the policy is tenant data, not code.
