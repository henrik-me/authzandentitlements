# CS11 — Access-governance entitlements (Entra pattern)

**Status:** active
**Owner:** yoga-ae
**Branch:** cs11/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 3 — Entitlements
**Lane:** Entitlements
**Depends on:** CS02, CS08

## Goal

Model access-governance entitlements: access packages, JIT elevation, and access reviews.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 3d85da8202a2 | 2026-07-02T19:47:54Z | Go-with-amendments | CS08 already pulls CS05; amend to state SoD checks go through PDP OpaProvider, not direct OPA coupling. |

## Deliverables

- Governance.Service; access packages (e.g., quarter-end close).
- JIT elevation with approval workflow; time-bound access.
- Access-review / recertification campaigns; JIT tied to SoD (via OPA).

## Exit criteria

- A user requests a JIT/access-package grant, gets approval, receives time-bound access that expires; reviews run.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Model access packages | pending | — | |
| JIT approval workflow | pending | — | |
| Time-bound grants + expiry | pending | — | |
| Review campaigns | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

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

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
