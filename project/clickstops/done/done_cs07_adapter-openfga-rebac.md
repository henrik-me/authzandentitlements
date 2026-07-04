# CS07 — Adapter: OpenFGA (ReBAC / Zanzibar)

**Status:** done
**Owner:** yoga-ae-c2
**Branch:** cs07/content
**Started:** 2026-07-03
**Closed:** 2026-07-04
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
| Add OpenFGA container | done | yoga-ae-c2 | agent-id=cs07-openfga-impl \| role=implementer \| report-status=complete \| learnings=2 |
| Author model + tuples | done | yoga-ae-c2 | agent-id=cs07-openfga-impl \| role=implementer \| report-status=complete \| learnings=2 |
| Implement adapter | done | yoga-ae-c2 | agent-id=cs07-openfga-impl \| role=implementer \| report-status=complete \| learnings=2 |
| Verify reverse-index queries | done | yoga-ae-c2 | agent-id=cs07-openfga-impl \| role=implementer \| report-status=complete \| learnings=2 |
| Close-out: docs + restart state | done | yoga-ae-c2 | Updated WORKBOARD.md + CONTEXT.md (CS07 complete entry + adapter status line); surface via /api/authz/rebac endpoints |
| Close-out: learnings + follow-ups | done | yoga-ae-c2 | Filed LRN-030..031; follow-ups noted (configurable/pinned model id, targeted tuple reconciliation, xUnit v3 Assert.Skip) |

## Notes / Learnings

_Delivered the OpenFGA (ReBAC / Zanzibar) engine adapter for the unified PDP: a config-gated `openfga` provider (`Pdp:Provider=openfga`) behind the CS05 seam, kept off the default no-Docker path. Ships a schema-1.1 ReBAC authorization model covering all four relationship types (account ownership, relationship-manager→customer indirection, branch/region hierarchy via tuple-to-userset, delegation) + a consistent synthetic seed-tuple graph; a fail-closed `OpenFgaProvider` (forward `Check` via the sync seam, bridged sync-over-async, Deny on engine failure) and `OpenFgaRebacService` (lazy client, idempotent bootstrap, reverse-index `ListUsers`/`ListObjects`); `/api/authz/rebac/{verify,who-can-access,what-can-user-access}` endpoints (400 on bad input, 503 fail-closed on engine unavailability); and pinned `openfga/openfga:v1.18.1` migrate+run Aspire containers on the shared `openfga` postgres db. Content PR #35 (squash-merged 99d4abe); integrated CS06/CS08/#40 during review. `dotnet build` 0/0; full solution 456 tests (PDP 316, +51 OpenFGA/ReBAC incl. deterministic units + self-skipping live-server integration + a fail-closed Evaluate test). Reviewed across 18 GPT-5.5 rubber-duck rounds (R1/R16 Needs-Fix fixed) + Copilot (13 comment rounds, all addressed — incl. real fixes: volatile bootstrap publication, mapper fail-closed on non-account resources, reverse-index prefix/whitespace 400s, endpoint 503s, provider fail-closed Evaluate, /verify full-wrap, info-leak → stable messages). Plan-vs-impl GPT-5.5 GO. New learnings LRN-030..031._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T01:10:00Z
**Outcome:** GO

Per-deliverable outcomes:

| Deliverable | Outcome |
|---|---|
| D1 — OpenFGA Aspire container | match — `AppHost.cs` wires `openfga-migrate` + `openfga` (pinned `openfga/openfga:v1.18.1`, postgres datastore, `WithExplicitStart`) and injects `Pdp__OpenFga__ApiUrl` into `authz-pdp`. |
| D2 — ReBAC model + tuples | match — `RebacModel.cs` (schema 1.1) models ownership, RM→customer, branch/region hierarchy, and delegation; `RebacSeedTuples.cs` seeds each category. |
| D3 — OpenFgaProvider + forward/reverse checks | match — `OpenFgaProvider` forward `Check` via the sync PDP seam (fail-closed); `OpenFgaRebacService` `ListUsers`/`ListObjects`; `RebacEndpoints` exposes `/verify`, `/who-can-access`, `/what-can-user-access`. |
| Exit criteria — who can view X / what can user Y access | match — reverse-index endpoints + `RebacScenarioCatalog` cover both directions against live OpenFGA. |

**Test coverage:** sufficient — structural model/tuple/catalog tests, provider mapping/registration/fail-closed tests, and self-skipping live-OpenFGA integration tests cover forward checks plus both reverse-index directions.

Scope note: CS07 ships a ReBAC-specific `RebacScenarioCatalog` rather than reusing the RBAC `FintechScenarioCatalog` — design-consistent (ReBAC ≠ RBAC), not a blocking divergence. Independently verified on `main`: `dotnet build` 0/0, full solution 456 tests pass (PDP 316), `harness lint` 0 failed.
