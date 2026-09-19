# ADR-0004: Pricing plans are immutable and versioned; invoices denormalise their pricing

**Status:** Accepted · **Date:** 2026-09-19

## Context

Tariffs change. If an invoice referenced a mutable plan row, raising a rate in
October would silently restate every invoice ever issued — and the customer service
call that follows is unanswerable.

The brief says "fixed per unit pricing or slab based (like electricity meters)".
"Slab based" is genuinely ambiguous: some tariffs are telescopic (each band charges
only its own units), others charge the whole volume at the rate of the band reached.

## Decision

1. `pricing_plans` names a tariff and carries no rates. `pricing_plan_versions`
   holds the rates and is **immutable**, valid over `[effective_from, effective_to)`.
   Editing a plan closes the current version and inserts a new one.
2. An invoice stores the `pricing_plan_version_id` **and** every resulting line item
   (band, units, rate, amount) in full.
3. Slab semantics are configuration: `SlabMode.Progressive` (default) or
   `SlabMode.WholeVolumeAtReachedBand`.
4. Bands are **upper-inclusive**, `(from, to]`. A band written "0–10" covers
   consumption up to and including exactly 10 m³.
5. The version effective at **period end** prices the whole period. No proration.

## Consequences

- Any historical invoice is reproducible exactly, and is also readable without
  resolving a tariff that may have been superseded four times since.
- Rounding is fixed and stated: away from zero, 2 decimal places per line, volumes
  to 3 decimal places. Away-from-zero rather than banker's rounding because it
  matches utility billing convention and does not surprise anyone reading a bill.
- Upper-inclusive bands make the two slab modes agree at every boundary — there is
  a test that asserts exactly this, because a silent divergence there would
  mis-bill every customer sitting on a band edge.
- No proration is a simplification, documented rather than hidden. A mid-period
  tariff change is rare and, when it happens, is handled by ending the old plan at a
  period boundary. **Revisit if** regulated tariff changes must take effect mid-month.
