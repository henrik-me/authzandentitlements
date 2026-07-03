# CS08 — Adapter: OPA / Rego (policy / ABAC)

**Status:** active
**Owner:** yoga-ae
**Branch:** cs08/content
**Started:** 2026-07-03
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
| Author Rego decision policy (mirror reference rules → 22-scenario parity) | pending | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=pending \| learnings=0 |
| Author `opa test` unit tests for the decision policy | pending | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=pending \| learnings=0 |
| ABAC conditions showcase (amount/time/geo/risk/tier) + `opa test` | pending | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=pending \| learnings=0 |
| Add OPA Aspire container (WithExplicitStart, pinned tag, bind-mount policy) + inject endpoint into authz-pdp | pending | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=pending \| learnings=0 |
| Implement `OpaDecisionProvider` (Name="opa") + DI registration + `Opa` config | pending | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=pending \| learnings=0 |
| OpaDecisionProvider unit tests (request shaping, response mapping, fail-closed) | pending | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=pending \| learnings=0 |
| Adapter doc `docs/authz/opa-adapter.md` | pending | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=pending \| learnings=0 |
| Close-out: docs + restart state | pending | yoga-ae | Update WORKBOARD.md, CONTEXT.md; adapter/policy docs ship in the content PR |
| Close-out: learnings + follow-ups | pending | yoga-ae | File LEARNINGS.md entries; planned follow-up CSs for any deferred ABAC wire-through |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed with the GPT-5.5 close-out gate before the `active → done` rename (CS03b). A NEEDS-FIX outcome blocks close-out._
