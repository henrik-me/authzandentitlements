# CS07 — Adapter: OpenFGA (ReBAC / Zanzibar)

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs07/content
**Started:** 2026-07-03
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate OpenFGA for relationship-based fintech scenarios.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 2cf861063d47 | 2026-07-02T19:47:54Z | Go | Sound as-is: CS05 dependency, OpenFGA ReBAC tuples, and forward/reverse queries align with the graph. |

## Deliverables

- OpenFGA Aspire container.
- ReBAC model + tuples: account ownership, relationship-manager->customer, branch/region hierarchy, delegation.
- OpenFgaProvider (OpenFga.Sdk); forward + reverse-index checks.

## Exit criteria

- OpenFGA answers ReBAC scenarios including "who can view account X" / "what can user Y access".

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add OpenFGA container | pending | — | |
| Author model + tuples | pending | — | |
| Implement adapter | pending | — | |
| Verify reverse-index queries | pending | — | |
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
