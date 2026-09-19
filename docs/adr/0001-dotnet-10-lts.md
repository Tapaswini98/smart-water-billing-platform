# ADR-0001: Target .NET 10 (LTS)

**Status:** Accepted · **Date:** 2026-09-19

## Context

The brief requires .NET Core 6 or above. At the time of writing, .NET 8 and .NET 9
both reach end of support on 10 November 2026 — seven weeks away. .NET 10 is the
current LTS release, supported until November 2028.

## Decision

Target `net10.0`, pinned via `global.json` with `rollForward: latestFeature`.

## Consequences

- An on-premise deployment gets security patches for its first two years in service
  without a runtime migration. For software installed on a customer's own hardware,
  where an upgrade means a site visit, that is the difference between a routine
  patch and a project.
- Contributors need the .NET 10 SDK. `global.json` fails fast with a clear message
  rather than silently building against a different runtime.
- The Docker build is unaffected — the SDK version is pinned in the image tag, so
  `docker compose up` works with no local SDK at all.
