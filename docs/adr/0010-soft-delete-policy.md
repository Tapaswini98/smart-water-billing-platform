# ADR-0010: Soft delete for master data, never for financial records

**Status:** Accepted · **Date:** 2026-09-19

## Context

"Soft deletes" is listed as appreciated functionality. Applied uniformly it is
harmful: hiding an issued invoice is not a deletion, it is a discrepancy.

## Decision

- **Soft-deleted** (nullable `deleted_at_utc` plus a global query filter):
  `users`, `meters`, `meter_api_keys`, `pricing_plans`.
- **Never deleted:** `meter_readings`, `invoices`, `invoice_line_items`,
  `payments`, `billing_runs`, `audit_log`, `pricing_plan_versions`.
- Unique indexes on soft-deletable tables are filtered on `deleted_at_utc IS NULL`,
  so a deleted email address or meter serial can be reused while live ones stay unique.
- A mistaken invoice is corrected by voiding and re-issuing, which leaves both
  documents visible.

## Consequences

- A deleted customer's invoices remain intact and attributable; the meter is
  retained and becomes unassigned rather than orphaned.
- Invoices and readings carry required foreign keys to soft-deletable rows. The
  filter is deliberately not propagated across those joins — an invoice must stay
  readable after its meter is retired — which is why
  `PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning` is
  suppressed explicitly, with this record as the reason.
