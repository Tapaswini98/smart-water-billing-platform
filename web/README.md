# Web client

React 19 + TypeScript + Vite, served by nginx. Design decisions are recorded in
[ADR-0011](../docs/adr/0011-frontend-architecture.md).

## Run it

With the whole stack (recommended — this is what a reviewer runs):

```bash
docker compose up --build      # web on :3000, API on :8080
```

Against a locally running API:

```bash
cd web
npm install
npm run dev                    # :5173, proxies /api to localhost:8080
```

## The generated API client

`src/shared/api/schema.d.ts` is **generated and committed** — never edited by hand.

The backend emits `openapi/WaterBilling.Api.json` on every build, so regenerating
needs no running server and no database:

```bash
dotnet build src/WaterBilling.Api    # from the repo root — writes openapi/
cd web && npm run generate:api
```

CI regenerates and fails the build if the result differs from what is committed, so
a backend DTO change that nobody propagated is caught on the pull request rather
than in the browser.

This is also why the API sets `JsonNumberHandling.Strict`: ASP.NET's web defaults
accept numbers written as strings, which made the schema describe every numeric
field as `number | string`. Generating the client surfaced it; see ADR-0011.

## Scripts

| Script | Purpose |
|---|---|
| `npm run dev` | Dev server on :5173 with `/api` proxied |
| `npm run build` | Type-check then production build |
| `npm run typecheck` | Type-check only |
| `npm run lint` | ESLint, type-aware rules |
| `npm run generate:api` | Regenerate the client from `openapi/` |
| `npm run format` | Prettier |

## Layout

Feature-sliced, so everything about invoices lives in one directory rather than
being scattered across `components/`, `hooks/` and `services/`.

```
src/
  app/        Router, providers, layout shell
  features/
    auth/     Context, guards, login
    meters/   Readings view and queries
    invoices/ Invoice history and detail
    pricing/  Admin tariff editor
  shared/
    api/      Generated schema, typed client, ProblemDetails mapping
    ui/       Button, Card, Badge, EmptyState, ProblemAlert, ErrorBoundary
    lib/      Volume, currency and date formatting
```

## Known limitations

- **The token lives in `sessionStorage`**, readable by any script on the origin. An
  httpOnly cookie with CSRF protection is the stronger design and the stated
  production path; `sessionStorage` bounds exposure to the tab rather than the
  browser profile.
- **Route guards are UX, not security.** They decide what the UI offers; every
  protected endpoint re-checks authorization server-side, and that is the check
  that matters.
- **No component tests yet.** The pure logic worth testing (consumption, pricing)
  lives in the .NET domain layer and is covered there.

## Pinned TypeScript 5.9

TypeScript 7 is current, but `typescript-eslint` supports `<6.1.0` and
`openapi-typescript` peer-requires `^5.x`. 5.9 is the newest version the whole
toolchain can actually parse — the same reasoning as choosing an LTS runtime:
newest is only useful if your tools support it.
