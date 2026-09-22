# Deployment strategy

Target platform is **AWS** ([ADR-0012](adr/0012-aws-as-deployment-target.md)).
Architecture diagrams are in [architecture.md](architecture.md); sizing and costs in
[infrastructure-sizing.md](infrastructure-sizing.md).

## Components

| Component | Where it runs on AWS | Notes |
|---|---|---|
| Billing API | ECS Fargate, private subnets | Stateless apart from its database connection; 2–4 tasks |
| Web client | S3 + CloudFront | Static build; the `web` container is not deployed to AWS |
| Database | RDS PostgreSQL 17, Multi-AZ | Isolated subnets, no internet route |
| Backups | Scheduled ECS task → S3 → Glacier | `scripts/backup.sh` in a container, on EventBridge Scheduler |
| Images | ECR | Tagged by commit SHA, never `latest` |

**The `web` container disappears on AWS.** Locally, nginx serves the SPA and proxies
`/api`; CloudFront does both — static assets from S3, `/api/*` to the ALB. Same
single origin, so still no CORS configuration anywhere, and one fewer always-on
container to pay for.

## Services and why each was chosen

### Edge

| Service | Role | Why this and not the alternative |
|---|---|---|
| **Route 53** | DNS, health checks | Alias records to CloudFront/ALB are free and resolve inside AWS |
| **CloudFront** | CDN for the SPA; routes `/api/*` to the ALB | Collapses two jobs into one, keeps a single origin, and terminates TLS at the edge |
| **ACM** | TLS certificates | Free, auto-renewing. There is no reason to manage certificates by hand |
| **AWS WAF** | Rate limiting, IP allowlist on ingestion | Large tier only. Below that the API's own per-meter rate limiter is enough, and WAF's $13/month is not |

### Compute

| Service | Role | Why this and not the alternative |
|---|---|---|
| **ECS Fargate** | Runs the API | No EC2 hosts to patch. **Not EKS**: $73/month for a control plane to schedule four containers. **Not Lambda**: a steady 24/7 request rate is exactly where always-on containers beat per-invocation billing. **Not App Runner**: more expensive than Fargate + ALB for an always-on service, and less control over networking |
| **Application Load Balancer** | Routes to Fargate, health checks | Needed for path-based routing and target health. Its fixed ~$24/month is the reason the small tier is cheaper on a single EC2 instance |
| **EC2** (small tier only) | Single instance running `docker compose` | Three times cheaper than the managed stack at 50 meters, and runs the identical compose file |

### Data

| Service | Role | Why this and not the alternative |
|---|---|---|
| **RDS PostgreSQL 17** | The database | Managed backups, PITR and Multi-AZ failover. **Not Aurora**: 20–30% more for a workload with no scaling problem to solve. **Not DynamoDB**: the brief requires relational, and the access pattern is a btree seek |
| **RDS Multi-AZ** | Automatic failover | Medium tier and above. Doubles the database cost and is still the largest single availability improvement available |
| **RDS read replica** | Reporting and exports | Large tier only, so an analytical query cannot slow ingestion |
| **S3 + Glacier Deep Archive** | Backups, 7-year retention | Lifecycle after 30 days. Object Lock in compliance mode makes the archive immutable — the specific defence against ransomware |
| **ECR** | Container images | Scan-on-push, lifecycle policy keeping the last 20 tags |

### Supporting

| Service | Role |
|---|---|
| **Secrets Manager** | Connection string, JWT signing key. Injected as ECS task secrets so they never appear in a task definition or a log |
| **CloudWatch Logs** | Serilog output via the `awslogs` driver, 30-day retention |
| **CloudWatch Alarms** | Task count, ALB 5xx rate, RDS CPU and free storage, and — the one that matters — *backup task failed* |
| **EventBridge Scheduler** | Triggers the nightly backup task |
| **Systems Manager Session Manager** | Shell access without SSH keys or a bastion host |
| **VPC** | Public (ALB, NAT) / private (Fargate) / isolated (RDS) subnets across 2 AZs |

### Deliberately not used

