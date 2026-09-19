# ADR-0009: On-premise billing path, cloud for backup and aggregation

**Status:** Accepted · **Date:** 2026-09-19

## Context

The brief asks for an **on-premise** IoT solution, and separately asks for a
deployment plan naming a **cloud platform and services**. Taken literally these
conflict. Ignoring the conflict, or answering only one half, would be the easy
mistake.

## Decision

Split by criticality.

- **On-premise (mandatory):** meters, the API, PostgreSQL. Meters sit on the site
  LAN and ingestion must keep working through a WAN outage — a utility cannot stop
  measuring water because an internet link is down. Invoicing runs from local data.
- **Cloud (supporting):** encrypted offsite backups to S3 with lifecycle transition
  to Glacier; container images in ECR; CI in GitHub Actions.
- **Cloud (optional, multi-site):** a control plane aggregating several sites for
  central reporting and monitoring, fed asynchronously so a link failure degrades
  reporting and never billing.

## Consequences

- The billing-critical path has no cloud dependency, which is what "on-premise"
  has to mean if it means anything.
- Offsite backup is the one place where the cloud is strictly better than anything
  on site: it survives fire, theft and flood, which a NAS in the same building
  does not.
- Data leaving the premises is limited to backups (encrypted, customer-held key)
  and, if enabled, aggregate telemetry. That boundary is stated so it can be
  reviewed by whoever signs off on it.
