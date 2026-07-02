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

## Deliverables

- Edge.Gateway (YARP) routing to services.
- Coarse policies on scope/claim/audience/tenant.
- Documented coarse-vs-fine boundary; both gates emit audit + telemetry.

## Exit criteria

- Requests lacking a required scope/audience/tenant are rejected at the edge.
- Allowed requests are routed; gateway decisions emit audit + OTel.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add YARP gateway + routes | pending | — | |
| Define coarse-grained policies | pending | — | |
| Wire audit + telemetry | pending | — | |
| Document the boundary | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
