# Deployment strategy

> **Status:** _(pending)_

## What gets deployed

_(pending)_ — the API container, PostgreSQL, the reverse proxy, and the backup agent.

## Target platform

_(pending)_ — on-premise for the billing path, AWS for backup and image registry.
The reasoning for the split is in
[ADR-0009](adr/0009-on-premise-with-cloud-backup.md).

### Cloud services used

_(pending)_ — S3 + Glacier for offsite backup, ECR for images, and what is
deliberately *not* used.

### Docker and Kubernetes

_(pending)_ — Docker Compose on site; the case against Kubernetes for a
single-node on-premise deployment, and the point at which that changes.

## Environments

_(pending)_ — local, staging, site.

## CI/CD

_(pending)_ — build, test, restore-verification, image publish, and how a site
actually receives an update over a possibly-intermittent link.

## Rollback

_(pending)_ — including the one that matters: how to roll back an application
version when a migration has already run.

## Commissioning a new site

_(pending)_ — the runbook for an installer.
