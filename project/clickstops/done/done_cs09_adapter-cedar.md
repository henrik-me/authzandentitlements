# CS09 — Adapter: Cedar (policy / ABAC)

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs09/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate Cedar (in-process via MonoCloud Cedar for .NET) as a second policy engine to compare against OPA.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 2223f626be57 | 2026-07-02T19:47:54Z | Go-with-amendments | Clarify Cedar parity is against the CS05 shared policy catalog, not CS08 artifacts, to preserve parallelism. |

## Deliverables

- CedarProvider using MonoCloud Cedar for .NET (verify .NET 10 compat).
- Cedar schema + policies mirroring the OPA scenarios.
- Amazon Verified Permissions documented as the cloud option.

## Exit criteria

- Cedar answers the same policy scenarios as OPA for head-to-head comparison.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add MonoCloud.Cedar (CPM pin + csproj) | done | sub-agent | agent-id=cs09-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — MonoCloud.Cedar 0.1.0 (native, .NET 10) CPM pin + versionless csproj ref |
| Author Cedar schema + policies (embedded; broad permit + annotated forbids) | done | sub-agent | agent-id=cs09-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — CedarPolicyModel: per-action broad permit + annotated forbids w/ stable ids + precedence map |
| Implement CedarDecisionProvider (Name="cedar", full-decision) + DI registration | done | sub-agent | agent-id=cs09-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — full-decision, fail-closed; registered in AddPdp (default stays reference) |
| Cedar tests: catalog parity + obligations + fail-closed + selection + reason-ordering | done | sub-agent | agent-id=cs09-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — +35 tests (PDP 264→299), all green |
| Verify parity with the 22-scenario catalog (head-to-head with OPA) | done | sub-agent | agent-id=cs09-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — full-catalog parity: same Decision + primary reason code as reference/OPA |
| Adapter doc + Amazon Verified Permissions cloud option + pdp-contract pointer | done | sub-agent | agent-id=cs09-docs \| role=doc-author \| report-status=complete \| learnings=1 — docs/authz/cedar-adapter.md + pdp-contract "Shipped adapters" pointer |
| Close-out: docs + restart state | pending | yoga-ae-c4 | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_Shipped the `cedar` engine adapter behind the CS05 PDP seam: a genuine in-process Cedar engine via **MonoCloud.Cedar 0.1.0** (native bindings, .NET 10; fork of cedar-policy/cedar-java) that **natively owns the full fintech decision** — head-to-head with OPA/Rego — rather than the role-gate-only `IEngineRoleAuthorizer` split used by the RBAC-only Casbin/aspnet adapters (per LRN-026). The embedded `CedarPolicyModel` uses a broad `permit` per action + one annotated `forbid` per deny reason, built from explicit `Policy(source, id)` objects so the authorization-response determining set carries **stable, mappable ids**; the adapter maps the determining forbid set to the reference's **first-failing** reason via per-action precedence (LRN-021). Threshold obligation on `transaction.create` is computed adapter-side; unknown actions and any engine error **fail closed** (provider-local `ProviderUnavailable`, never throws, never permits) mirroring the OPA adapter. Container-free "lite" profile; default provider stays `reference`. `dotnet build` 0/0; full-solution `dotnet test` **299** (PDP 264→299, +35 incl. full-catalog parity + per-scenario + obligations + combined-failure ordering + fail-closed + runtime selection). Doc: `docs/authz/cedar-adapter.md` (+ Amazon Verified Permissions as the managed/cloud option) and a `pdp-contract.md` "Shipped adapters" pointer._

_Learning candidates (to file at close-out): (1) **cedar-api** — `PolicySet.ParsePolicies` ignores `@id` annotations and auto-assigns sequential `policyN` ids; build the `PolicySet` from explicit `Policy(source, id)` objects so `AuthorizationSuccessResponse.GetReason()` returns stable, mappable determining ids (combined failures return the full determining set, enabling first-failing precedence selection). (2) **cedar-net10-compat** — MonoCloud.Cedar 0.1.0 restores/builds 0/0 and loads its win-x64 native binary under the .NET 10 RC runtime with no extra setup. (3) **doc-consistency** — `opa-adapter.md` existed but was never linked from `pdp-contract.md`'s "Shipped adapters" note; the CS09 doc pointer closes it for both OPA and Cedar._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T02:20:00Z
**Analyzed HEAD:** 1b90ae9 (merged main, PR #48)
**Outcome:** GO

Per-deliverable outcomes (all **match**):

| # | Deliverable | Outcome |
|---|---|---|
| 1 | CedarProvider using MonoCloud Cedar for .NET (verify .NET 10 compat) | match — `Providers/Adapters/Cedar/CedarDecisionProvider.cs` (`Name="cedar"`, `BasicAuthorizationEngine`), registered in `AddPdp`; `MonoCloud.Cedar` 0.1.0 pinned (CPM) + referenced from the net10.0 PDP project; native binding loads (build + Cedar tests exercise real evaluation). |
| 2 | Cedar schema + policies mirroring the OPA scenarios / CS05 catalog | match — `CedarPolicyModel.cs` embeds a broad `permit` per action + one annotated `forbid` per deny reason (explicit `Policy(source,id)`), and the adapter maps the determining set to the reference's first-failing reason with the threshold obligation. Per the R1 amendment, parity is against the CS05 22-scenario catalog (not literal CS08 artifacts); per LRN-026 Cedar owns the full decision natively (head-to-head with OPA). The "schema" is the embedded entity shape (no separate Cedar `Schema` artifact), documented explicitly. |
| 3 | Amazon Verified Permissions documented as the cloud option | match — `docs/authz/cedar-adapter.md` documents AVP as the managed/cloud Cedar option (policy store + `IsAuthorized`, trade-offs); `pdp-contract.md` points to it. |

**Exit criterion:** **met** — Cedar answers the same 22-scenario `FintechScenarioCatalog` as the reference/OPA (same `Decision` + primary reason code), verified by `CedarDecisionProviderTests` (full-catalog + per-scenario) and the full-solution build 0/0 / PDP `dotnet test` 358/358.

**Scope discipline:** confirmed within CS09 (Cedar provider + embedded policy model + registration + CPM/csproj + docs/tests); the single non-CS09 change (repointing the CS07 `OpenFgaRegistrationTests` unknown-provider placeholder from `"cedar"` — now a real engine — to `"does-not-exist"`) is a justified integration fix.

Independently verified: `dotnet build` 0/0; `dotnet test` (PDP) 358/358. Local code review: GPT-5.5 rubber-duck R1 (Block: tenant fail-open + doc) → R2 (Go-with-amendments) → R3/R5 (Go); Copilot review (2 non-blocking comments addressed, threads resolved). Content PR #48 (squash-merged).
