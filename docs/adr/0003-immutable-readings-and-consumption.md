# ADR-0003: Readings are an immutable ledger; consumption is a pure function of it

**Status:** Accepted · **Date:** 2026-09-19

## Context

Meters report `TOTAL` (cumulative m³) and `FLOW` (m³/hr). The obvious implementation
— keep a running consumption counter per meter and add each delta to it — breaks on
every real-world condition: duplicate deliveries, out-of-order arrival, meter
replacement, and any correction to historical data.

## Decision

Readings are inserted and never updated or deleted. Consumption for a period is
derived on demand:

```
consumption(meter, start, end)
    = TOTAL(latest reading ≤ end) − TOTAL(latest reading ≤ start)
```

computed by walking the series pairwise so a register reset can be summed across
rather than producing a negative figure.

### Ingestion rules

| Condition | Behaviour | Why |
|---|---|---|
| Duplicate `(meter_id, reading_at)` | `200` with `status: "duplicate_ignored"` | A retrying device must be safe. `409` would make retry logic treat a successful outcome as an error. Enforced by a unique index, not by a check-then-insert race. |
| Out-of-order arrival | Accepted, flagged `OutOfOrderArrival` | Consumption is computed from the time-ordered series, so arrival order cannot change any invoice. |
| `TOTAL` decreased | Accepted, flagged `TotalDecreased`, `MeterResetEvent` recorded | A reset or replacement, not corruption. Billing sums across the discontinuity; an operator resolves the physical cause. |
| Timestamp > 10 min in the future | Rejected | A drifting device clock would otherwise bill a future period. |
| Timestamp > 35 days old | Rejected | A replayed buffer would silently alter already-issued invoices. Historical loads go through the admin CSV path. |
| `FLOW` disagrees with `ΔTOTAL` | Accepted, flagged `FlowTotalMismatch` | `FLOW` never bills anything; it is a free consistency check on `TOTAL`. |
| Non-zero `FLOW` sustained 24 h | Flagged `ContinuousFlow` | No household has zero idle time. Candidate leak, reported rather than billed silently. |

## Consequences

- Periods tile exactly: no gap at midnight on the 1st, no reading counted twice.
- A late-arriving reading self-corrects the next time consumption is computed.
  There is no stored counter to repair.
- Consumption is a pure function over a list of structs, so every rule above has a
  unit test that runs without a database.
- Cost: consumption is computed per query rather than read from a column. At the
  design volume this is two index seeks. **Revisit at ~50 M readings**, where a
  monthly rollup table becomes worth its maintenance cost.
