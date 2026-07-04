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
| Config-driven provider selection | pending | — | |
| RBAC->ReBAC translator | pending | — | |
| Dual-run compare | pending | — | |
| Author adapter guide | pending | — | |
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
