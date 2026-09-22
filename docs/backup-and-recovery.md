# Backup and disaster recovery

A backup that has never been restored is a hypothesis. This document states the
method, the schedule and the recovery procedures — and the repository contains a
real dump plus a script that restores it and checks the result, so the claim is
tested rather than asserted.

## Method

Two mechanisms, because they fail differently and cover different losses.

### 1. Logical backups — `pg_dump`, custom format

```bash
scripts/backup.sh                      # auto-detects container or local PostgreSQL
scripts/backup.sh --label pre-upgrade  # tag a backup with why it was taken
```

Custom format (`-Fc`) rather than plain SQL:

- Compressed — the demo database is 44,040 readings and dumps to 1.3 MB.
- Selectively restorable, so a single table can be recovered without the rest.
- Restorable in parallel by `pg_restore`.
- Portable across PostgreSQL versions and across machines.
- **Unmistakably a backup**, not a migration script that happens to contain INSERTs.

Every backup writes three files: the dump, a `.sha256`, and a `.manifest.md`
recording tool and server versions, the exact command, the size, the checksum and
**per-table row counts at capture**. The manifest is what makes a later restore
verifiable against what was actually captured.

### 2. WAL archiving — continuous, for point-in-time recovery

`pg_dump` alone means losing everything since the last dump. WAL archiving closes
that gap: every committed transaction is streamed to the archive, so recovery can
target any moment.

```
wal_level = replica
archive_mode = on
archive_command = 'test ! -f /backups/wal/%f && cp %p /backups/wal/%f'
archive_timeout = 300      # force a segment every 5 minutes even when idle
```

Used together: the nightly dump is the base, WAL is everything since.

## Schedule and retention

| What | When | Retained | Where |
|---|---|---|---|
| RDS automated snapshot | Daily, during the backup window | 35 days | Managed by RDS, cross-region copy enabled |
| Full logical backup (`pg_dump`) | Daily, 02:00 local | 30 days | S3 Standard |
| Full logical backup | Weekly, Sunday | 12 weeks | S3 Standard → Standard-IA |
| Full logical backup | Monthly, 1st | 7 years | S3 Glacier Deep Archive |
| Transaction logs | Continuous | 35 days | RDS, enabling PITR to any second |
| Pre-deployment backup | Before every release | 90 days | S3 Standard |

**Two mechanisms on purpose.** RDS automated snapshots and PITR are the fast path —
restore to any second in the last 35 days with a console action. The `pg_dump`
logical backups are the *portable* path: they restore onto any PostgreSQL anywhere,
including a developer laptop or an on-premise box, and they are what protects
against losing the AWS account itself. A snapshot is useless if you cannot reach the
account that holds it.

Seven years on monthlies is deliberate: billing records are financial records, and
the retention is set by the customer's tax and utility-regulator obligations rather
than by engineering preference.

**Scheduling on AWS** — EventBridge Scheduler triggers a one-off ECS task running
the same image with `scripts/backup.sh`. A CloudWatch alarm fires on task failure,
because a backup schedule nobody is alerted about is a schedule that silently
stopped months ago.

**Scheduling on-premise** — a `systemd` timer rather than cron, because it survives a
missed window (`Persistent=true`) and its failures are visible in
`systemctl status`:

```ini
# /etc/systemd/system/waterbilling-backup.timer
[Unit]
Description=Nightly water billing database backup

[Timer]
OnCalendar=*-*-* 02:00:00
Persistent=true
RandomizedDelaySec=300

[Install]
WantedBy=timers.target
```

**Offsite**, encrypted client-side so the storage provider never holds readable
customer data:

```bash
restic -r s3:s3.amazonaws.com/waterbilling-backups backup /backups
restic -r s3:s3.amazonaws.com/waterbilling-backups forget \
  --keep-daily 30 --keep-weekly 12 --keep-monthly 84 --prune
```

## RPO and RTO

The numbers this design actually achieves, not the ones we would like:

**On AWS:**

| Scenario | RPO (data lost) | RTO (time to service) |
|---|---|---|
| AZ failure | 0 | 1–2 min, RDS Multi-AZ failover is automatic |
| Fargate task failure | 0 | Seconds — ECS replaces it behind the load balancer |
| Database corruption or bad migration | ≤ 5 min via PITR | 30–60 min |
| Accidental deletion, caught quickly | ≤ 5 min via PITR | 30–60 min |
| Region failure | ≤ 24 h (cross-region snapshot copy) | 2–4 h to rebuild in another region |
| AWS account loss or compromise | ≤ 24 h | 4–8 h, restoring the logical dump elsewhere |

**On-premise (single box):**

| Scenario | RPO | RTO |
|---|---|---|
| Disk failure, RAID 1 intact | 0 | ~0 — degraded but serving |
| Both disks fail | ≤ 24 h | 4–8 h |
| Total site loss | ≤ 24 h | 4–8 h, dominated by hardware procurement |

