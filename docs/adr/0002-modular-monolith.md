# ADR-0002: Modular monolith, three projects, no separate application layer

**Status:** Accepted · **Date:** 2026-09-19

## Context

The system has one deployable concern: accept readings, price them, issue invoices.
It will run on a single box inside a customer's network, serving at most a few
hundred meters. Two structural choices were available — microservices per concern,
or a monolith — and within the monolith, whether to add a separate application
layer between the API and the domain.

## Decision

A single deployable API. Three projects:

- **`WaterBilling.Domain`** — entities and the two pure calculators. Zero package
  references, enforced by the project file.
- **`WaterBilling.Infrastructure`** — EF Core, PostgreSQL, hashing, tokens, seeding.
- **`WaterBilling.Api`** — vertical feature slices: endpoint, validator, DTOs and
  handler colocated per feature.

No application/use-case layer, and no repository interfaces over `DbContext`.

## Consequences

- `DbContext` is already a unit of work and its `DbSet<T>` is already a repository.
  Wrapping it would add a layer whose only job is to forward calls, and would make
  the query-shaping this system needs (projections, index-aligned ordering) harder
  rather than easier.
- Business rules that must be right are in `Domain`, testable with no host and no
  database. Everything else is plumbing.
- **Revisit when:** a second deployable appears (a customer portal with its own
  release cadence, or a reporting service), or when more than one team ships into
  this repository. Neither is true today, and building for either now would be
  paying interest on a loan not yet taken.
