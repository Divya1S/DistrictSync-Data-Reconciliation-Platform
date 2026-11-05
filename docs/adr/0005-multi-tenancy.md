# ADR 0005: Shared-schema multi-tenancy with defence in depth

**Status:** Accepted · **Date:** 2026-11-04

## Context
Tenants (school districts, business units) must never see or affect each other's data or processing, but
one database per tenant is operationally expensive for a platform expected to host many small tenants.

## Decision
**Data isolation** — one schema, `TenantId` on every tenant-scoped row, three independent guards:
1. A strongly-typed `TenantId` so ids cannot be mixed up at compile time.
2. EF Core **global query filters** on every `ITenantScoped` entity, bound to the ambient `ITenantContext`
   (JWT claim in the API, message header in the worker). Only explicit "system" scopes bypass them.
3. A **write guard** in `SaveChanges` that throws `TenantIsolationViolationException` if any added/modified
   row belongs to another tenant. Cross-tenant reads look like 404s to callers.

**Processing isolation** — per-tenant quotas (`TenantQuotas`): the API refuses runs beyond
`MaxConcurrentReconciliations`, the ingestion surface is rate-limited per tenant (token bucket), and the
worker's `TenantWorkLimiter` caps in-flight jobs per tenant so a bulk load from one tenant cannot occupy
every worker slot.

## Consequences
- Operational simplicity of one database; isolation verified by integration tests (`Tenants_cannot_see_each_others_data`).
- A dedicated-database tier for large tenants would mean a connection-string resolver per tenant; the
  `IAppDbContext` seam already supports that.
- Webhook sources authenticate with a per-source API key whose hash is bound to the source row, so the key
  itself selects the tenant.
