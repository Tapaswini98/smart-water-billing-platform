# Smart Water Billing Platform

An on-premise IoT water billing system: meters report cumulative volume over REST,
the platform derives consumption, and an administrator issues monthly invoices
against a configurable tariff.

Built with **.NET 10 (LTS)** and **PostgreSQL 17**, runnable with one command.

All required functionality is implemented, and the backup has been executed and
verified — see [Backup and disaster recovery](#backup-and-disaster-recovery).

---

## Contents

- [Quick start](#quick-start)
- [What is implemented](#what-is-implemented)
- [Architecture](#architecture)
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
| Web UI | <http://localhost:3000> |
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

### Required

| Requirement | Status |
|---|---|
| Authentication (Admin / Customer roles) | JWT bearer for users; per-meter API key for devices |
| Create a water meter | `POST /api/v1/meters` — issues the ingestion key in the same response |
| Create a user | `POST /api/v1/users` — PBKDF2-HMAC-SHA512 hashing |
| Customer holds one or more meters | Assignment endpoint; every query scoped server-side |
| View consumption info | Per-period and month-by-month, derived from the reading series |
| REST data ingestion (TOTAL / FLOW) | Single and batch, idempotent, with anomaly detection |
| Generate previous-month invoices | Idempotent billing runs with per-meter outcomes |
| Fixed **or** slab pricing, admin-configurable | Both, with progressive and whole-volume slab modes |
| Customer views current and previous invoices | List and full breakdown, scoped to their own |
| Documentation | This file, 11 ADRs, and four deliverable documents |

### Appreciated

| Feature | Status |
|---|---|
| Validation and meaningful errors | RFC 9457 problem documents, FluentValidation, domain-level tariff validation |
| Soft deletes | Master data only, never financial records ([ADR-0010](docs/adr/0010-soft-delete-policy.md)) |
| Unit tests | 32 tests over the two calculations that decide what a customer pays |
| Multiple ingestion mechanisms | REST single + REST batch (store-and-forward) |
| Payment gateway with logs | Mocked provider, every attempt logged, idempotent on a caller key |
| Supply cut-off (relay valve) | Desired-vs-reported valve state, with a mandatory audited reason |
| Web UI | React + TypeScript, client generated from the OpenAPI contract |
| Webhook data egress | **Not built** — see [what I would improve](#what-i-would-improve-with-more-time) |
| Water tanks (level sensors) | **Not built** — see [what I would improve](#what-i-would-improve-with-more-time) |

## Architecture

Diagrams — system context, AWS deployment, the ingestion-to-invoice flow, the two
authentication schemes, and the data model — are in
[`docs/architecture.md`](docs/architecture.md). They render inline on GitHub.

Deployment target is **AWS** ([ADR-0012](docs/adr/0012-aws-as-deployment-target.md)):
CloudFront and S3 for the web client, ECS Fargate behind an ALB for the API, and RDS
PostgreSQL Multi-AZ. The `web` container exists only for local development — on AWS
CloudFront does nginx's job of serving the SPA and routing `/api/*`.

On-premise remains supported and is roughly 2.4× cheaper over five years; the
comparison is in [`docs/infrastructure-sizing.md`](docs/infrastructure-sizing.md).

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

Fifteen tables. Full DDL is committed at [`db/schema.sql`](db/schema.sql); the model
that generates it is in
[`src/WaterBilling.Infrastructure/Persistence/Configurations/`](src/WaterBilling.Infrastructure/Persistence/Configurations/).
Naming is `snake_case` throughout so the database is usable from `psql` without
quoting every identifier.

```
users ──────< meters >────── pricing_plans ──< pricing_plan_versions ──< pricing_slabs
                │  │                                     │
                │  └──< meter_api_keys                    │ (cited by)
                │                                         │
                ├──< meter_readings                       │
                ├──< meter_reset_events                   │
                │                                         │
                └──< invoices >───────────────────────────┘
                       │  │
                       │  └──< invoice_line_items
                       └──< payments ──< payment_events

billing_runs ──< billing_run_items >── meters, invoices
audit_log (standalone, append-only)
```

### The tables that carry design weight

**`meter_readings`** — the immutable ledger. Never updated, never deleted. Every
invoice is reproducible from it. `total_m3` is the billing authority;
`flow_m3_per_hour` is diagnostic only. `anomalies` is a flags column, so one reading
can be both a reset and implausible against its reported flow.

**`pricing_plan_versions`** — immutable, valid over `[effective_from, effective_to)`.
"Editing" a tariff closes the current version and opens a successor, so raising a
rate cannot restate a bill issued last year.

**`invoices`** — stores both the tariff version it cites **and** its line items in
full, so it reads today exactly as it did when issued, without resolving a tariff
that may have been superseded four times since.

**`billing_runs` / `billing_run_items`** — the answer to "did billing work this
month?", with a reason recorded for every meter that was not invoiced.

### Indexes that carry guarantees

| Index | Why it exists |
|---|---|
| `meter_readings (meter_id, reading_at_utc)` unique | Makes ingestion idempotent at the storage layer, not in application code |
| `invoices (meter_id, period_start_utc)` unique | Makes invoice generation idempotent — re-running a period cannot double-bill |
| `payments (idempotency_key)` unique, partial | A retried checkout cannot charge twice |
| `pricing_plans (is_default)` unique, partial | At most one default plan, enforced by the database rather than by a service-layer check two admins can race |
| `meter_api_keys (prefix)` | Narrows key lookup so ingestion does not hash every key in the estate |
| `users (email)` unique, partial on `deleted_at_utc IS NULL` | Live addresses stay unique; a deleted one becomes reusable |

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
| [0009](docs/adr/0009-on-premise-with-cloud-backup.md) | On-premise billing path, cloud for backup — _superseded by 0012_ |
| [0010](docs/adr/0010-soft-delete-policy.md) | Soft delete for master data, never for financial records |
| [0011](docs/adr/0011-frontend-architecture.md) | React SPA with a generated API client; no client-side state library |
| [0012](docs/adr/0012-aws-as-deployment-target.md) | AWS as the deployment target; gateway buffering provides WAN resilience |

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
dotnet test                                    # 32 domain tests, no Docker needed
cd web && npm run lint && npm run build        # lint, type-check, bundle
scripts/verify-restore.sh --local              # backup restore drill
```

`WaterBilling.Domain.Tests` covers the two calculations that decide what a customer
pays — consumption derivation and tariff pricing — including the boundary values
(0, exactly on a band edge, one millilitre past it) where an off-by-one would
silently mis-bill everyone in a band, and the property that August + September must
equal both months billed together.

The solution builds with `TreatWarningsAsErrors`. The web client is generated from
the API's OpenAPI document, and CI fails if the committed client has drifted from it.

### Verified by hand against a live database

Integration tests are the main gap (see [improvements](#what-i-would-improve-with-more-time)).
These behaviours were confirmed manually against PostgreSQL with 44,041 seeded readings:

| Behaviour | Result |
|---|---|
| Migrations apply to an empty database | 15 tables created |
| Consumption across a register reset | Summed across the discontinuity, flagged, never negative |
| Consumption across a 30-hour reporting gap | Correct — derived from anchors, not from counting readings |
| Billing run, previous month | 5 invoices, 1 meter skipped with a stated reason |
| **Re-running the same period** | 0 new invoices, all reported `AlreadyBilled` |
| Slab breakdown on an invoice | All four bands, telescopic, totals reconcile |
| Customer listing invoices | Sees only their own |
| Customer passing `?customerId=` for another account | Ignored — scoping is applied before caller filters |
| Customer reading another customer's invoice | `403` |
| Customer calling an admin endpoint | `403` |
| Duplicate reading posted twice | `200 duplicate_ignored` |
| Customer JWT on the ingestion endpoint | `401` |
| Payment retried with the same idempotency key | One payment row, not two |
| Tariff with a gap between bands | `422` with the specific bands named |

## Infrastructure sizing

Three AWS reference configurations, each costed line by line in
[`docs/infrastructure-sizing.md`](docs/infrastructure-sizing.md).

| Scale | Configuration | Monthly | Per meter |
|---|---|---:|---:|
| **Small** (≤ 50 meters) | Single EC2 running the same `docker compose` | **$21** | $0.41 |
| Small — managed alternative | ECS Fargate + RDS + ALB | $64 | $1.29 |
| **Medium** (≤ 300 meters) | ECS Fargate ×2 + RDS Multi-AZ + CloudFront | **$256** | $0.85 |
| **Large** (2,000 meters) | The above scaled, plus read replica and WAF | **$977** | $0.49 |

`ap-south-1`, on-demand list prices, September 2026. One-year Savings Plans and
Reserved Instances take medium to ~$212/month and large to ~$796/month.

The sizing is driven by one measured number: 300 meters reporting every 15 minutes
is **0.33 writes per second** and ~1.6 GB/year — taken from the seeded database, not
estimated. That is three orders of magnitude below what a single PostgreSQL instance
handles, which is *why* there is no queue, no sharding and no Kubernetes. The
document lists what was deliberately left out and the volume at which each would
become correct.

It also gives the honest comparison: at 300 meters, five-year TCO is **$12.7k on AWS
against $5.2k on-premise**. The premium buys Multi-AZ failover, managed backups and
no hardware to procure or replace. Whether that is worth it is the customer's call —
the numbers are there so they can make it.

## Backup and disaster recovery

Method, schedule, RPO/RTO and recovery procedures in
[`docs/backup-and-recovery.md`](docs/backup-and-recovery.md).

**The backup has been executed.** [`backups/`](backups/) contains a real
`pg_dump` custom-format backup taken from a running instance *after exercising the
application* — three months of readings ingested, a billing run executed, invoices
issued, a payment recorded — together with its SHA-256, a manifest recording tool
versions and per-table row counts, and a log of an actual restore drill.

```bash
scripts/backup.sh                  # take a backup (+ checksum + manifest)
scripts/verify-restore.sh          # restore into a throwaway target and check it
scripts/verify-restore.sh --local  # same, without Docker
```

The verifier does not just count rows: it recomputes every invoice total from its
own line items and fails if they disagree. That catches a restore that completed but
lost rows from a child table — which a row count on the parent would report as
healthy. A recorded run is at [`backups/restore-drill.log`](backups/restore-drill.log),
and the check runs in CI.

## Deployment

Platform, services and CI/CD in [`docs/deployment.md`](docs/deployment.md), with
[`scripts/deploy.sh`](scripts/deploy.sh) implementing the on-premise path
(pre-deployment backup → recreate → health check → automatic rollback).

**AWS**, with each service choice argued against its alternative: ECS Fargate rather
than EKS ($73/month for a control plane to schedule four containers) or Lambda (a
steady 24/7 rate is where always-on containers win); RDS PostgreSQL rather than
Aurora (20–30% more for a scaling problem that does not exist); CloudFront + S3
rather than a second container serving static files.

Release is OIDC from GitHub to an IAM role — no long-lived AWS keys in repository
secrets — with ECS rolling deployment and a CloudWatch alarm that rolls back on a
5xx spike. From the medium tier upward, migrations move out of application startup
into a dedicated pipeline task, per the threshold in
[ADR-0008](docs/adr/0008-postgresql-single-node.md).

## Project plan

[`docs/sprint-plan.md`](docs/sprint-plan.md) — **six weeks, three developers,
shipping continuously.**

The brief asks for waterfall. Waterfall's value is its decision gates; its cost is
serialisation. This plan keeps all five gates — scope, design, billing verified, UAT,
go-live — and drops the serialisation, because a team that cannot deploy for five
weeks does not discover it was wrong until week six. Every week ends with something
on staging, and week 4 is the pivot: the first invoice generated from real readings.

It includes what a plan written under deadline pressure usually leaves out:

- **Scope triage agreed on day one** (MoSCoW), so cutting in week 5 is a decision
  already made rather than a negotiation at 11pm.
- **A short list of what deadline pressure does not get to touch** — boundary tests
  on consumption and pricing, authorization tests, backup verification in CI,
  two-person review on migrations. Short precisely so it holds when the week is bad.
- **A technical debt ledger with payback dates**, where items past their date block
  new feature work. Debt without a date is a hope, not a decision.
- **Sustained overtime as a tracked risk** with "cut scope, not weekends" as the
  mitigation.
- DORA targets, and the deployment pipeline that makes daily production releases
  reasonable rather than reckless.

The pure 12-week serial variant is included too, for a customer whose regulator
requires documented sign-off before implementation.

## What I would improve with more time

Ordered by what I would actually do next.

1. **Integration tests against real PostgreSQL.** The domain calculations are well
   covered by 32 unit tests, but the guarantees that matter most — idempotent
   ingestion, idempotent billing, the authorization boundary — live in unique indexes
   and endpoint filters. I verified them by hand against a live database (the
   evidence is in this README); they should be Testcontainers tests that run on every
   push. This is the single biggest gap.

2. **Webhook egress.** The `billing_run` and reading-anomaly events are the obvious
   payloads. Doing it properly means a delivery table with retry and exponential
   backoff, HMAC-signed payloads, and a dead-letter path — which is why it did not
   fit. A naive fire-and-forget POST would have been worse than not building it.

3. **Water tanks with level sensors.** A second device class with its own ingestion
   path and its own anomaly rules (a *falling* level is normal, unlike a falling
   meter TOTAL). Clean to add; simply not required by the core billing flow.

4. **Automated supply cut-off.** The valve state machine and its audit trail exist,
   but nothing yet *decides* to close a valve. That decision needs a policy engine
   with grace periods, notification thresholds and regulatory constraints — cutting
   off a household's water on a timer, with no human in the loop, would be
   irresponsible to ship.

5. **CSV bulk ingestion.** Listed as "multiple ingestion mechanisms"; REST single and
   batch are implemented, CSV upload is not.

6. **httpOnly cookie auth instead of `sessionStorage`.** The current token is
   readable by any script on the origin. The fix is a cookie plus CSRF protection —
   noted as a limitation rather than quietly left.

7. **Meter mTLS.** Per-meter API keys are a reasonable exercise answer; client
   certificates issued by the utility's own CA are the production answer. It needs a
   certificate lifecycle an installer can operate, which is a larger piece of work
   than this assignment.

8. **Invoice PDFs and email delivery.** Customers expect a document, not a JSON
   response.

9. **Partitioning `meter_readings` by month**, at the ~50 M row threshold in
   [ADR-0008](docs/adr/0008-postgresql-single-node.md). Not needed yet, and doing it
   early would be exactly the over-engineering the sizing document argues against.

### What I would do differently

The `MeterResponse` projection was originally a plain method called inside a LINQ
`Select`. It compiled, ran, and returned every navigation property as `null` —
because EF client-evaluated it. I caught it by looking at real seeded output rather
than from a test, which is itself the argument for point 1 above.

## Repository layout

```
src/
  WaterBilling.Domain/          Entities and pure calculators. Zero package references.
  WaterBilling.Infrastructure/  EF Core, PostgreSQL, hashing, tokens, seeding.
  WaterBilling.Api/             Vertical feature slices: endpoint + validator + DTOs.
tests/
  WaterBilling.Domain.Tests/    Pure unit tests. No database, no host.
  WaterBilling.Api.Tests/       Integration tests against real PostgreSQL.
web/                            React + TypeScript client. See web/README.md.
openapi/                        Contract emitted by the API build; the web client is generated from it.
docs/
  architecture.md               System, AWS deployment, data flow and schema diagrams.
  adr/                          Architecture decision records.
db/                             Generated schema DDL and migration instructions.
scripts/                        Backup, restore and restore-verification.
backups/                        One sanitized sample dump, for the restore drill.
```
