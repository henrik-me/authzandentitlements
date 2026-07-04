# CS31 — Engine-adapter test seams & degenerate-input parity

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c3 — 2026-07-04, LRN harvest (CS28h): dispositioning open learnings into fix CSs.
**Depends on:** CS07, CS09, CS16

## Goal

Improve the offline testability and fail-closed coverage of the PDP engine adapters: extract an OpenFGA check seam for offline ReBAC explanation tests, add degenerate-input fail-closed parity rows/tests against the reference oracle, and land the deferred OpenFGA model-id / reconciliation hardening.

## Background

Fixes **LRN-038**, **LRN-033**, and **LRN-031**.

LRN-038: `OpenFgaRebacService` is sealed and non-virtual, so a permit/deny `DecisionExplanation` cannot be asserted in the offline suite (a blank `ApiUrl` throws in `BuildClient`). Extracting an `IOpenFgaCheckClient` seam that the provider depends on makes ReBAC permit/deny explanations unit-testable without a live server.

LRN-033: the 22-scenario `FintechScenarioCatalog` uses only non-blank tenants, so fail-closed predicates (tenant, maker, status, scope) are never exercised on null/empty/whitespace input — a real fail-open tenant-isolation gap stayed green over the catalog. Add degenerate rows/tests that assert parity against `ReferenceDecisionProvider` (Decision + `Reasons[0].Code`), not a hardcoded expectation.

LRN-031: deferred OpenFGA follow-ups — a configurable/pinned authorization-model id (to avoid per-boot model-version growth on a persistent store), targeted tuple-existence reconciliation instead of read-all, and adopting `Assert.Skip` if/when the repo moves to xUnit v3.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | OpenFGA test seam | Extract `IOpenFgaCheckClient` that `OpenFgaProvider` depends on; `OpenFgaRebacService` implements it; a test double forces `allowed=true`/`false` offline. | LRN-038 — permit/deny explanations must be assertable without a live OpenFGA server. |
| 2 | Degenerate-input coverage | Add null/empty/whitespace-attribute parity tests asserting equivalence to `ReferenceDecisionProvider` for **every** engine; consider adding blank/whitespace rows to the shared catalog so all engines are held to them. | LRN-033 — a realistic-values catalog does not exercise fail-closed predicates on boundary input. |
| 3 | OpenFGA hardening | Make the authorization-model id configurable/pinned; use targeted tuple-existence reconciliation instead of read-all; keep the soft-skip unless xUnit v3 lands. | LRN-031 — avoid per-boot model-version growth on a persistent shared store. |

## Deliverables

- `IOpenFgaCheckClient` seam plus offline permit/deny explanation tests.
- Degenerate-input fail-closed parity tests (and optional shared-catalog blank/whitespace rows).
- OpenFGA authorization-model-id pin configuration plus targeted tuple reconciliation.

## User-approval gates

None.

## Exit criteria

- Offline ReBAC permit/deny explanation is asserted without a live server.
- Degenerate-input parity is green across all engines.
- The OpenFGA authorization-model id is pinned/configurable.
- Full-solution `dotnet build` + `dotnet test` green.

## Risks + open questions

- The xUnit v3 migration is out of scope — keep the soft-skip.
- Shared-catalog row additions must not break existing engine parity.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 4b4f2c2eaed9 | 2026-07-04T17:47:00Z | Go | LRN scope, cited symbols, dependencies, and deliverables all align; no blocking issues. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
