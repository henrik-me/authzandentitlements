# PDP engine adapters: ASP.NET Core policies + Casbin.NET (CS06)

> **Scope:** the first two engine adapters behind the unified AuthZEN-aligned PDP — the
> in-process, container-free ".NET baselines". They implement the CS05
> [`IAuthorizationDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/IAuthorizationDecisionProvider.cs)
> contract documented in [pdp-contract.md](pdp-contract.md) and answer the same 22-scenario
> [`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
> as the reference engine. See [ARCHITECTURE.md](../../ARCHITECTURE.md) and the
> [coarse- vs. fine-grained boundary](../architecture/coarse-vs-fine-boundary.md).

## What CS06 ships

Two engines register alongside the CS05 `reference` provider and are selectable by
configuration without touching calling code:

| Engine name | Type | Backing technology | Package |
|---|---|---|---|
| `aspnet` | `AspNetCorePolicyProvider` | ASP.NET Core policy-based authorization (`RolesAuthorizationRequirement`) | none — shared framework |
| `casbin` | `CasbinDecisionProvider` | Casbin.NET RBAC model + policy (in-process) | `Casbin.NET` (CPM-pinned) |

Both run entirely in-process: **no containers, no external files, no network** ("lite"
profile). Both are pure, deterministic functions of the `AccessRequest`.

## Design: one shared harness, a swappable engine for the RBAC core

The fintech decision is mostly **ABAC** — coarse-scope re-check, tenant isolation,
subject-is-maker, pending status, segregation of duties, and the $10,000 approval-threshold
obligation — plus a per-action **ordering** that determines which failure a caller sees
first. Only one dimension is genuinely **RBAC**: *is the subject's role eligible for this
action?*

CS06 factors the decision accordingly:

- [`FintechRuleEvaluator`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/FintechRuleEvaluator.cs)
  owns the engine-agnostic part: the exact same ordered pipeline + ABAC rules as the CS05
  [`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs),
  returning the first failing rule's reason code. This is what keeps every adapter in
  lock-step parity with the reference and the shared catalog.
- [`IEngineRoleAuthorizer`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/IEngineRoleAuthorizer.cs)
  is the one hook the evaluator delegates to the engine: `IsRoleAuthorized(action,
  subjectRoles)`. Each adapter answers it with its own engine, and encodes the eligible-role
  *sets* in that engine's native policy form (an ASP.NET policy, a Casbin RBAC policy) —
  which is what makes these genuine engine integrations rather than hard-coded role lists.

So an adapter is thin by design — it supplies a role gate and defers everything else:

```csharp
public AccessDecision Evaluate(AccessRequest request) =>
    FintechRuleEvaluator.Evaluate(request, _roleAuthorizer);
```

This realizes the architecture's thesis directly: **the same question, a swappable engine.**
It is also exactly what the CS06 plan-review amendment intends — the RBAC baselines cover the
role decision, and the non-RBAC (ABAC) rules are composed by the shared harness rather than
being force-fit into an RBAC engine.

### Role eligibility (the only thing the engine decides)

| Action | Eligible roles |
|---|---|
| `bank.account.read` | *not role-gated* (any authenticated, same-tenant caller) |
| `bank.account.create` | `BranchManager` |
| `bank.transaction.create` | `Teller`, `BranchManager`, `ComplianceOfficer` (maker-eligible) |
| `bank.transaction.approve` / `bank.transaction.reject` | `BranchManager`, `ComplianceOfficer` (checker-eligible) |

Everything else (scope, tenant, maker, pending, SoD, obligation, unknown-action) is decided
identically for every engine by `FintechRuleEvaluator`, so all three providers return the
same decision **and** the same primary reason code for all 22 catalog scenarios.

## The `aspnet` engine

`AspNetCorePolicyProvider` (`Name = "aspnet"`) answers the role gate with ASP.NET Core's own
policy-based authorization primitives from the shared framework — the same
`RolesAuthorizationRequirement` type that backs `[Authorize(Roles = …)]` and
`AuthorizationPolicyBuilder.RequireRole(…)`. For each role-gated action it holds a
requirement carrying that action's eligible roles, builds a `ClaimsPrincipal` with the
subject's role claims, and evaluates the requirement through an `AuthorizationHandlerContext`.
It needs no NuGet package (the types live in `Microsoft.AspNetCore.Authorization`, already
available to the Web SDK project).

## The `casbin` engine

`CasbinDecisionProvider` (`Name = "casbin"`) answers the role gate with a Casbin.NET RBAC
model + policy: the model is embedded as a text constant and the policy is a set of in-memory
`(role, action)` pairs added programmatically via `AddPolicy` at construction — no
`.conf`/`.csv` files on disk, no persistent adapter store. The model maps a subject to an action:

```ini
[request_definition]
r = sub, act
[policy_definition]
p = sub, act
[policy_effect]
e = some(where (p.eft == allow))
[matchers]
m = r.sub == p.sub && r.act == p.act
```

with one `AddPolicy(role, action)` grant per eligible pair. `IsRoleAuthorized` enforces the
subject's roles against the action:

```csharp
subjectRoles.Any(role => enforcer.Enforce(role, action));
```

## Selecting an engine

Selection is the CS05 seam, unchanged: set `Pdp:Provider` in
[`appsettings.json`](../../src/AuthzEntitlements.Authz.Pdp/appsettings.json) (or any config
source) to the engine's name. The default stays `reference` so builds, tests, and `aspire
run` never depend on a specific adapter.

```json
{ "Pdp": { "Provider": "casbin" } }
```

An unknown name still fails closed via
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
— it throws, naming the unknown provider and listing the registered ones, rather than
silently defaulting to some engine.

## Parity guarantee

Each adapter is tested by running the full `FintechScenarioCatalog` through
[`ScenarioCatalogRunner`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/ScenarioCatalogRunner.cs):
a scenario passes only when the adapter's decision **and** primary reason code match the
catalog's expectation. All 22 scenarios pass for both `aspnet` and `casbin`, so they are
verified equivalent to the reference across tenant isolation, role eligibility, the
maker-checker threshold and boundary, segregation of duties, subject-is-maker, missing
scopes, and the fail-closed unknown-action path.
