# Backup and disaster recovery

> **Status:** _(pending)_ — method chosen, schedule and drill results to follow.

## Method

_(pending)_ — `pg_dump` custom-format logical backups for portability, plus WAL
archiving for point-in-time recovery. Rationale, and why not only one of the two.

## Schedule and retention

_(pending)_ — daily full, continuous WAL, retention tiers, and the offsite copy.

## RPO and RTO

_(pending)_ — the numbers this design actually achieves, not the ones we would like.

## Restore procedure

_(pending)_ — step-by-step, written so that someone who did not build the system
can follow it under pressure.

## Verification

A backup that has never been restored is a hypothesis. `scripts/verify-restore.sh`
restores the committed sample dump into a clean PostgreSQL container, asserts row
counts, and recomputes an invoice total to confirm the data came back meaning the
same thing. It runs in CI.

## Disaster recovery scenarios

_(pending)_ — disk failure, site loss, accidental deletion, and ransomware, each
with the actual sequence of steps.

## The sample backup in this repository

`backups/` contains one dump of the demo database: schema plus **synthetic** data
only. It contains no real customer data and no credentials beyond the well-known
Development seed accounts. See [`backups/README.md`](../backups/README.md).
