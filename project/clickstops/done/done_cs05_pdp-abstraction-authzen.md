# CS05 — AuthZEN-aligned unified PDP abstraction

**Status:** done
**Owner:** yoga-ae-c2
**Branch:** cs05/content
**Started:** 2026-07-03
**Closed:** 2026-07-03
**Phase:** 2 — Fine-grained AuthZ
**Lane:** PDP-core (hub)
**Depends on:** CS02

## Goal

Define the unified, AuthZEN-aligned PDP abstraction + scenario catalog so every engine answers the same question.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 058c4149dd2c | 2026-07-02T19:47:54Z | Go-with-amendments | Sound hub dependency on CS02; clarify audit and OTel work as contracts/hooks only to avoid stealing CS12/CS13 scope. |

## Deliverables

- IAuthorizationDecisionProvider: subject/action/resource/context -> decision + reasons/obligations (AuthZEN-aligned).
- Authz.Pdp host service + config-driven provider selection.
- Scenario catalog of fintech decisions expressed once, dispatchable to any engine.
- Per-decision audit event + OTel span/metric hooks.

## Exit criteria

- A reference provider answers the full scenario catalog.
- Contract documented and ready for adapter CS06-CS09.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Design AuthZEN-aligned contract | done | yoga-ae-c2 | Orchestrator design from CS02/CS03 domain + enforcement map |
| Implement Authz.Pdp host + contract + reference provider | done | sub-agent | agent-id=cs05-impl-core \| role=service-implementer \| report-status=complete \| learnings=3 |
| Author scenario catalog | done | sub-agent | agent-id=cs05-impl-core \| role=service-implementer \| report-status=complete \| learnings=3 |
| Wire audit/OTel hooks (contracts/hooks only) | done | sub-agent | agent-id=cs05-impl-core \| role=service-implementer \| report-status=complete \| learnings=3 |
| PDP unit tests | done | sub-agent | agent-id=cs05-impl-tests \| role=test-author \| report-status=complete \| learnings=3 |
| Document AuthZEN contract for CS06-CS09 | done | sub-agent | agent-id=cs05-impl-docs \| role=doc-author \| report-status=complete \| learnings=3 |
| Close-out: docs + restart state | done | yoga-ae-c2 | Updated WORKBOARD.md, CONTEXT.md; docs/authz/pdp-contract.md shipped in content PR |
| Close-out: learnings + follow-ups | done | yoga-ae-c2 | Filed LRN-021, LRN-022; no follow-up CS needed (CS06-CS09 already planned) |

## Notes / Learnings

_Delivered the `AuthzEntitlements.Authz.Pdp` service: AuthZEN-aligned `IAuthorizationDecisionProvider`, in-process reference provider mirroring the Bank.Api rules, config-driven fail-closed provider selection (the CS06–CS09 seam), a 22-scenario engine-agnostic catalog + runner, and per-decision audit + OTel hooks (contracts only). Content PR #24 (squash-merged); build 0/0, 139 PDP tests, full-solution tests green. GPT-5.5 rubber-duck R1 (Block) → R2–R6 (Go) + 5 Copilot rounds, all findings resolved. New learnings: LRN-021 (reference-provider rule-order parity), LRN-022 (static-telemetry test isolation)._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-03T21:44:05Z
**Outcome:** GO

Per-deliverable outcomes (all **match**):

| # | Deliverable | Outcome |
|---|---|---|
| 1 | `IAuthorizationDecisionProvider` (subject/action/resource/context → decision + reasons/obligations, AuthZEN-aligned) | match — `Contracts/*`, documented in `docs/authz/pdp-contract.md` |
| 2 | `Authz.Pdp` host + config-driven provider selection | match — `Program.cs`, `PdpOptions`, `AuthorizationDecisionProviderFactory` (fail-closed), AppHost + `.sln` wiring |
| 3 | Scenario catalog expressed once, dispatchable to any engine | match — `FintechScenarioCatalog` (22 scenarios) + `ScenarioCatalogRunner` |
| 4 | Per-decision audit event + OTel span/metric hooks | match — `PdpDecisionService` funnels one audit event + `pdp.evaluate` span + `pdp.decisions.total` counter per decision |

**Exit criteria:** both **met** — the reference provider answers the full 22-scenario catalog (`ScenarioCatalogRunner`, `POST /api/authz/scenarios/verify` → 200 only when all pass); the contract is documented for CS06–CS09 in `docs/authz/pdp-contract.md`.

**Test coverage:** sufficient — 139 PDP tests (catalog parity, per-rule branches, provider selection/fail-closed, request validation, contract vocabulary, audit/telemetry hooks, and review-hardening cases).

**Scope discipline:** confirmed hooks/contracts only — no live Audit.Service (CS13) and no observability stack/collector (CS12); no Bank.Api integration (AppHost adds standalone `authz-pdp` only). In-scope hardening added during review: boundary request validation, fail-fast blank/duplicate provider names, whitespace-trim config, whitespace-tenant fail-closed, low-cardinality metric action, reasonless-provider audit fallback.

Independently verified: `dotnet build` 0/0; `dotnet test` (PDP) 139/139.
