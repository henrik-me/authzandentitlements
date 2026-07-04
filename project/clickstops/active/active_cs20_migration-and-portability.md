# CS20 — Migration & portability (extensibility)

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs20/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS06, CS07, CS08

## Goal

Demonstrate extensibility — swap engines behind the abstraction and translate models.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | afe0f3342fa7 | 2026-07-02T19:47:54Z | Go | CS06 supplies RBAC source, CS07 ReBAC target, and CS08 dual-run coverage. |

## Deliverables

- Config-driven engine swap with no app-code change.
- RBAC->ReBAC translation example.
- Dual-run/shadow compare of two engines; a documented "add a new engine adapter" guide.

## Exit criteria

- Switching the active engine needs no app-code change; RBAC->ReBAC translation validated; new-adapter guide usable.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Config-driven provider selection | complete | yoga-ae-c4 | agent-id=yoga-ae-c4/cs20-code \| role=implementer \| report-status=complete \| learnings=0; D1 demonstrated (no app-code change) via EngineSwapPortabilityTests over existing CS05 factory |
| RBAC->ReBAC translator | complete | yoga-ae-c4 | agent-id=yoga-ae-c4/cs20-code \| role=implementer \| report-status=complete \| learnings=0; new Migration/ translator + in-process parity resolver (roles-as-usersets); PDP tests 512->537 |
| Dual-run compare | complete | yoga-ae-c4 | agent-id=yoga-ae-c4/cs20-code \| role=implementer \| report-status=complete \| learnings=0; D3 zero-divergence parity gate via CS17 ShadowRunner.RunCatalog + non-vacuous divergence-caught test |
| Author adapter guide | complete | yoga-ae-c4 | agent-id=yoga-ae-c4/cs20-docs \| role=implementer \| report-status=complete \| learnings=1; docs/authz/migration-and-portability.md + adding-an-engine-adapter.md |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, agent `cs20-pvi`) — independent of the claude-opus-4.8 implementers
**Date:** 2026-07-04
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1: Config-driven engine swap with no app-code change | match | Implemented through the existing CS05 provider-factory seam; validated by `EngineSwapPortabilityTests` using one unchanged call site across configured providers. |
| D2: RBAC→ReBAC translation example validated | match | Ships the translator, fintech RBAC sample, generated ReBAC graph, and parity tests over the full user×permission grid plus fail-closed cases. |
| D3: Dual-run/shadow compare of two engines | match | Validated via `ShadowRunner.RunCatalog` tests for reference-vs-candidate parity and a deliberate divergence-caught case. |
| D4: Documented "add a new engine adapter" guide | match | `docs/authz/adding-an-engine-adapter.md` gives concrete implement/register/select/parity/shadow-validate steps. |

**Test-coverage:** sufficient — PDP tests 544/544; covers config-only selection, translation structure/parity/fail-closed/determinism, and dual-run agreement + non-vacuous divergence detection.

**Scope:** exit criteria honored; no substantive drift. The decided library + tests + docs scope (no new HTTP endpoint) is a legitimate satisfaction of the deliverables — CS20 demonstrates portability through the existing factory and shadow-run seams. Follow-up (non-blocking, LRN-044): fail-closed distinct/non-empty validation in `RbacPolicy.Create`.
