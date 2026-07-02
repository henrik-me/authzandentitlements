# CS09 — Adapter: Cedar (policy / ABAC)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate Cedar (in-process via MonoCloud Cedar for .NET) as a second policy engine to compare against OPA.

## Deliverables

- CedarProvider using MonoCloud Cedar for .NET (verify .NET 10 compat).
- Cedar schema + policies mirroring the OPA scenarios.
- Amazon Verified Permissions documented as the cloud option.

## Exit criteria

- Cedar answers the same policy scenarios as OPA for head-to-head comparison.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add MonoCloud Cedar | pending | — | |
| Author schema + policies | pending | — | |
| Implement adapter | pending | — | |
| Verify parity with OPA scenarios | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
