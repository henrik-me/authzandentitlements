# CS07 — Adapter: OpenFGA (ReBAC / Zanzibar)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate OpenFGA for relationship-based fintech scenarios.

## Deliverables

- OpenFGA Aspire container.
- ReBAC model + tuples: account ownership, relationship-manager->customer, branch/region hierarchy, delegation.
- OpenFgaProvider (OpenFga.Sdk); forward + reverse-index checks.

## Exit criteria

- OpenFGA answers ReBAC scenarios including "who can view account X" / "what can user Y access".

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add OpenFGA container | pending | — | |
| Author model + tuples | pending | — | |
| Implement adapter | pending | — | |
| Verify reverse-index queries | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
