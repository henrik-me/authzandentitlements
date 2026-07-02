# CS17 — Policy lifecycle + validation/testing

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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

## Notes / Learnings

_None yet — populated during implementation and close-out._
