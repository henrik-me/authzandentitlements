# CS16 — Explainability: why allowed / why denied

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs16/content
**Started:** 2026-07-04
**Closed:** —
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

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
