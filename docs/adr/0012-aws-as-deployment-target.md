# ADR-0012: AWS as the deployment target, with gateway buffering for WAN resilience

**Status:** Accepted · **Date:** 2026-09-20 · **Supersedes:** [ADR-0009](0009-on-premise-with-cloud-backup.md)

## Context

ADR-0009 put the billing-critical path on-premise and used the cloud only for
backup and aggregation. Its central argument was that ingestion must survive a WAN
outage, because a utility cannot stop measuring water when an internet link drops.

That argument was correct about the requirement and wrong about where the
requirement is satisfied. **Meters do not post to the API directly** — they speak a
field protocol to a site gateway, which translates and forwards. The gateway already
buffers when it cannot reach the platform, which is precisely why the ingestion
endpoint accepts out-of-order arrival and a batch upload path.

So the outage resilience lives at the edge, not in where the database runs.

## Decision

**AWS is the primary deployment target.** Three reference configurations, sized and
costed in [infrastructure-sizing.md](../infrastructure-sizing.md):

| Scale | Configuration |
|---|---|
| ≤ 50 meters | Single EC2 instance running the same `docker compose` stack |
| ≤ 300 meters | ECS Fargate + RDS Multi-AZ + CloudFront/S3 |
| 300+ meters | The above, scaled out, with a read replica and WAF |

On-premise remains a **supported alternative**, not the default. It stays documented
because two situations genuinely require it: a regulator that forbids billing data
leaving the premises, and a site with no reliable WAN at all.

## Consequences

- **Readings are not lost during a WAN outage.** Meters keep counting; the gateway
  buffers and replays through `POST /api/v1/ingest/readings/batch` on reconnect.
  Ingestion is idempotent, so replay is safe. Billing is monthly, so hours of
  disconnection have no effect on an invoice — and consumption is derived from TOTAL
  anchors rather than by counting readings, so even a gap spanning the period
  boundary bills correctly.
- **The 35-day backdating limit bounds the exposure.** A gateway offline longer than
  that needs an administrator to load the history deliberately. That limit was
  written for a different reason and turns out to be the right one here too.
- **The `web` container disappears.** CloudFront serves the SPA from S3 and routes
  `/api/*` to the ALB, doing nginx's job. Still one origin, so still no CORS.
- **Multi-AZ and managed backups come for free**, which no single on-premise box
  provides. Against that, RDS is a managed service with less tuning latitude.
- **It costs more.** At 300 meters, five-year TCO is roughly **$12.7k on AWS versus
  $5.2k on-premise** — about 2.4× (working in
  [infrastructure-sizing.md](../infrastructure-sizing.md)). The premium buys no
  hardware procurement, no site visits for failed disks, and cross-AZ redundancy.
  Stated plainly so the customer can make the call rather than inherit it.
- **The same images run in both places.** Nothing in the application knows which it
  is in — configuration is environment variables and a connection string — so the
  choice stays reversible.

## Revisit if

- A customer's regulator prohibits billing data leaving the premises → on-premise.
- A site's WAN outages routinely exceed the 35-day backdating limit → on-premise.
- Deployment grows past ~10 sites → a single multi-tenant AWS deployment starts to
  beat ten independently maintained boxes on both cost and operational effort.
