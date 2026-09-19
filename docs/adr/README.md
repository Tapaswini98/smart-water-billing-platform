# Architecture Decision Records

Short records of the decisions that shaped this codebase, written before the code
rather than reconstructed afterwards. Each one states the decision, the reason, and
— where it matters — the point at which the decision should be revisited.

A decision without a stated limit is a guess that got lucky. Where a record says
"revisit at N", that number is the trigger for the next conversation.

| # | Decision | Status |
|---|----------|--------|
| [0001](0001-dotnet-10-lts.md) | Target .NET 10 (LTS) | Accepted |
| [0002](0002-modular-monolith.md) | Modular monolith, three projects, no separate application layer | Accepted |
| [0003](0003-immutable-readings-and-consumption.md) | Readings are an immutable ledger; consumption is a pure function of it | Accepted |
| [0004](0004-versioned-pricing-and-invoice-snapshots.md) | Pricing plans are immutable and versioned; invoices denormalise their pricing | Accepted |
| [0005](0005-per-meter-ingestion-credentials.md) | Meters authenticate with a per-meter API key, not a user token | Accepted |
| [0006](0006-time-and-billing-periods.md) | Store UTC, bill on a configured local offset, no proration | Accepted |
| [0007](0007-idempotent-invoice-generation.md) | Invoice generation is idempotent and reports per-meter outcomes | Accepted |
| [0008](0008-postgresql-single-node.md) | Single-node PostgreSQL, migrations applied at startup | Accepted |
| [0009](0009-on-premise-with-cloud-backup.md) | On-premise billing path, cloud for backup and aggregation | Accepted |
| [0010](0010-soft-delete-policy.md) | Soft delete for master data, never for financial records | Accepted |
