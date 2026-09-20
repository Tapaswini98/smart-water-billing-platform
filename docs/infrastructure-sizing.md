# Infrastructure sizing

The point of this document is not to list three server sizes. It is to show the
arithmetic that makes the choice obvious, and then to decline to over-engineer.

## The workload, in numbers

Assume the design cadence of **one reading per meter every 15 minutes** (96/day).
Row size is measured from the live schema: `meter_readings` is 12 columns of fixed
types plus indexes, ~150 bytes all in.

| | Small (≤ 50 meters) | Medium (≤ 300) | Large (300+, modelled at 2,000) |
|---|---|---|---|
| Readings / day | 4,800 | 28,800 | 192,000 |
| Readings / year | 1.75 M | 10.5 M | 70 M |
| Average write rate | 0.06 /s | **0.33 /s** | 2.2 /s |
| Burst (all meters reporting at once) | 50 | 300 | 2,000 |
| Data growth / year | ~0.3 GB | **~1.6 GB** | ~10 GB |
| 5-year retention | ~1.5 GB | ~8 GB | ~50 GB |

Measured against the seeded demo database, which holds 44,041 readings: the
compressed backup is 1.3 MB, so ~31 bytes/row compressed and ~150 bytes/row on disk
with indexes. The projections above are that measurement multiplied out, not a guess.

Invoices are negligible by comparison: 300 meters × 12 months = 3,600 rows/year,
plus four to six line items each.

## What that arithmetic says

**0.33 writes per second.** A single PostgreSQL instance on modest hardware handles
thousands. The entire five-year medium-tier dataset fits in RAM on a 16 GB box, so
the working set is never on disk and read latency is a non-issue.

This is why the architecture has **no message queue, no stream processor, no
sharding and no read replica**. They would not make the system faster; they would
add components that must be understood and maintained by whoever is on site when
something breaks at 2 a.m. The thresholds at which that judgement changes are in
[ADR-0008](adr/0008-postgresql-single-node.md).

## Small scale — up to 50 meters

A single small-form-factor PC in the plant room. Roughly 0.3 GB/year, ~5,000
writes/day.

**Hardware**

| Component | Recommendation | Why |
|---|---|---|
| Compute | Intel N100 / Ryzen embedded mini-PC, 4 cores | Fanless, ~10 W, no moving parts in a dusty plant room |
| RAM | 8 GB | Whole dataset in page cache with room for the .NET heap |
| Storage | 256 GB NVMe SSD | ~4 GB of data over 10 years; the SSD is for IOPS and reliability, not capacity |
| Backup target | 1 TB USB SSD or NAS share | Local restore path that does not need the WAN |
| Network | Gigabit ethernet to the meter VLAN | |
| Power | 650 VA line-interactive UPS | ~20 min hold-up: enough to ride out a brownout and shut down cleanly |

**Software:** Debian 12 or Ubuntu 24.04 LTS · Docker Engine + Compose · PostgreSQL
17 and the API as containers · Caddy or nginx terminating TLS · `systemd` timer for
`scripts/backup.sh` · restic or rclone to object storage.

**Cost indication:** ~£450 hardware, ~£5/month offsite storage.

## Medium scale — up to 300 meters

The reference deployment. ~1.6 GB/year, ~29,000 writes/day, 0.33 writes/second.

**Hardware**

| Component | Recommendation | Why |
|---|---|---|
| Compute | 1U rack server or tower, 8 cores (Xeon E-2400 / Ryzen 7) | Headroom for a billing run to saturate a core without affecting ingestion |
| RAM | 32 GB | 16 GB would suffice; 32 GB means the five-year dataset never leaves cache |
| Storage | 2 × 1 TB NVMe SSD, **RAID 1** | Mirroring is the cheapest possible protection against the single most likely hardware failure |
| Backup target | NAS with its own redundancy, plus offsite | |
| Network | Dual gigabit, bonded | Survives one cable or switch port failing |
| Power | 1500 VA UPS with managed shutdown | |

**Software:** same stack as small scale, plus **WAL archiving enabled** for
point-in-time recovery, Prometheus + Grafana (`postgres_exporter`, and the API's
`/health/ready`), and log shipping to Loki or the customer's SIEM.

**PostgreSQL tuning** (defaults are sized for a far smaller machine):

```
shared_buffers = 8GB              # ~25% of RAM
effective_cache_size = 24GB       # ~75% of RAM: planner hint, not an allocation
work_mem = 32MB
maintenance_work_mem = 1GB
wal_level = replica               # required for WAL archiving
archive_mode = on
random_page_cost = 1.1            # SSD, not spinning rust
checkpoint_completion_target = 0.9
```

**Cost indication:** ~£2,500 hardware, ~£15/month offsite storage.

## Large scale — 300+ meters

Modelled at 2,000 meters: ~10 GB/year, 2.2 writes/second average.

Still comfortably a single-node workload. What changes is not throughput but
**availability**: at this size an outage affects enough customers that a planned
recovery path is required rather than optional.

**Hardware**

| Component | Recommendation | Why |
|---|---|---|
| Compute | 2 × 1U servers, 16 cores each | Primary and streaming replica |
| RAM | 64 GB each | |
| Storage | 4 × 2 TB NVMe, RAID 10 | Capacity plus write throughput |
| Network | Dual 10 GbE, redundant switches | |
| Power | Redundant PSUs, dual UPS feeds | |

**Software changes at this tier:**

- **Streaming replication** to the second node, with `pgBackRest` for backup and PITR.
- **Partition `meter_readings` by month** once it passes ~50 M rows (about 5 years at
  2,000 meters). Detaching an old partition is then a metadata operation rather than
  a `DELETE` that has to be vacuumed.
- **Two API containers** behind the reverse proxy. That crosses the threshold in
  ADR-0008, so migrations move from startup into a separate pipeline step.
- Connection pooling via **PgBouncer** in transaction mode.

**Cost indication:** ~£9,000 hardware, ~£60/month offsite storage.

## What is deliberately absent, at every tier

| Not used | Why |
|---|---|
| Kafka / RabbitMQ | 0.33 writes/second. The queue would carry less traffic than its own heartbeat. |
| Kubernetes | One or two nodes in a plant room. K8s adds a control plane to operate, for orchestration nobody needs. See [ADR-0009](adr/0009-on-premise-with-cloud-backup.md). |
| NoSQL / time-series DB | The brief requires a relational database, and the access pattern — "latest reading at or before X" — is a two-column btree seek. |
| Sharding | A single node absorbs 2,000 meters with three orders of magnitude to spare. |
| Redis | Nothing here is expensive enough to cache. Consumption is two index seeks. |

Each of these becomes correct at some volume. None of them is correct at these volumes.

## Edge and network

Meters reach the platform over the site LAN, on a VLAN separated from office traffic.
Where meters speak a field protocol rather than HTTP (Modbus RTU, M-Bus, LoRaWAN), a
gateway translates and posts to `/api/v1/ingest/readings` with the meter's own key.

**During a WAN outage everything that matters keeps working**: meters are local,
ingestion is local, the database is local, and invoicing runs from local data. What
degrades is offsite backup and any cloud reporting — by design (ADR-0009).

Gateways buffer readings when the LAN itself is interrupted and replay them through
`/api/v1/ingest/readings/batch` on reconnect. The 35-day backdating limit bounds how
much history a device can rewrite without an administrator's involvement.
