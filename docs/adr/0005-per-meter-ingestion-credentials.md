# ADR-0005: Meters authenticate with a per-meter API key, not a user token

**Status:** Accepted · **Date:** 2026-09-19

## Context

A water meter cannot complete an interactive login, cannot store a refresh token
safely, and may be physically accessible to anyone who can reach the riser cupboard.
It still has to authenticate — an unauthenticated ingestion endpoint lets anyone on
the LAN write another customer's bill.

## Decision

Each meter is issued a 256-bit random key, presented on every request as
`X-Meter-Key`. Only a SHA-256 hash is stored; the plaintext is displayed once, at
creation. Keys can be revoked or expired individually. Ingestion is rate-limited per
key prefix. This is a distinct ASP.NET authentication scheme (`MeterKey`), separate
from the JWT scheme used by humans.

## Consequences

- Compromise of one meter's key exposes one meter. It cannot read invoices, cannot
  read other meters, and cannot authenticate to any user-facing endpoint — that is
  enforced by the authorization policy, not by remembering to check.
- SHA-256 rather than a slow KDF is correct here: the secret is 256 bits of CSPRNG
  output, so there is no low-entropy password to protect against offline guessing,
  and a slow hash on every reading would be cost with no benefit. The lookup is
  narrowed by an indexed 12-character prefix, then compared in constant time.
- Replay of a captured reading is harmless, because ingestion is idempotent
  (ADR-0003) — the replay is recognised and discarded.
- **Not yet done, and the production path:** mutual TLS, with the meter's client
  certificate issued by the utility's own CA. That moves the secret into a hardware
  element and makes revocation a CRL entry. It needs a certificate lifecycle the
  installer can operate, which is a larger piece of work than this exercise.
