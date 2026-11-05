# ADR 0001: Modular monolith with two deployables, orchestrated by .NET Aspire

**Status:** Accepted · **Date:** 2026-11-04

## Context
The platform has clear bounded contexts (ingestion, reconciliation, rules, cases/workflow, operations) and
clear asynchronous seams between them. A microservice-per-context layout would demonstrate "distributed
systems" but multiply the operational surface (databases, deployments, contracts) for a team of one.

## Decision
One solution with strict layering (`Domain ← Application ← Infrastructure ← hosts`) compiled into **two
runtime processes**: `DistrictSync.Api` (synchronous HTTP + SignalR) and `DistrictSync.Worker` (consumers,
outbox dispatcher, workflow runner). Both share one SQL Server database and one RabbitMQ broker. Layer
boundaries are enforced by `DistrictSync.ArchitectureTests`, not by convention. `.NET Aspire` wires the
processes, containers, connection strings and OpenTelemetry for local runs.

## Consequences
- The asynchronous boundary that matters (API → broker → worker) is real; everything else stays in-process.
- Horizontal scale is per process: N workers compete on queues and lease-claim outbox/workflow rows.
- Splitting a context into its own service later means moving a folder, not rewriting contracts: the
  `Contracts` assembly already defines the wire format independently of the domain model.
