# ADR-0007: Invoice generation is idempotent and reports per-meter outcomes

**Status:** Accepted · **Date:** 2026-09-19

## Context

"Generate invoices for all meters for the previous month" is one button that an
admin will press twice — because the first press appeared to hang, because the
network blipped, or because they were not sure it worked. It also runs across an
estate where some meters will be unassigned, unconfigured or silent.

## Decision

1. A unique index on `(meter_id, period_start)` makes double-billing impossible at
   the storage layer, not merely unlikely at the service layer.
2. Each run creates a `billing_run` with a `billing_run_item` per meter recording
   the outcome: `Generated`, `AlreadyBilled`, `GeneratedWithoutData`, `Skipped` or
   `Failed`, each with an operator-readable message.
3. A meter with no readings in the period is billed a **zero-consumption invoice at
   the standing charge**, flagged `no_data` — it is not skipped.
4. One meter failing does not abort the run.
5. Runs can be executed as a dry run: everything computed and reported, nothing saved.

## Consequences

- Re-running a period is safe and says so, rather than being safe by accident.
- A gap in the invoice series is how revenue quietly goes missing; issuing a flagged
  zero invoice makes a silent meter visible on the next bill instead of in an audit.
- The run's summary is the answer to "did billing work this month?" without anyone
  having to query the database.
