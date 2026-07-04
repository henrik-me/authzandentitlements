# CS16 — Explainability: why allowed / why denied

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs16/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS06, CS07, CS08, CS09

## Goal

Make "why allowed / why denied" a first-class, normalized output for every decision (critical).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 79296c98c8a7 | 2026-07-02T19:47:54Z | Go-with-amendments | Engine deps resolve extraction; clarify CS15 owns UI rendering or add CS15 if CS16 must deliver display. |

## Deliverables

- Normalized reason/obligation model in the PDP contract.
- Per-engine explanation extraction (OPA/Cedar policy trace, OpenFGA relationship path, Casbin matched rule).
- Surfaced in Playground + Audit Explorer; documented comparison of explanation quality.

## Exit criteria

- Every decision returns a structured reason; each engine’s explanation is captured and displayed.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Wave A — normalized explanation contract + service/audit/endpoint wiring | done | yoga-ae-c4 | agent-id=cs16-foundation \| role=implementer \| model=claude-opus-4.8 \| report-status=complete \| learnings=1 |
| Wave B — reference + Casbin + ASP.NET extraction (shared FintechRuleEvaluator) | done | yoga-ae-c4 | agent-id=cs16-rbac-family \| role=implementer \| model=claude-opus-4.8 \| report-status=complete \| learnings=0 |
| Wave B — OPA/Rego policy-trace extraction | done | yoga-ae-c4 | agent-id=cs16-opa \| role=implementer \| model=claude-opus-4.6 \| report-status=complete \| learnings=1 |
| Wave B — Cedar policy-id extraction | done | yoga-ae-c4 | agent-id=cs16-cedar \| role=implementer \| model=claude-opus-4.8 \| report-status=complete \| learnings=0 |
| Wave B — OpenFGA relationship-path extraction | done | yoga-ae-c4 | agent-id=cs16-openfga \| role=implementer \| model=claude-opus-4.6 \| report-status=complete \| learnings=1 |
| Surface explanation in /evaluate response + audit event (UI rendering deferred to CS15 per Plan review R1) | done | yoga-ae-c4 | data-only in CS16; CS15 owns playground/audit-explorer rendering |
| Wave C — explainability + explanation-quality comparison docs | pending | yoga-ae-c4 | agent-id=cs16-docs \| role=implementer \| model=claude-opus-4.8 \| report-status=pending \| learnings=0 |
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

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T03:52:00Z
**Outcome:** GO

Independent plan-vs-implementation review (GPT-5.5, independent of the claude-opus-4.8/4.6 implementers) against the merged content squash `6f17c05`.

| # | CS16 plan deliverable | Outcome | Assessment |
|---|---|---|---|
| 1 | Normalized reason/obligation model in the PDP contract. | match | `DecisionExplanation`, `PolicyReference`, normalized `DeterminingRules`/`PolicyReferenceKinds`, and `AccessDecision.Explanation` were added while preserving existing reasons/obligations. |
| 2 | Per-engine explanation extraction. | match | All engines attach structured explanations: reference/shared-evaluator `rule` ids, Casbin matched policy line, ASP.NET requirement, OPA Rego rule id + package path, Cedar determining policy id(s), and OpenFGA checked relationship tuple. |
| 3 | Surfaced in Playground + Audit Explorer; documented comparison of explanation quality. | match | Per the Plan review R1 amendment, CS16 shipped the DATA surface (`/evaluate` + `/scenarios/verify` responses, `PdpDecisionAuditEvent` fields + the default logging sink) plus `docs/authz/explainability.md` (explanation-quality comparison); interactive UI rendering remains CS15. |

Material additions beyond the plan: a central baseline-explanation guarantee in `PdpDecisionService` (no decision is ever unexplained), structured emission of the explanation fields in `LoggingPdpDecisionAuditSink`, and runtime `/evaluate` smoke evidence for permit + deny.

**Test-coverage assessment:** sufficient. Full solution `dotnet test` 546/546; PDP 406/406; `opa test` 51/51; runtime smoke confirmed. Non-blocking caveat: OpenFGA permit/deny relationship explanations rely on mapper/unit coverage + the live runtime smoke rather than a fully offline fake-server unit test (OpenFgaRebacService is sealed/non-mockable — see LRN-038).
