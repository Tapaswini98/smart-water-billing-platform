# ADR-0011: React SPA with a generated API client, no client-side state library

**Status:** Accepted · **Date:** 2026-09-19

## Context

A web UI is required. The obvious failure modes for a frontend attached to a typed
backend are drift between the two, and reaching for infrastructure the app does not
need.

## Decision

**React 19 + TypeScript + Vite**, served by nginx as static files.

**The API client is generated from the OpenAPI document, not hand-written.**
`Microsoft.Extensions.ApiDescription.Server` writes `openapi/WaterBilling.Api.json`
on every backend build — without a database, because the startup work that needs one
moved into a hosted service that build-time tooling never starts.
`openapi-typescript` turns that into `web/src/shared/api/schema.d.ts`, and
`openapi-fetch` gives a typed client over it. CI regenerates and fails if the result
differs from what is committed.

**No Redux, MobX or Zustand.** Meters, readings, invoices and tariffs are all server
state: TanStack Query owns them, with cache keys, staleness and retry policy. The
only genuinely client-owned state is the session, which is a React context. Form
state is `useState`.

**Feature-sliced folders** (`features/invoices/…`), not type-sliced
(`components/`, `hooks/`, `services/`).

**The API base path is the relative `/api`.** nginx proxies it to the API container
in production; Vite's dev server proxies it in development.

## Consequences

- A changed DTO becomes a **TypeScript compile error**, not a runtime `undefined`.
  The contract cannot drift silently in either direction.
- Setting the client's number handling to `Strict` on the backend was a direct
  consequence: ASP.NET's web JSON defaults accept numbers written as strings, so the
  schema exporter described every numeric field as `number | string`. That union
  propagated into the generated client and forced callers to narrow a type the API
  never returns. Strict mode produced a clean contract — a concrete example of
  generating the client surfacing a real API-design defect.
- Declining a state library is a decision, not an omission. A cache of HTTP
  responses in a global store means writing invalidation by hand and getting it
  wrong; **revisit if** genuine cross-screen client state appears, such as an
  offline queue of readings captured during a site visit.
- No env-var API URL means one image is correct in every environment. Vite inlines
  `import.meta.env` at build time, so an absolute URL would bake one environment's
  host into the bundle.
- **No CORS configuration exists anywhere in this project**, because the browser
  only ever talks to one origin.
- Cost: an SPA needs JavaScript enabled and is worse for first paint than server
  rendering. Both are acceptable for an authenticated internal tool on a LAN.
- The token is held in `sessionStorage`, which is readable by any script on the
  origin. An httpOnly cookie with CSRF protection is the stronger design and is the
  stated production path; `sessionStorage` at least bounds exposure to the tab.
