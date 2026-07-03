# CS04 — Coarse-grained edge gateway (YARP)

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs04/content
**Started:** 2026-07-03
**Closed:** —
**Phase:** 1 — AuthN + coarse-grained
**Lane:** Identity
**Depends on:** CS03

## Goal

Enforce coarse-grained authorization on token scopes/claims at a YARP edge before any fine-grained check.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | bd7f5db0abb5 | 2026-07-02T19:47:54Z | Go-with-amendments | Blocker resolved; no live Audit.Service required, but align deliverable/task wording to say audit-ready events. |

## Deliverables

- Edge.Gateway (YARP) routing to services.
- Coarse policies on scope/claim/audience/tenant.
- Documented coarse-vs-fine boundary; both gates emit audit + telemetry.

## Exit criteria

- Requests lacking a required scope/audience/tenant are rejected at the edge.
- Allowed requests are routed; gateway decisions emit structured, audit-ready events + OTel (Audit.Service ingests them in CS13).

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add YARP gateway + routes | pending | — | |
| Define coarse-grained policies | pending | — | |
| Wire audit + telemetry | pending | — | |
| Document the boundary | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