EKS, Kafka/MSK/Kinesis, ElastiCache, Aurora, DynamoDB, Timestream, and IoT Core —
each with the volume at which it would become correct, in
[infrastructure-sizing.md](infrastructure-sizing.md#what-is-deliberately-absent).

## Docker: yes. Kubernetes: no.

Containers, because the same image runs on a laptop, in CI, on ECS and on an
on-premise box, and because the on-premise fallback in ADR-0012 depends on it.

Not Kubernetes, for reasons specific rather than fashionable:

- **Cost.** EKS is $73/month for the control plane before a single node — more than
  the entire small-tier bill, and 28% of the medium tier's.
- **Scale.** Two to four containers with no complex scheduling, placement or
  service-mesh requirement. Fargate covers it.
- **Operations.** Whoever maintains this is a site engineer or a small team, not a
  platform team. ECS has less to learn and less to break.

**When it would become right:** multi-tenant hosting for many utilities, where
per-tenant isolation and autoscaling stop being hypothetical. That is a different
product, and moving would be a deliberate migration rather than a default.

## Environments

| Environment | Runs on | Data | Purpose |
|---|---|---|---|
| Local | `docker compose` | Seeded demo | Development |
| CI | GitHub Actions runners | Testcontainers | Build, test, restore verification |
| Staging | One Fargate task + `db.t4g.micro` | Anonymised copy | Rehearse upgrades and migrations against realistic data |
| Production | Sized per tier | Live | — |

Staging exists to time a migration against a realistically sized `meter_readings`
table before it runs in production, not to be a second production. It costs about
$40/month and can be stopped outside working hours.

## CI/CD

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every push:

| Job | What it proves |
|---|---|
| `build-and-test` | Compiles with warnings as errors; 32 domain tests pass |
| `web` | Regenerates the API client and **fails if it drifted**; lints; type-checks; builds |
| `verify-restore` | The committed backup genuinely restores and the data is intact |
| `docker-build` | Both images build |

**The release workflow below is the target design, not a file in this repository.**
There is no live AWS account for a take-home to deploy into, and the assignment
asks deployment strategy to be planned and documented — unlike the backup, which it
explicitly asks to be designed *and executed*. `ci.yml` is real and runs on every
push; this is what a second workflow, gated on a tag and holding the OIDC role
below, would do:

1. Build and push to ECR, tagged with the commit SHA.
2. Sync the SPA build to S3 and invalidate the CloudFront distribution.
3. Register a new ECS task definition and update the service.
4. **ECS rolling deployment** with `minimumHealthyPercent: 100`, so the old tasks
   keep serving until the new ones pass their health check.
5. A CloudWatch deployment alarm rolls the service back automatically if 5xx rates
   rise after the switch.

Authentication is **OIDC from GitHub to an IAM role** — no long-lived AWS access
keys in repository secrets.

## Database migrations

Migrations are applied by the API at startup, which is correct for one instance and
a race with more than one ([ADR-0008](adr/0008-postgresql-single-node.md)).

**On AWS, from the medium tier upward, migrations move out of startup**: the release
pipeline runs a one-off ECS task with the same image and `--migrate-only` before
updating the service. That makes the ordering explicit, gives migrations their own
timeout and logs, and means a long migration cannot fail a container health check
and trigger a rollback loop.

## Rollback

Application rollback is easy; the migration is the hard part and the one usually
left unaddressed.

**Application only:** update the ECS service to the previous task definition
revision. Two to three minutes, no data implications.

**Additive migrations** (new nullable column, new table) are backward-compatible —
the previous image ignores them, so rolling back the application alone is safe.
Migrations here are written additively wherever possible for exactly this reason.

**Destructive migrations** (dropped or renamed columns) cannot be rolled back by
redeploying, because the old code expects something that no longer exists. The
procedure is: restore the pre-deployment backup, replay WAL to just before the
migration, then deploy the previous revision. This is why a destructive change is a
**two-release change** — release N stops using the column, release N+1 drops it —
and why `deploy.sh` takes a backup before touching anything.

## Provisioning

Infrastructure as code, not console clicks. Terraform is the recommendation over
CloudFormation or CDK: it is the most portable if a customer later requires an
on-premise or non-AWS deployment, which ADR-0012 leaves open.

```
infra/
  modules/{network,database,api,web,backup}/
  environments/{staging,production}/
```

Not committed in this repository — it would be a substantial piece of work whose
value here is the plan rather than the HCL, and half-finished Terraform is worse
than none. The resource list is the tables above.

## Commissioning a new deployment

1. Terraform apply the network, database and ECR repositories.
2. Populate Secrets Manager: a generated database password and
   `openssl rand -base64 48` for the JWT signing key. **Never the `.env.example`
   defaults** — `deploy.sh` refuses to run if it detects them.
3. Push the first images; run the migration task.
4. Create the real administrator account.
5. Set `ASPNETCORE_ENVIRONMENT=Production` and `SEED__ENABLED=false`. This is the
   step that gets forgotten, and the one that leaves well-known credentials on a
   production system.
6. Register meters and issue ingestion keys; record each key once, in the customer's
   own secret store. They cannot be recovered.
7. Enable the backup schedule and **run `verify-restore.sh` before signing off** —
   the first backup is the one most likely to be misconfigured.
8. Point monitoring at `/health/ready` and confirm the alarms fire by breaking
   something deliberately.

## On-premise fallback

The same images, the same `docker compose up`, per
[ADR-0009](adr/0009-on-premise-with-cloud-backup.md) (superseded but retained for
its hardware bill of materials). Nothing in the application knows which environment
it is in — configuration is environment variables and a connection string — so the
choice stays reversible.
