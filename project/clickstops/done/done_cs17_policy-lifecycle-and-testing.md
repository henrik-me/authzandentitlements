# CS17 — Policy lifecycle + validation/testing

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs17/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
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
| Policy CI + versioning | done | yoga-ae-c3 | Golden-snapshot version hash + drift (`GoldenDecisionSnapshot`, `GET /policy/version`); policy test suite is the gate + adoption snippet in docs |
| Golden/negative/property tests | done | yoga-ae-c3 | `GoldenDecisionTests`, `PolicyInvariantTests` (determinism, fail-closed totality, threshold obligations, cross-engine parity) |
| AuthZEN conformance suite | done | yoga-ae-c3 | `Lifecycle/AuthZen` mapper + `POST /authzen/evaluation` + `AuthZenConformanceTests` (full-catalog round-trip) |
| Shadow-run harness | done | yoga-ae-c3 | `ShadowRunner` + `POST /shadow{,/catalog}` + what-if (`WhatIfEvaluator`, `POST /whatif`); `ShadowRunnerTests`, `WhatIfEvaluatorTests` |
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

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T03:46:47Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable / sub-part | Outcome | Rationale |
|---|---|---|
| D1 — Policy versioning | match | `GoldenDecisionSnapshot.Version` SHA-256 content hash + `GET /api/authz/policy/version`. |
| D1 — CI validation / gating | diverged | Delivered as the runnable policy test suite (+59 tests) + an opt-in workflow snippet, NOT an active .NET CI workflow — respecting the repo's process-gates-only GitHub Actions posture (see PR #55 Notes / escalation). |
| D1 — Rollout / rollback | match | Policy-as-code process anchored by golden snapshot/version + shadow-catalog parity + rollback-by-revert with drift detection. |
| D1 — What-if simulation | match | `WhatIfEvaluator` + `POST /api/authz/whatif`, non-audited (bypasses `PdpDecisionService`). |
| D1 — Drift detection | match | `GoldenDecisionSnapshot.Diff` (decision/reason/obligation + missing/extra scenario) via `/policy/version`. |
| D2 — Golden-decision tests | match | `GoldenDecisionTests` (baseline alignment, per-engine no-drift, drift, missing/extra, obligations, version shape). |
| D2 — Negative + property-based tests | match | `PolicyInvariantTests` deterministic generated cross-product invariants + fail-closed negatives. |
| D2 — AuthZEN conformance | match | `AuthZenMapper`/endpoint + fail-closed `AuthZenRequestValidation` + full-catalog round-trip (`AuthZenConformanceTests`). |
| D3 — Shadow / dual-run harness | match | `ShadowRunner` single-request + whole-catalog decision/reason/obligation diffs. |

**Test coverage:** gaps (non-blocking) — endpoints are validated at the service/mapper level (pure-domain, per repo convention) rather than via HTTP-level integration tests; the non-audited invariant of what-if/shadow is by-design (bypasses `PdpDecisionService`) but not asserted through an endpoint-level fake audit sink; CI gating is documentation-only (the deliberate posture). Filed as LRN-035 for a possible follow-up.

**Outcome GO:** no blocking implementation gaps; the CI item is a documented, intentional divergence given the repo's process-gates-only posture and the delivered local policy test gate. Review lineage: GPT-5.5 rubber-duck R1 (Needs-Fix: AuthZEN boundary fail-open) → R2–R5 (Go); Copilot 3 rounds (whitespace-normalization, nullable `out`, nullable param — all resolved).
