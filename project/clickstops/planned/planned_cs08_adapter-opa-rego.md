# CS08 — Adapter: OPA / Rego (policy / ABAC)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate OPA/Rego for maker-checker, segregation-of-duties, and conditional policy scenarios.

## Deliverables

- OPA Aspire container (REST decision API).
- Rego policies: maker-checker (creator != approver), four-eyes/dual-auth thresholds, SoD, conditions on amount/time/geo/risk/tier.
- OpaProvider; WASM in-process noted as alternative.

## Exit criteria

- OPA answers the policy scenarios; policies unit-tested with `opa test`.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add OPA container | pending | — | |
| Author Rego + tests | pending | — | |
| Implement adapter | pending | — | |
| Map scenarios | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
