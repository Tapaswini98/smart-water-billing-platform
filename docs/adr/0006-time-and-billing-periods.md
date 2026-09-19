# ADR-0006: Store UTC, bill on a configured local offset, no proration

**Status:** Accepted · **Date:** 2026-09-19

## Context

"Previous month" is a local-calendar idea, but storing local times makes every
comparison ambiguous. Meters may also report in their own local time, or with a
drifting clock.

## Decision

- Every timestamp is stored as `timestamptz` in UTC. The EF model converts on the
  way in, so "always UTC" is a property of the model rather than a rule each call
  site has to remember.
- Billing period boundaries are computed in a configured fixed offset
  (`Billing:BillingOffsetHours`, default +05:30) so "previous month" means the
  utility's calendar month, not UTC's.
- Periods are half-open `[start, end)`, so consecutive months tile exactly.
- A fixed offset, not an IANA zone: the target deployments do not observe daylight
  saving, and a fixed offset removes the DST-transition edge cases entirely.

## Consequences

- No 23-hour or 25-hour billing months, and no reading that lands in two invoices.
- **Revisit if** the platform is deployed where DST applies; the change is to store
  an IANA zone id and resolve boundaries through `TimeZoneInfo`, which is contained
  to `DateRange.PreviousMonth`.
