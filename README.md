# Smart Water Billing Platform

An on-premise IoT water billing system: meters report cumulative volume over REST,
the platform derives consumption, and an administrator issues monthly invoices
against a configurable tariff.

Built with **.NET 10 (LTS)** and **PostgreSQL 17**, runnable with one command.

> **Status:** scaffolding in progress. Sections marked _(pending)_ are being filled
> in as the corresponding work lands. Everything not so marked is implemented.

---

## Contents

- [Quick start](#quick-start)
- [What is implemented](#what-is-implemented)
- [How it works](#how-it-works)
- [Database schema](#database-schema)
- [API walkthrough](#api-walkthrough)
- [Key design decisions](#key-design-decisions)
- [Assumptions](#assumptions)
- [Testing](#testing)
- [Infrastructure sizing](#infrastructure-sizing)
- [Backup and disaster recovery](#backup-and-disaster-recovery)
- [Deployment](#deployment)
- [Project plan](#project-plan)
- [What I would improve with more time](#what-i-would-improve-with-more-time)

---

## Quick start

**Prerequisites:** Docker Desktop (or Docker Engine + Compose v2). No local .NET SDK
is required — the image builds the application.

```bash
git clone <repository-url>
cd smart-water-billing-platform
cp .env.example .env          # local-only credentials; never committed
docker compose up --build
```

That gives you:

| Thing | Where |
|---|---|
| API | <http://localhost:8080> |
| Interactive API reference (Scalar) | <http://localhost:8080/scalar/v1> |
| OpenAPI document | <http://localhost:8080/openapi/v1.json> |
| Liveness / readiness | `/health/live`, `/health/ready` |
| PostgreSQL | `localhost:5432` |

The database is migrated automatically on first start and, in `Development`, seeded
with a demo estate (see [API walkthrough](#api-walkthrough)).

### Running without Docker

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a reachable
PostgreSQL instance.

```bash
dotnet tool restore                        # restores dotnet-ef from .config/dotnet-tools.json
docker compose up -d postgres              # or point at your own PostgreSQL
dotnet run --project src/WaterBilling.Api
```

Migrations are applied automatically at startup
([ADR-0008](docs/adr/0008-postgresql-single-node.md) states why, and when that
should change). To apply them by hand, or to add one, see [`db/README.md`](db/README.md).

### Demo credentials

Development seed data only. These accounts do not exist in any other environment.

| Role | Email | Password |
|---|---|---|
| Admin | `admin@waterworks.local` | `Admin#12345` |
| Customer | `asha@example.com` | `Customer#12345` |
| Customer | `ravi@example.com` | `Customer#12345` |
| Customer | `leela@example.com` | `Customer#12345` |

Meters are seeded with deterministic ingestion keys (e.g. `wmk_demo0001_asha_flat_a101`)
so the walkthrough works immediately, without creating anything first.

---

## What is implemented

| Requirement | Status |
|---|---|
| Authentication mechanism | JWT bearer for users; per-meter API key for devices |
| Create a water meter | _(pending)_ |
| Create a user | _(pending)_ |
| View consumption info | Readings endpoint implemented; aggregated view _(pending)_ |
| REST data ingestion | Single and batch, idempotent, with anomaly detection |
| Generate previous-month invoices | _(pending)_ |
| Fixed and slab pricing, admin-configurable | Pricing engine and tariff model implemented; admin endpoints _(pending)_ |
| Validation and meaningful errors | RFC 9457 problem documents, FluentValidation |
| Soft deletes | Implemented for master data ([ADR-0010](docs/adr/0010-soft-delete-policy.md)) |
| Unit tests | Domain calculators covered; integration tests _(pending)_ |
| Multiple ingestion mechanisms | REST single + batch; CSV upload _(pending)_ |
| Payment gateway with logs | Schema in place; mock provider _(pending)_ |
| Supply cut-off (relay valve) | Schema in place; endpoints _(pending)_ |
| Webhook data egress | _(not planned — see [improvements](#what-i-would-improve-with-more-time))_ |
| Water tanks (level sensors) | _(not planned — see [improvements](#what-i-would-improve-with-more-time))_ |

---

## How it works

### The measurement model

A meter reports two values: `TOTAL` (cumulative m³, ever-increasing) and `FLOW`
(instantaneous m³/hr). **Only `TOTAL` bills anything.** `FLOW` is used as a free
consistency check and for leak detection.

Readings are written once and never modified. Consumption is derived:

```
consumption(meter, start, end)
    = TOTAL(latest reading ≤ end) − TOTAL(latest reading ≤ start)
```

walked pairwise so a meter reset is summed across rather than producing a negative
bill. The full reasoning, and the table of ingestion rules, is in
[ADR-0003](docs/adr/0003-immutable-readings-and-consumption.md).

Consequences worth knowing up front:

- **Ingestion is idempotent.** Re-posting a reading returns `200` with
  `status: "duplicate_ignored"`. A meter that retries after a timeout is always safe.
- **Arrival order does not matter.** Consumption comes from the time-ordered series.
- **Late data self-corrects.** There is no stored counter to repair.
- **Periods tile exactly.** Billing August then September totals the same as billing
  both at once — there is a test that asserts precisely this.

### Pricing

Tariffs are immutable and versioned. "Editing" a plan closes the current version and
opens a new one, so raising a rate cannot restate a bill issued last year. Each
invoice stores both the tariff version id and every line item in full.

Two shapes are supported, because "slab based (like electricity meters)" is
genuinely ambiguous:

- `Progressive` (default) — telescopic; each band charges only its own units.
- `WholeVolumeAtReachedBand` — the whole volume at the rate of the band reached.

Bands are upper-inclusive, `(from, to]`. Details in
[ADR-0004](docs/adr/0004-versioned-pricing-and-invoice-snapshots.md).

---

## Database schema

_(pending — ER diagram and per-table explanation)_

Fifteen tables. The full DDL is committed at [`db/schema.sql`](db/schema.sql), and
the model that generates it is in
[`src/WaterBilling.Infrastructure/Persistence/Configurations/`](src/WaterBilling.Infrastructure/Persistence/Configurations/).
Naming is `snake_case` throughout so the database is usable from `psql` without
quoting every identifier.

| Group | Tables |
|---|---|
| Identity | `users` |
| Assets | `meters`, `meter_api_keys` |
| Measurement | `meter_readings`, `meter_reset_events` |
| Tariffs | `pricing_plans`, `pricing_plan_versions`, `pricing_slabs` |
| Billing | `invoices`, `invoice_line_items`, `billing_runs`, `billing_run_items` |
| Money | `payments`, `payment_events` |
| Audit | `audit_log` |

Indexes that carry design weight:

| Index | Why it exists |
|---|---|
| `meter_readings (meter_id, reading_at_utc)` unique | Makes ingestion idempotent at the storage layer |
| `invoices (meter_id, period_start_utc)` unique | Makes invoice generation idempotent |
| `meter_api_keys (prefix)` | Narrows key lookup so ingestion does not hash every key |
| `invoices (customer_id, period_start_utc desc)` | The customer's invoice history screen |

---

## API walkthrough

A ready-to-run request collection lives at [`docs/api-walkthrough.http`](docs/api-walkthrough.http)
(VS Code REST Client, Rider, or `curl` equivalents in the comments).

The short version, once `docker compose up` is healthy:

```bash
# 1. Log in as the administrator
curl -s localhost:8080/api/v1/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"admin@waterworks.local","password":"Admin#12345"}'

# 2. Post a reading as a meter (note: no user token — a device key)
curl -s localhost:8080/api/v1/ingest/readings \
  -H 'content-type: application/json' \
  -H 'X-Meter-Key: wmk_demo0001_asha_flat_a101' \
  -d '{"readingAtUtc":"2026-09-19T10:00:00Z","totalM3":1240.5,"flowM3PerHour":0.4}'

# 3. Post it again — idempotent, returns duplicate_ignored rather than an error
```

---

## Key design decisions

Recorded as ADRs, written before the code rather than reconstructed afterwards.
Each states the decision, the reason, and the point at which it should be revisited.

| # | Decision |
|---|---|
| [0001](docs/adr/0001-dotnet-10-lts.md) | Target .NET 10 — the current LTS, supported to Nov 2028, while .NET 8 and 9 both expire Nov 2026 |
| [0002](docs/adr/0002-modular-monolith.md) | Modular monolith; no repository layer over `DbContext`, no application layer |
| [0003](docs/adr/0003-immutable-readings-and-consumption.md) | Readings are an immutable ledger; consumption is a pure function |
| [0004](docs/adr/0004-versioned-pricing-and-invoice-snapshots.md) | Immutable versioned tariffs; invoices denormalise their pricing |
| [0005](docs/adr/0005-per-meter-ingestion-credentials.md) | Per-meter API keys for ingestion, not user tokens |
| [0006](docs/adr/0006-time-and-billing-periods.md) | Store UTC, bill on a configured offset, half-open periods, no proration |
| [0007](docs/adr/0007-idempotent-invoice-generation.md) | Idempotent invoice generation with per-meter run reporting |
| [0008](docs/adr/0008-postgresql-single-node.md) | Single-node PostgreSQL — with the arithmetic that justifies it and the thresholds that would change it |
| [0009](docs/adr/0009-on-premise-with-cloud-backup.md) | On-premise billing path, cloud for backup and multi-site aggregation |
| [0010](docs/adr/0010-soft-delete-policy.md) | Soft delete for master data, never for financial records |

---

## Assumptions

Listed in full in [`docs/assumptions.md`](docs/assumptions.md). The ones that would
change the system most if wrong:

1. A meter belongs to at most one customer at a time; a customer may hold many meters.
2. One reading per meter per timestamp. The timestamp is the device's, not the server's.
3. Billing is monthly, on the utility's calendar month, with no proration.
4. A single currency per tariff; the platform does not convert.
5. Meters are trusted to report their own `TOTAL` honestly; tamper detection is out of scope.

---

## Testing

```bash
dotnet test                                    # everything
dotnet test tests/WaterBilling.Domain.Tests    # pure, no Docker needed
```

The solution builds with `TreatWarningsAsErrors`, so a warning fails CI rather than
accumulating.

`WaterBilling.Domain.Tests` covers the two calculations that decide what a customer
pays — consumption derivation and tariff pricing — including the boundary values
(0, exactly on a band edge, one millilitre past it) where an off-by-one would
silently mis-bill everyone in a band.

Integration tests use Testcontainers against a real PostgreSQL, because the
correctness this system depends on lives partly in unique indexes. _(pending)_

---

## Infrastructure sizing

Full recommendation with the supporting arithmetic in
[`docs/infrastructure-sizing.md`](docs/infrastructure-sizing.md). _(pending)_

---

## Backup and disaster recovery

Method, schedule, RPO/RTO and a tested restore in
[`docs/backup-and-recovery.md`](docs/backup-and-recovery.md). _(pending)_

A sanitized sample backup — schema plus synthetic data, no credentials and no real
customer data — is committed at [`backups/`](backups/), and
[`scripts/verify-restore.sh`](scripts/verify-restore.sh) restores it into a clean
container and asserts the data came back intact.

---

## Deployment

Platform, services, containerisation and CI/CD in
[`docs/deployment.md`](docs/deployment.md). _(pending)_

---

## Project plan

A waterfall plan for delivering this system with three developers, including phase
gates, role allocation and a risk register, is in
[`docs/sprint-plan.md`](docs/sprint-plan.md). _(pending)_

---

## What I would improve with more time

_(pending — written last, honestly)_

---

## Repository layout

```
src/
  WaterBilling.Domain/          Entities and pure calculators. Zero package references.
  WaterBilling.Infrastructure/  EF Core, PostgreSQL, hashing, tokens, seeding.
  WaterBilling.Api/             Vertical feature slices: endpoint + validator + DTOs.
tests/
  WaterBilling.Domain.Tests/    Pure unit tests. No database, no host.
  WaterBilling.Api.Tests/       Integration tests against real PostgreSQL.
docs/
  adr/                          Architecture decision records.
db/                             Generated schema DDL and migration instructions.
scripts/                        Backup, restore and restore-verification.
backups/                        One sanitized sample dump, for the restore drill.
```
