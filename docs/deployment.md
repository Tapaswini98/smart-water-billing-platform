# Deployment strategy

## Components

| Component | Image | Port | Notes |
|---|---|---|---|
| `web` | `nginx:1.27-alpine` + Vite build | 8080 | Serves the SPA, proxies `/api` to the API. No CORS anywhere as a result. |
| `api` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble` | 8080 | Stateless apart from its database connection |
| `postgres` | `postgres:17-alpine` | 5432 | Named volume, never a bind mount into the repo |
| `backup` | `systemd` timer on the host | — | `scripts/backup.sh` + restic offsite |

Both application images run as non-root and carry a `HEALTHCHECK`.

## Target platform: on-premise first, AWS supporting

The brief asks for an on-premise solution and separately for a cloud platform. The
tension is resolved rather than ignored ([ADR-0009](adr/0009-on-premise-with-cloud-backup.md)):
**the billing-critical path is on-premise; the cloud does the things the cloud is
strictly better at.**

| Concern | Where | Why |
|---|---|---|
| Meters, ingestion, database, invoicing | **On-premise** | Meters are on the LAN. Ingestion and billing must survive a WAN outage — a utility cannot stop measuring water because an internet link is down. |
| Offsite backup | **AWS S3 + Glacier** | Survives fire, theft and flood. A NAS in the same building does not. |
| Container registry | **AWS ECR** | Signed, versioned images; sites pull rather than build. |
| CI/CD | **GitHub Actions** | Build, test, verify the backup, publish images. |
| Secrets | **AWS Secrets Manager** (multi-site) or `systemd` credentials (single site) | |
| Multi-site aggregation, monitoring | **Optional cloud control plane** | Fed asynchronously, so a link failure degrades reporting and never billing. |

### AWS services, if a hosted deployment is wanted

For a demo or a customer without a plant room, the same containers run unchanged:

| Service | Role |
|---|---|
| **ECS Fargate** | Runs `api` and `web`. No EC2 hosts to patch. |
| **RDS for PostgreSQL 17** | Multi-AZ, automated backups, PITR |
| **Application Load Balancer** | TLS termination via ACM |
| **ECR** | Image registry |
| **S3 + Glacier** | Backups, lifecycle to Glacier after 30 days |
| **CloudWatch Logs** | Serilog output |
| **Secrets Manager** | Connection string, JWT signing key |
| **VPC, private subnets** | RDS unreachable from the internet |

**EC2 is deliberately not the answer** for the hosted variant: Fargate removes host
patching for a workload that never needs host-level access. For the on-premise
deployment the question does not arise — the hardware is the customer's.

## Docker: yes. Kubernetes: no.

**Docker Compose**, for reasons that are specific rather than fashionable:

- One or two nodes in a plant room. Kubernetes adds a control plane that must itself
  be operated, for orchestration this workload does not need.
- The person maintaining it is a site engineer, not a platform team. `docker compose
  ps` and `docker compose logs` are learnable in an afternoon.
- 0.33 writes/second ([sizing](infrastructure-sizing.md)) needs no horizontal scaling.
- Rolling updates across two containers are not worth a scheduler.

**When Kubernetes would become right:** a hosted multi-tenant deployment serving many
utilities, where per-tenant isolation and autoscaling stop being hypothetical. That
is a different product, and it would be a deliberate migration rather than a
default. Stated so the choice can be re-examined rather than inherited.

## Environments

| Environment | Runs on | Data | Purpose |
|---|---|---|---|
| Local | Developer machine, `docker compose` | Seeded demo | Development |
| CI | GitHub Actions ephemeral runners | Testcontainers | Build, test, restore verification |
| Staging | One on-premise-identical VM | Anonymised copy | Rehearse upgrades against real-shaped data |
| Site | Customer hardware | Live | Production |

Staging exists to rehearse the upgrade, not to be a second production. It is the
only place a migration against a realistically sized `meter_readings` table gets
timed before it runs on a customer's box.

## CI/CD

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every push:

| Job | What it proves |
|---|---|
| `build-and-test` | Compiles with warnings as errors; domain tests pass |
| `web` | Regenerates the API client and **fails if it drifted**; lints; type-checks; builds |
| `verify-restore` | The committed backup genuinely restores and the data is intact |
| `docker-build` | Both images build |

Release adds image publishing to ECR, tagged with the commit SHA — never `latest`,
so a site can always be told exactly which build it is running.

### Delivering to a site

Sites sit behind intermittent links, so updates are **pulled on a schedule, not
pushed**:

```bash
scripts/deploy.sh --tag 1.4.0     # pull, back up, recreate, verify, roll back on failure
```

The script takes a pre-upgrade backup before touching anything, and rolls back
automatically if the health check fails. Both behaviours exist because a failed
upgrade on a customer's site is a van dispatch.

## Rollback

Application rollback is easy; the migration is the hard part, and the one usually
left unaddressed.

**Application only:** re-deploy the previous image tag. Seconds.

**Application + schema:** additive migrations (new nullable column, new table) are
backward-compatible — the previous image ignores them, so rolling back the
application alone is safe. That is why migrations here are written additively
wherever possible.

**Destructive migrations** (dropped or renamed columns) cannot be rolled back by
redeploying, because the old code expects a column that no longer exists. The
procedure is: restore the pre-upgrade backup, replay WAL to just before the
migration, then redeploy the old image. This is precisely why `deploy.sh` takes a
backup first, and why a destructive migration is a two-release change — release N
stops using the column, release N+1 drops it.

## Commissioning a new site

1. Install Debian 12, create the service user, install Docker Engine + Compose.
2. Clone the repository (or copy a release bundle), `cp .env.example .env`.
3. Generate real secrets: `openssl rand -base64 48` for `JWT__SIGNINGKEY`, and a
   database password. **Never ship the `.env.example` defaults.**
4. `docker compose up -d` — migrations apply automatically on first start.
5. Create the real administrator, then **disable seeding** (`SEED__ENABLED=false`)
   and set `ASPNETCORE_ENVIRONMENT=Production`.
6. Register meters and issue their ingestion keys; record each key once, in the
   customer's own secret store.
7. Install and enable the backup timer; **run `verify-restore.sh` before leaving
   site** — the first backup is the one most likely to be misconfigured.
8. Point monitoring at `/health/ready`.

Step 5 is the one that gets forgotten, and it is the one that leaves well-known
credentials on a production box.
