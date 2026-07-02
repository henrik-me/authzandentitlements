# CS05 — AuthZEN-aligned unified PDP abstraction

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** PDP-core (hub)
**Depends on:** CS02

## Goal

Define the unified, AuthZEN-aligned PDP abstraction + scenario catalog so every engine answers the same question.

## Deliverables

- IAuthorizationDecisionProvider: subject/action/resource/context -> decision + reasons/obligations (AuthZEN-aligned).
- Authz.Pdp host service + config-driven provider selection.
- Scenario catalog of fintech decisions expressed once, dispatchable to any engine.
- Per-decision audit event + OTel span/metric hooks.

## Exit criteria

- A reference provider answers the full scenario catalog.
- Contract documented and ready for adapter CS06-CS09.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Design AuthZEN-aligned contract | pending | — | |
| Implement Authz.Pdp host | pending | — | |
| Author scenario catalog | pending | — | |
| Wire audit/OTel hooks | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
