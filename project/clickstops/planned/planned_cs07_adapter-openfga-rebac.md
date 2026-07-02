# CS07 — Adapter: OpenFGA (ReBAC / Zanzibar)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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

## Notes / Learnings

_None yet — populated during implementation and close-out._
