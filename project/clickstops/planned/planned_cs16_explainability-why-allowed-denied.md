# CS16 — Explainability: why allowed / why denied

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS06, CS07, CS08, CS09

## Goal

Make "why allowed / why denied" a first-class, normalized output for every decision (critical).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 79296c98c8a7 | 2026-07-02T19:47:54Z | Go-with-amendments | Engine deps resolve extraction; clarify CS15 owns UI rendering or add CS15 if CS16 must deliver display. |

## Deliverables

- Normalized reason/obligation model in the PDP contract.
- Per-engine explanation extraction (OPA/Cedar policy trace, OpenFGA relationship path, Casbin matched rule).
- Surfaced in Playground + Audit Explorer; documented comparison of explanation quality.

## Exit criteria

- Every decision returns a structured reason; each engine’s explanation is captured and displayed.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Extend contract with reasons/obligations | pending | — | |
| Per-engine explanation extractors | pending | — | |
| Surface in UI | pending | — | |
| Document explanation-quality comparison | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