**The honest caveat**, which applies to both: an outage long enough to matter has an
RTO measured in hours, because it includes rebuilding infrastructure. Ingestion is
unavailable in the meantime — but meters keep counting and their gateways buffer.
Readings replay on reconnect, so the *measurement* record survives even though the
service did not. Consumption is derived from TOTAL anchors rather than by counting
readings, so a period spanning the outage still bills correctly. That property is
what makes an hours-long RTO tolerable for a monthly billing cycle, and it is the
same property ADR-0012 relies on.

## Restore procedures

Written so that someone who did not build the system can follow them under pressure.

### Verify a backup without touching anything

```bash
scripts/verify-restore.sh                     # Docker: throwaway container
scripts/verify-restore.sh --local             # no Docker: throwaway database
```

Creates a disposable target, restores, asserts structure and row counts, recomputes
every invoice total from its own line items, and drops the target. It never touches
a running database. A recorded run is committed at
[`backups/restore-drill.log`](../backups/restore-drill.log).

### Restore the latest backup (destructive)

```bash
scripts/backup.sh --label pre-restore          # 1. back up what is there NOW
docker compose stop api                        # 2. stop writers
scripts/restore.sh --file backups/<file>.dump --yes
docker compose start api                       # 3. resume
curl localhost:8080/health/ready               # 4. confirm
```

Step 1 is not optional. The most common way a restore makes things worse is
discovering, afterwards, that the current state contained something the backup did not.

### Point-in-time recovery

```bash
systemctl stop waterbilling-api
pg_ctl stop -D /var/lib/postgresql/17/main
mv /var/lib/postgresql/17/main /var/lib/postgresql/17/main.broken

pg_basebackup -D /var/lib/postgresql/17/main -Fp -Xs -P

cat >> /var/lib/postgresql/17/main/postgresql.conf <<'EOF'
restore_command = 'cp /backups/wal/%f %p'
recovery_target_time = '2026-09-20 14:30:00+05:30'
recovery_target_action = 'promote'
EOF
touch /var/lib/postgresql/17/main/recovery.signal

pg_ctl start -D /var/lib/postgresql/17/main
```

Recover to **just before** the damaging event, then verify before promoting. Keep
`main.broken` until the recovered database has been confirmed good.

## Disaster recovery scenarios

| Scenario | Response |
|---|---|
| **Single disk fails** | RAID 1 continues degraded. Replace the disk, rebuild, no downtime. Monitoring must alert on degraded arrays — a mirror nobody noticed has failed is not a mirror. |
| **Both disks fail** | Rebuild the host, restore the latest dump, replay WAL to the failure point. RTO 4–8 h. |
| **Database corruption** | Stop writers immediately. Do **not** restart the API — a running application will write more bad data. PITR to just before corruption. |
| **Accidental `DELETE` / bad migration** | PITR to just before it. If caught within minutes, RPO is ≤ 5 min. |
| **AZ failure (AWS)** | RDS fails over to the standby automatically; ECS reschedules tasks in the surviving AZ. No action required. |
| **Region failure (AWS)** | Rebuild from Terraform in the secondary region, restore the cross-region snapshot copy. |
| **Site loss (on-premise: fire, flood, theft)** | Restore from S3 onto replacement hardware. Meter gateways buffer and replay, so measurement history survives the gap. |
| **Ransomware** | Offsite copies are immutable: S3 Object Lock in compliance mode, so nothing — including a compromised admin credential — can delete them inside the retention window. This is the specific reason offsite is object storage with a lock rather than a mounted NAS share, which encrypts along with everything else. |
| **Backup itself is corrupt** | Caught before it matters: `verify-restore.sh` runs in CI on every push, and nightly against the previous night's dump. |

## Verification, in practice

| Check | Frequency | Mechanism |
|---|---|---|
| Checksum | Every backup | `sha256sum`, written alongside the dump |
| Structural + semantic restore | Every push | `verify-restore.sh` in GitHub Actions |
| Full restore drill | Nightly | `verify-restore.sh` against the latest dump |
| Documented recovery rehearsal | Quarterly | An engineer who did not write this follows the procedure above, timed, on a clean machine |

The quarterly rehearsal is the one that actually matters. Automated verification
proves the *data* restores; only a human following the written procedure proves the
*procedure* works — and that is what fails at 3 a.m.

## The sample backup in this repository

`backups/sample-<timestamp>.dump` is a genuine `pg_dump` custom-format backup taken
from a running instance after exercising the application: three months of readings
ingested, a billing run executed, invoices issued, a payment recorded.

Row counts at capture are in the manifest. It contains **synthetic data only** — no
real customer data, and no credentials beyond the well-known Development seed
accounts. See [`backups/README.md`](../backups/README.md) for why that boundary
exists in a public repository.
