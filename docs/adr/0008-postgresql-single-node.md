# ADR-0008: Single-node PostgreSQL, migrations applied at startup

**Status:** Accepted · **Date:** 2026-09-19

## Context

Sizing the write path from the brief's own numbers: 300 meters reporting every
15 minutes is **28,800 rows/day**, **~10.5 M rows/year**, roughly **1.5–2 GB/year**
with indexes, at an average of **0.33 writes/second**. Peak, if every meter reported
in the same second, is 300 writes — which PostgreSQL absorbs without noticing.

## Decision

One PostgreSQL 17 instance. No queue, no stream processor, no sharding, no read
replicas. EF Core migrations are applied by the API at startup.

## Consequences

- The arithmetic above is three orders of magnitude below what a 4-core / 16 GB box
  with an SSD handles comfortably. Introducing Kafka or a sharding scheme here would
  add operational surface to a system that must be maintained by whoever is on site.
- Startup migration makes `docker compose up` a single command and removes a manual
  step that gets forgotten during a site visit.
- **Stated limits — the points at which this decision changes:**
  - **More than one API replica**, or a migration that rewrites a large table:
    migrations move to a separate job in the pipeline, because concurrent startup
    migration is a race and a long one blocks the health check.
  - **~50 M readings** (roughly 5 years at 300 meters, or 1 year at 1,500):
    partition `meter_readings` by month.
  - **Sustained read load from reporting**: add a streaming replica and point
    read-only queries at it.
  - **> 5,000 meters**: revisit the whole write path, starting with batching
    ingestion writes.
