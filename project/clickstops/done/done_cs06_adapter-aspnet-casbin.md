# CS06 — Adapters: ASP.NET Core policies + Casbin.NET

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs06/content
**Started:** 2026-07-03
**Closed:** 2026-07-03
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Provide the .NET-native baselines (RBAC) as container-free adapters: ASP.NET Core policy-based + Casbin.NET.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | b4705206e5f1 | 2026-07-02T19:47:54Z | Go-with-amendments | Clarify RBAC baseline coverage so passing the scenario catalog allows explicit unsupported denies for non-RBAC cases. |

## Deliverables

- AspNetCorePolicyProvider (roles: teller/manager/compliance/auditor).
- CasbinProvider (RBAC/ABAC model + policy).
- Both implement IAuthorizationDecisionProvider; container-free "lite" profile.

## Exit criteria

- Both adapters pass the scenario catalog and are selectable at runtime.
- Lite profile runs with no containers.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Shared rule-evaluator foundation | done | yoga-ae-c4 | Orchestrator seam: FintechRuleEvaluator + IEngineRoleAuthorizer (per-action ordering + ABAC; role gate delegated to the engine) |
| Implement ASP.NET policy adapter | done | sub-agent | agent-id=cs06-impl \| role=engine-adapter \| report-status=complete \| learnings=0 — AspNetCorePolicyProvider via RolesAuthorizationRequirement |
| Implement Casbin adapter | done | sub-agent | agent-id=cs06-impl \| role=engine-adapter \| report-status=complete \| learnings=0 — CasbinDecisionProvider via embedded Casbin.NET RBAC model+policy |
| Map scenarios (per-adapter catalog parity) | done | sub-agent | Both adapters pass all 22 FintechScenarioCatalog scenarios (decision + primary reason code) |
| Register adapters + verify lite profile | done | yoga-ae-c4 | AddPdp registers aspnet+casbin (default stays reference); no containers; full-solution build 0/0, all tests green (Pdp 235) |
| Adapter docs | done | yoga-ae-c4 | docs/authz/adapters-aspnet-casbin.md + pointer from pdp-contract.md |
| Close-out: docs + restart state | done | yoga-ae-c4 | Removed WORKBOARD row; CONTEXT.md CS06 summary; adapter doc shipped in content PR |
| Close-out: learnings + follow-ups | done | yoga-ae-c4 | Filed LRN-025 (claim-branch rebase race) + LRN-026 (RBAC-baseline adapter seam); no follow-up CS needed (CS07-CS09 already claimed/planned) |

## Notes / Learnings

_Shipped the first two engine adapters behind the CS05 PDP: `aspnet` (ASP.NET Core `RolesAuthorizationRequirement`) and `casbin` (embedded Casbin.NET 2.21.2 RBAC model+policy), both thin `IEngineRoleAuthorizer` wrappers over a shared `FintechRuleEvaluator` that composes the fintech ABAC in reference parity — so each adapter answers the 22-scenario catalog identically while its engine owns only role eligibility ("same question, swappable engine"). Container-free lite profile; default stays `reference`. Content PR #34 (squash-merged); build 0/0, full-solution tests 375 (PDP 139→235). GPT-5.5 rubber-duck R1 (Conditional Go)→R2/R3/R4 (Go) + 3 Copilot rounds (all resolved). New learnings: LRN-025 (claim-branch rebase race), LRN-026 (RBAC-baseline adapter seam)._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-03T23:08:00Z
**Outcome:** GO

Per-deliverable outcomes (all **match**):

| # | Deliverable | Outcome |
|---|---|---|
| 1 | AspNetCorePolicyProvider (roles: teller/manager/compliance/auditor) | match — `Providers/Adapters/AspNetCore/AspNetCorePolicyProvider.cs` (`Name="aspnet"`, `IAuthorizationDecisionProvider`, ASP.NET Core `RolesAuthorizationRequirement` role gate; auditor covered as non-eligible/read-only) |
| 2 | CasbinProvider (RBAC/ABAC model + policy) | match — `Providers/Adapters/Casbin/CasbinDecisionProvider.cs` (`Name="casbin"`, embedded Casbin.NET RBAC model + programmatic policy; ABAC composed via `FintechRuleEvaluator`) |
| 3 | Both implement IAuthorizationDecisionProvider; container-free "lite" profile | match — both implement the contract; `PdpServiceCollectionExtensions` registers reference+aspnet+casbin; Casbin model/policy are embedded strings — no files/containers/network |

**Exit criteria:** both **met** — both adapters pass the full 22-scenario `FintechScenarioCatalog` (decision + primary reason code; `dotnet test` PDP 235/235) and are runtime-selectable via `Pdp:Provider` (`AdapterProviderSelectionTests`); the lite profile runs in-process with no containers (`dotnet build` 0/0).

**Amendment (R1) honored:** the RBAC engines own only role eligibility (`IEngineRoleAuthorizer`); the non-RBAC/ABAC rules are handled explicitly by the shared `FintechRuleEvaluator`, so both adapters pass ALL 22 scenarios — exceeding the allowed "unsupported deny" fallback.

**Scope discipline:** confirmed within CS06 (two in-process adapters + shared harness + registration + docs/tests); no creep into later engine adapters or containerized PDP work.

Independently verified: `dotnet build` 0/0; `dotnet test` (PDP) 235/235. Local code review: GPT-5.5 rubber-duck R1 (Conditional Go) → R2/R3/R4 (Go); Copilot PR review 3 rounds (all resolved). Content PR #34 (squash-merged).
