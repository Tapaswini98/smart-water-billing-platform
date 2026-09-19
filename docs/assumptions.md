# Assumptions

Reasonable assumptions are encouraged by the brief. These are the ones this
implementation makes. Each says what was assumed and what would change if it is wrong.

## Domain

1. **A meter belongs to at most one customer at a time; a customer may hold many
   meters.** Shared or split billing across occupants is out of scope.
   *If wrong:* an assignment table with effective dates replaces the direct foreign
   key, and invoices resolve the customer as of the period rather than at generation.

2. **A meter may be unassigned.** New builds and vacant premises are commissioned
   and ingest data before anyone is billed for them. Unassigned meters are reported
   as skipped by a billing run rather than silently ignored.

3. **`TOTAL` is the billing authority; `FLOW` never bills anything.** `FLOW` is an
   instantaneous sample, used for consistency checking and leak detection only.

4. **A decreasing `TOTAL` means a register reset or a meter replacement**, not
   corruption or theft. Consumption is summed across the discontinuity and the event
   is raised for an operator. *If wrong* (i.e. if tampering is expected), the reset
   event becomes an alert with a supply-cut-off workflow attached.

5. **Meters report honestly.** There is no tamper detection, no signature on the
   payload, and no reconciliation against a bulk inlet meter.

6. **One reading per meter per timestamp**, and the timestamp is the device's own.
   A meter that buffers through an outage must still report when each reading
   happened, so the server cannot assign receipt time.

## Billing

7. **Billing is monthly on the utility's calendar month**, boundaries computed in a
   configured fixed offset (default +05:30), half-open `[start, end)`.

8. **No proration.** The tariff version effective at period end prices the whole
   period. A mid-period tariff change is handled by aligning the change to a period
   boundary.

9. **One currency per tariff.** No conversion, no multi-currency invoice.

10. **Tax is a single percentage applied to the standing charge plus usage.** Real
    tax regimes are more structured; this is a placeholder with the right shape.

11. **A meter with no readings in a period is billed the standing charge with zero
    consumption, flagged `no_data`** — not skipped. A gap in the invoice series is
    how revenue goes missing unnoticed.

12. **Invoices are issued per meter, not per customer.** A customer with three
    meters receives three invoices. *If wrong:* a consolidated statement groups
    them; the per-meter invoice stays as the underlying document.

## Security and operations

13. **The API is reached over the site LAN or a VPN.** TLS termination is the
    responsibility of the reverse proxy in front of it, not of the application.

14. **Per-meter API keys are acceptable for this exercise; mutual TLS is the
    production path.** See [ADR-0005](adr/0005-per-meter-ingestion-credentials.md).

15. **A single API instance per site.** Migrations are applied at startup, which is
    a race with more than one replica. The threshold is stated in
    [ADR-0008](adr/0008-postgresql-single-node.md).

16. **Backups are the operator's responsibility to offsite.** The platform produces
    and verifies them; shipping them off the premises is a configured destination.

## Scope

17. **No user interface is required.** The brief specifies behaviours, not screens;
    "a customer can view invoices" is satisfied by an authenticated endpoint. A
    minimal static demo page is included for convenience, not as a product.

18. **Payments are demonstrated against a mock provider.** No real gateway
    credentials exist in this repository, and none should.
