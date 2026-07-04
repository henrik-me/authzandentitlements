# CS31 — Engine-adapter test seams & degenerate-input parity

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs31/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
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
| Extract IOpenFgaCheckClient seam | done | yoga-ae-c3 | One-member forward-Check seam; provider depends on it; DI resolves same singleton (LRN-038) |
| Offline ReBAC permit/deny explanation tests | done | yoga-ae-c3 | FakeOpenFgaCheckClient forces allowed=true/false; asserts engine/DeterminingRule/tuple ref |
| Degenerate-input fail-closed parity tests | done | yoga-ae-c3 | null/empty/whitespace vs ReferenceDecisionProvider oracle across engines (LRN-033) |
| OpenFGA model-id pin + targeted reconciliation | done | yoga-ae-c3 | AuthorizationModelId pin; per-tuple existence probe replaces read-all (LRN-031) |
| Close-out: docs + restart state | done | yoga-ae-c3 | CONTEXT.md updated; doc docs/authz/adapter-test-seams-and-degenerate-parity.md |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | LRN-038/033/031 flipped to applied |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T20:35:04Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| `IOpenFgaCheckClient` seam + offline permit/deny explanation tests | match | Seam exposes only `CheckAsync`; provider depends on it; DI maps it to the same `OpenFgaRebacService` singleton; offline allow/deny/fail-closed tests use `FakeOpenFgaCheckClient` |
| Degenerate-input fail-closed parity vs reference oracle | match | `DegenerateInputParityTests` compares reference/aspnet/casbin/cedar on null/empty/whitespace; OpenFGA/OPA boundaries covered separately; OPA ABAC degenerate parity stays in Rego |
| OpenFGA model-id pin + targeted reconciliation | match | `OpenFgaOptions.AuthorizationModelId` pin; pin-when-configured else write-then-pin; per-tuple existence probe (not read-all); offline pin tests + self-skipping live test |
| Adapter seam / degenerate-parity doc | match | `docs/authz/adapter-test-seams-and-degenerate-parity.md` |

**Test coverage:** sufficient — verified HEAD `66fbc7d`; `dotnet build` 0/0; `dotnet test ...Authz.Pdp.Tests` **726/726**.

**Outcome GO:** All CS31 deliverables + exit criteria met; documented divergences honored (seam exposes only `CheckAsync`; degenerate cases separate from the shared catalog; OPA ABAC degenerate parity stays in the Rego suite). GPT-5.5 content review R1 Go + 2 narrow re-attests (Copilot nits + rebase) + Copilot (3 nits resolved) + PvI GO.
