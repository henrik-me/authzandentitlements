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

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 3de3961da0b7 | 2026-07-02T19:47:54Z | Go | Sound as-is: CS05 dependency, Rego maker-checker, SoD, threshold, and condition scenarios are coherent. |

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
