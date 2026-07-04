# CS17 — Policy lifecycle + validation/testing

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs17/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS06, CS07, CS08, CS09

## Goal

Treat policies as code with a full lifecycle and rigorous validation (key).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 24d816a2d5a8 | 2026-07-02T19:47:54Z | Go | Engine adapter deps are sufficient for shadow dual-run and AuthZEN conformance. |

## Deliverables

- Policy versioning + CI validation; rollout/rollback; simulation/what-if; drift detection.
- Golden-decision tests, negative + property-based tests, AuthZEN conformance.
- Shadow/dual-run comparison harness.

## Exit criteria

- Policy changes are gated by CI tests; what-if simulation available; shadow-run compares engines on identical inputs.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Policy CI + versioning | pending | — | |
| Golden/negative/property tests | pending | — | |
| AuthZEN conformance suite | pending | — | |
| Shadow-run harness | pending | — | |
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
