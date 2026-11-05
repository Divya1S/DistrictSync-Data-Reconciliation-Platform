# ADR 0004: Streaming sort-merge join for reconciliation

**Status:** Accepted · **Date:** 2026-11-04

## Context
Tenants have millions of records per source. Loading both sides into dictionaries is O(n) memory and
falls over first on the biggest tenant, which is exactly where reconciliation matters most.

## Decision
`SourceRecords.MatchKey` is stored with a **binary collation** (`Latin1_General_BIN2`) so SQL Server's
`ORDER BY MatchKey` agrees byte-for-byte with `string.CompareOrdinal` in .NET. Each side is read by
`SqlSourceRecordStreamReader` using **keyset pagination** on `(MatchKey, Id)` through a covering index, so
page N costs the same as page 1. `ReconciliationEngine` merges the two ordered streams in a single pass,
holding at most one key-group per side; it classifies matched / field-conflict / missing / duplicate as it goes.

Discrepancies are evaluated by the rule engine and written in batches of 500 together with a
**checkpoint** (last fully processed key) in the same transaction, so a crashed run resumes rather than restarts.

## Consequences
- Memory is bounded by page size (5,000 rows per side) regardless of table size.
- The engine refuses out-of-order input loudly: a collation mismatch is a bug, not a data issue.
- Duplicates within a source are detected for free (consecutive equal keys) and the most recently updated
  record represents the group in the comparison.
- `BenchmarkDotNet` (`tests/DistrictSync.Benchmarks`) measures the in-memory merge independent of I/O.
