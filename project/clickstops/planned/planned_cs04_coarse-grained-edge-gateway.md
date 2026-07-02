# CS04 — Coarse-grained edge gateway (YARP)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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

## Notes / Learnings

_None yet — populated during implementation and close-out._
