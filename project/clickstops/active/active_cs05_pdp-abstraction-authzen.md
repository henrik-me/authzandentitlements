# CS05 — AuthZEN-aligned unified PDP abstraction

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs05/content
**Started:** 2026-07-03
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** PDP-core (hub)
**Depends on:** CS02

## Goal

Define the unified, AuthZEN-aligned PDP abstraction + scenario catalog so every engine answers the same question.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 058c4149dd2c | 2026-07-02T19:47:54Z | Go-with-amendments | Sound hub dependency on CS02; clarify audit and OTel work as contracts/hooks only to avoid stealing CS12/CS13 scope. |

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
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
