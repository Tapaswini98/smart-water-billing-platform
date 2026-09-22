# Infrastructure sizing and cost

Three AWS reference configurations, each with the arithmetic behind it and a costed
bill of materials. Architecture diagrams are in [architecture.md](architecture.md);
the reasoning for AWS over on-premise is [ADR-0012](adr/0012-aws-as-deployment-target.md).

**Pricing basis:** `ap-south-1` (Mumbai), on-demand list prices, 730 hours/month,
USD, as of September 2026. Chosen to match the INR tariffs in the domain model.
These are indicative — verify against the
[AWS Pricing Calculator](https://calculator.aws) before quoting a customer, because
list prices move and every account has its own discounts.

---

## The workload, in numbers

At the design cadence of **one reading per meter every 15 minutes** (96/day). Row
size is measured from the live database, not estimated: 44,040 seeded readings
produce a 1.3 MB compressed dump, so ~31 bytes/row compressed and ~150 bytes/row on
disk with indexes.

| | Small (≤ 50) | Medium (≤ 300) | Large (2,000) |
|---|---|---|---|
| Readings / day | 4,800 | 28,800 | 192,000 |
| Readings / year | 1.75 M | 10.5 M | 70 M |
| **Average write rate** | 0.06 /s | **0.33 /s** | 2.2 /s |
| Burst (all meters at once) | 50 | 300 | 2,000 |
| Data growth / year | ~0.3 GB | **~1.6 GB** | ~10 GB |
| 5-year retention | ~1.5 GB | ~8 GB | ~50 GB |
| API requests / month | ~150 k | ~900 k | ~5.8 M |

**0.33 writes per second.** That number drives every decision below. A single
PostgreSQL instance handles thousands; the whole five-year medium dataset fits in
RAM. This is why there is no queue, no stream processor, no sharding and no
Kubernetes — see [what is deliberately absent](#what-is-deliberately-absent).

---

## Small — up to 50 meters

At this size the managed-services premium is most of the bill, so there are two
honest answers.

### Option A — single EC2 running the same `docker compose` *(recommended)*

Identical stack to local development. One instance, one disk, no orchestration.

| Service | Specification | Monthly |
|---|---|---:|
| EC2 `t4g.small` | 2 vCPU, 2 GB, Graviton, on-demand | $13.43 |
| EBS `gp3` | 50 GB | $4.56 |
| S3 + Glacier Deep Archive | ~2 GB hot, ~10 GB archived | $0.07 |
| ECR | 2 images, ~2 GB | $0.20 |
| Route 53 | 1 hosted zone | $0.50 |
| CloudWatch Logs | ~2 GB/month ingested | $1.21 |
| Data transfer out | ~5 GB | $0.55 |
| **Total** | | **$20.52** |

**$21/month · $246/year · $0.41 per meter per month**

Also needs: an Elastic IP (free while attached), ACM certificate (free), and
Caddy or nginx on the instance for TLS.

**The trade:** no Multi-AZ, no managed backups, and a reboot is downtime. For 50
meters reporting every 15 minutes, with gateways that buffer, that is a reasonable
trade — and it is *three times cheaper* than Option B.

### Option B — managed (ECS Fargate + RDS)

| Service | Specification | Monthly |
|---|---|---:|
| ECS Fargate | 1 task, 0.5 vCPU, 1 GB, 24/7 | $20.72 |
| Application Load Balancer | 1 ALB + ~1 LCU | $24.38 |
| RDS `db.t4g.micro` | Single-AZ, 20 GB `gp3` | $15.44 |
| CloudFront + S3 | SPA, ~2 GB transfer | $0.40 |
| Secrets Manager | 2 secrets | $0.80 |
| CloudWatch Logs | ~2 GB/month | $1.21 |
| ECR · Route 53 · S3 backups · scheduled backup task | | $0.97 |
| Data transfer out | ~5 GB | $0.55 |
| **Total** | | **$64.47** |

**$64/month · $774/year · $1.29 per meter per month**

The ALB alone is 38% of this bill and is a fixed cost regardless of traffic. NAT
Gateway is avoided entirely by placing Fargate tasks in public subnets with a
security group that permits no inbound traffic — a documented trade of a routable
address for $41/month, reasonable at this size and **not** carried into the tiers
below.

---

## Medium — up to 300 meters

The reference configuration. 0.33 writes/second, ~1.6 GB/year.

| Service | Specification | Monthly |
|---|---|---:|
| ECS Fargate | 2 tasks × 0.5 vCPU / 1 GB — two for availability, not throughput | $41.45 |
| Application Load Balancer | 1 ALB + ~2 LCU | $31.03 |
| RDS `db.t4g.medium` | **Multi-AZ**, 100 GB `gp3` | $128.12 |
| NAT Gateway | 1, ~20 GB processed | $42.00 |
| CloudFront + S3 | SPA, ~10 GB transfer | $1.95 |
| CloudWatch | 10 GB logs + 12 alarms | $7.87 |
| Secrets Manager | 3 secrets | $1.20 |
| S3 + Glacier | backups, ~45 GB total | $0.20 |
| ECR · Route 53 · scheduled backup task | | $0.95 |
| Data transfer out | ~15 GB | $1.64 |
| **Total (on-demand)** | | **$256.41** |
| **With 1-year Savings Plan + RDS Reserved Instance** | | **~$212** |

**$256/month on-demand · $3,077/year · $0.85 per meter per month**
**$212/month committed · $2,544/year · $0.71 per meter per month**

RDS Multi-AZ doubles both instance and storage cost — $128 of a $256 bill. It buys
automatic failover to a standby in another AZ, which is the single largest
availability improvement available at this tier and the reason it is not optional
here.

### PostgreSQL parameter group

RDS defaults are sized conservatively. For `db.t4g.medium` (4 GB):

```
shared_buffers              = {DBInstanceClassMemory/4}   # RDS default formula, ~1 GB
effective_cache_size        = {DBInstanceClassMemory*3/4}
work_mem                    = 16MB
maintenance_work_mem        = 512MB
random_page_cost            = 1.1        # gp3, not spinning rust
checkpoint_completion_target= 0.9
log_min_duration_statement  = 1000       # surface slow queries before customers do
```

---

## Large — 300+ meters (modelled at 2,000)

2.2 writes/second, ~10 GB/year. Still a single-node *write* workload; what changes
is availability and read capacity, not throughput.

| Service | Specification | Monthly |
|---|---|---:|
| ECS Fargate | 4 tasks × 1 vCPU / 2 GB, target-tracking autoscaling | $165.80 |
| Application Load Balancer | 1 ALB + ~5 LCU | $50.96 |
| RDS `db.m6g.large` | **Multi-AZ**, 500 GB `gp3` | $404.08 |
| RDS read replica | `db.m6g.large`, 500 GB — reporting and exports | $202.04 |
| NAT Gateway | 2, one per AZ | $84.56 |
| CloudWatch | 50 GB logs + 25 alarms + dashboards | $35.86 |
| AWS WAF | Web ACL, 5 rules, ~6 M requests | $13.60 |
| CloudFront + S3 | SPA, ~50 GB transfer | $9.40 |
| Secrets Manager | 4 secrets | $1.60 |
| S3 + Glacier | backups, ~265 GB total | $0.83 |
| ECR · Route 53 · scheduled backup task | | $1.30 |
| Data transfer out | ~60 GB | $6.56 |
| **Total (on-demand)** | | **$976.59** |
| **With 1-year Savings Plan + RDS Reserved Instances** | | **~$796** |

**$977/month on-demand · $11,719/year · $0.49 per meter per month**
**$796/month committed · $9,552/year · $0.40 per meter per month**

### What changes at this tier, beyond size

- **Read replica.** Reporting and CSV exports move off the primary so a long
  analytical query cannot slow ingestion.
- **Partition `meter_readings` by month** past ~50 M rows (~5 years at 2,000
  meters). Detaching an old partition then becomes a metadata operation rather than
  a `DELETE` that has to be vacuumed.
- **More than one API task crosses the threshold in
  [ADR-0008](adr/0008-postgresql-single-node.md)**, so migrations move out of
  application startup into a separate ECS task in the pipeline.
- **WAF** in front of CloudFront: rate limiting and an IP allowlist for the
  ingestion path, which is the only endpoint reachable by devices on customer
  premises.

---

## Cost summary

| | Small (EC2) | Small (ECS) | Medium | Large |
|---|---:|---:|---:|---:|
| Meters | 50 | 50 | 300 | 2,000 |
| Monthly, on-demand | $21 | $64 | $256 | $977 |
| Monthly, committed | — | — | $212 | $796 |
| Annual, on-demand | $246 | $774 | $3,077 | $11,719 |
| **Per meter per month** | **$0.41** | **$1.29** | **$0.85** | **$0.49** |

Cost per meter *falls* as scale rises, because the fixed services — ALB, NAT, Route
53, the load balancer's baseline — dominate a small deployment and barely move as
meters are added. The implication is commercial rather than technical: below roughly
150 meters, the managed configuration is hard to justify against a single instance.

### Five-year TCO versus on-premise, at 300 meters

| | On-premise | AWS on-demand | AWS committed |
|---|---:|---:|---:|
| Hardware (1U server, 32 GB, RAID 1) | $3,150 | — | — |
| Power (~150 W @ $0.09/kWh) | $591 | — | — |
| Offsite backup to S3 | $300 | included | included |
| Maintenance labour (~4 h/year) | $1,200 | — | — |
| Cloud services | — | $15,385 | $12,720 |
| **5-year total** | **$5,241** | **$15,385** | **$12,720** |
| **Monthly equivalent** | **$87** | **$256** | **$212** |

**On-premise is roughly 2.4× cheaper over five years at this scale.** That is worth
stating plainly rather than burying, and it is presumably part of why the brief
specifies an on-premise solution.

What the AWS premium buys: no hardware procurement or refresh cycle, no site visit
when a disk fails, Multi-AZ failover, managed patching and backups, and the ability
to add a second site without buying anything. Whether that is worth $7.5k over five
years is the customer's judgement — the numbers are here so they can make it.

The on-premise bill of materials is retained in
[ADR-0009](adr/0009-on-premise-with-cloud-backup.md) and the same containers run in
both places unchanged, so the decision stays reversible.

---

## What is deliberately absent

| Not used | Why not | When it would become right |
|---|---|---|
| Kafka / MSK / Kinesis | 0.33 writes/second. The queue would carry less traffic than its own heartbeat, and costs more than the database. | Sustained ingest above ~1,000 writes/second, or fan-out to several consumers |
| EKS / Kubernetes | Two to four containers with no complex scheduling. EKS adds $73/month for the control plane alone, before any node. | Multi-tenant hosting for many utilities, where per-tenant isolation stops being hypothetical |
| DynamoDB / Timestream | The brief requires a relational database, and the access pattern — "latest reading at or before X" — is a two-column btree seek | Never, under this brief |
| ElastiCache / Redis | Nothing is expensive enough to cache. Consumption is two index seeks. | Read-heavy dashboards at 10× the current scale |
| Aurora | ~20–30% more than RDS PostgreSQL for a workload with no scaling problem to solve | Read replicas beyond two, or a need for sub-second failover |
| Lambda for ingestion | A steady 24/7 request rate is exactly the shape where always-on containers beat per-invocation billing | Bursty or intermittent traffic |
| AWS IoT Core | Meters speak REST via a gateway, which is what the brief specifies | Meters speaking MQTT directly, where IoT Core's device registry and shadows earn their $1.20/million messages |

Each becomes correct at some scale. None is correct at these scales, and adding one
would mean paying for operational complexity that buys nothing.

---

## Network and edge

Meters sit on a site VLAN separated from office traffic, and reach the platform
through a gateway that translates their field protocol (Modbus RTU, M-Bus, LoRaWAN)
into `POST /api/v1/ingest/readings`.

**During a WAN outage** the gateway buffers and replays through the batch endpoint
on reconnect. Ingestion is idempotent, so replay is safe; consumption is derived from
TOTAL anchors rather than by counting readings, so even an outage spanning a period
boundary bills correctly. The 35-day backdating limit bounds how much history a
device can rewrite without an administrator's involvement.

This is what makes a cloud backend acceptable for a brief that says "on-premise" —
the resilience requirement is satisfied at the edge, where it belongs
([ADR-0012](adr/0012-aws-as-deployment-target.md)).
