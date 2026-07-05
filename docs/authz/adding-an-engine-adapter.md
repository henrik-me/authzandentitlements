# Adding an engine adapter

> **Scope:** a step-by-step checklist for plugging a **new** authorization engine into the unified
> PDP behind `IAuthorizationDecisionProvider`. It is the practical companion to the
> [PDP contract](pdp-contract.md) (the reference for the seam, the request/response shapes, and the
> reference-provider rules) and to [migration & portability](migration-and-portability.md) (why the
> engine is a config-driven, swappable choice). Read the contract first; this guide does not restate
> it. The shipped adapters — [ASP.NET Core + Casbin](adapters-aspnet-casbin.md),
> [OPA / Rego](opa-adapter.md), and [Cedar](cedar-adapter.md) — are the worked examples you copy.

## Overview

An adapter makes a new engine answer the **same** question the PDP always asks —
*may this subject perform this action on this resource in this context?* — and return the **same**
self-explaining decision. Because engines are selected by configuration
([migration & portability](migration-and-portability.md#config-driven-engine-swap-no-app-code-change)),
a correct adapter is one that (a) implements the contract, (b) decides identically to the reference
oracle across the scenario catalog, (c) fails closed, and (d) never disturbs the deterministic,
Docker-free default. The steps below are ordered; the [checklist](#checklist) at the end is the
tick-through summary.

## Steps

### 1. Implement `IAuthorizationDecisionProvider`

Implement
[`IAuthorizationDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/IAuthorizationDecisionProvider.cs):
a `Name` and an `Evaluate`. Pick a **unique, stable, lowercase** `Name` (`reference`, `aspnet`,
`casbin`, `cedar`, `opa`, `openfga`, `spicedb`, `cerbos`, `keto`, `topaz` are taken) — it is the value `Pdp:Provider` selects, matched
case-insensitively. Blank or duplicate names are rejected at startup by the factory, so choose once
and keep it stable.

```csharp
public sealed class MyEngineProvider : IAuthorizationDecisionProvider
{
    public string Name => "myengine";
    public AccessDecision Evaluate(AccessRequest request) => /* ... */;
}
```

### 2. Decide faithfully — mirror the fintech pipeline

Your engine must return the **same decision and the same primary reason code** as the reference for
every input. The oracle is
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
(`Name = "reference"`); the [PDP contract](pdp-contract.md#reference-provider-semantics) documents its
ordered per-action checks, reason codes, and obligations. There are two proven integration styles —
choose the one that matches how much of the decision your engine natively owns:

- **Full-decision engines** (e.g. OPA, Cedar) own the *entire* fintech decision natively —
  scope, tenant, maker, pending, SoD, threshold obligation, and ordering — inside their own policy
  language. See [opa-adapter.md](opa-adapter.md) and [cedar-adapter.md](cedar-adapter.md).
- **Role-gate-only engines** (e.g. ASP.NET Core, Casbin) natively decide *only* the RBAC dimension
  (is the subject's role eligible for the action?) and delegate everything else to the shared harness.
  Implement
  [`IEngineRoleAuthorizer`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/IEngineRoleAuthorizer.cs)
  and defer to
  [`FintechRuleEvaluator`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/FintechRuleEvaluator.cs),
  which runs the exact same ordered ABAC pipeline as the reference:

  ```csharp
  public AccessDecision Evaluate(AccessRequest request) =>
      FintechRuleEvaluator.Evaluate(request, this); // 'this' supplies IsRoleAuthorized
  ```

  This is the thin-adapter path — you encode only the eligible-role sets in your engine's native
  policy form. See [adapters-aspnet-casbin.md](adapters-aspnet-casbin.md).

### 3. Fail closed on every uncertainty

Any transport error, timeout, parse failure, or unknown reason **must** resolve to a `Deny` with a
stable, non-sensitive message — never a permit, and never a thrown exception that becomes the only
error signal. This is the repository-wide fail-closed doctrine. The out-of-process and native
adapters demonstrate it: [opa-adapter.md](opa-adapter.md) (a missing/unreachable OPA server denies
with a `ProviderUnavailable`-style reason), and [cedar-adapter.md](cedar-adapter.md) /
[OpenFGA](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs) for the
native/ReBAC cases. Do not leak transport or policy internals in the deny message.

**Extended-authorization context (OBO / delegation / break-glass) is fail-closed *for* you.** By
default your engine is treated as **not** supporting the CS19/CS21 extended context. The
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
wraps every provider that does not declare
[`ISupportsExtendedAuthorizationContext`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ISupportsExtendedAuthorizationContext.cs)
in a fail-closed guard that **denies** any request carrying `Subject.Actor`, `Context.Delegation`, or
`Context.BreakGlass` with the distinct reason `ExtendedContextUnsupported` — so a delegation-unaware
engine can never silently weaken an on-behalf-of decision into a fail-open. The guard sits at the
factory seam, so it protects the enforced path **and** the shadow / what-if / playground surfaces
alike; you write **no per-adapter code** for it (and you should not re-implement it inside the adapter).
**Opt in only when your engine natively honours** OBO, delegation, and break-glass — i.e. it constrains
the decision to the human/actor intersection, honours grant expiry, and never elevates an integrity
invariant — by implementing the marker interface. See the
[PDP contract's extended-authorization boundary](pdp-contract.md#extended-authorization-fail-closed-boundary).

### 4. Register in `AddPdp` — keep the default deterministic

Register the provider in
[`PdpServiceCollectionExtensions.AddPdp`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
as an `IAuthorizationDecisionProvider` singleton, alongside the existing engines:

```csharp
services.AddSingleton<IAuthorizationDecisionProvider, MyEngineProvider>();
```

The default `Pdp:Provider` stays `reference`, so **registering an adapter never changes the active
engine**. If your engine needs an out-of-process or container resource (a server, a database), make
it **opt-in**: bind its options and build any client **lazily** (only on first use), and in the
Aspire AppHost start the resource with `.WithExplicitStart()` and **no hard `WaitFor`** on the PDP —
exactly as OpenFGA and OPA are wired — so `aspire run`, builds, and tests still run the deterministic
default with no Docker.

### 5. Select via `Pdp:Provider`

Selection is the unchanged CS05 seam: set `Pdp:Provider` (in
[`appsettings.json`](../../src/AuthzEntitlements.Authz.Pdp/appsettings.json) or any config source, incl.
the `Pdp__Provider` environment variable) to your engine's `Name`. No calling code changes.

```json
{ "Pdp": { "Provider": "myengine" } }
```

An unknown name fails closed through
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs),
which throws naming the unknown provider and listing the registered ones.

### 6. Prove parity against the catalog

Answer the 22-scenario
[`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
**identically** to the reference — decision *and* primary reason code — via
[`ScenarioCatalogRunner`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/ScenarioCatalogRunner.cs).
Add a catalog-parity test modeled on the shipped per-adapter tests
([`CasbinDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CasbinDecisionProviderTests.cs),
[`CedarDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CedarDecisionProviderTests.cs),
[`OpaDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/OpaDecisionProviderTests.cs)).
Also attach a rich, engine-native **explanation** (CS16) via `AccessDecision.WithExplanation(...)`
and let the shared audit / OTel hooks fire through `PdpDecisionService` — see
[explainability.md](explainability.md) and the
[decision-explanation section](pdp-contract.md#decision-explanation-explainability) of the contract.

### 7. Validate before you trust the swap

A green parity test is the unit gate; before promoting the engine in a running system, **shadow-run**
it. Compare the candidate against the trusted engine across the whole catalog with
`POST /api/authz/shadow/catalog` (or `ShadowRunner.RunCatalog`) and require `allAgree == true` — see
[migration & portability](migration-and-portability.md#dual-run--shadow-as-the-migration-safety-net).
Then confirm the golden snapshot / policy version is unchanged (`GET /api/authz/policy/version`),
per [policy lifecycle](policy-lifecycle.md#policy-version--the-golden-snapshot).

### 8. Worked example — copy the smallest adapter

The fastest start is to copy the **thinnest** shipped adapter. For a role-gate-only engine, copy
`CasbinDecisionProvider` or `AspNetCorePolicyProvider` (documented in
[adapters-aspnet-casbin.md](adapters-aspnet-casbin.md)): they implement `IEngineRoleAuthorizer`, hold
the eligible-role sets in the engine's native policy form, and delegate the rest to
`FintechRuleEvaluator`. For a full-decision engine, [cedar-adapter.md](cedar-adapter.md) (in-process)
and [opa-adapter.md](opa-adapter.md) (out-of-process) are the templates for owning the whole decision
natively and mapping it back onto `AccessDecision`.

## Checklist

| # | Step | Done when |
|---|---|---|
| 1 | Implement `IAuthorizationDecisionProvider` | Unique, stable, lowercase `Name` + `Evaluate`. |
| 2 | Faithful semantics | Full-decision **or** role-gate-only (`IEngineRoleAuthorizer` + `FintechRuleEvaluator`), matching the reference oracle. |
| 3 | Fail closed | Transport/timeout/parse/unknown → `Deny` with a stable, non-sensitive message; never permit or throw. |
| 4 | Register in `AddPdp` | Registered as a singleton; default stays `reference`; any container resource is opt-in (`WithExplicitStart`, no hard `WaitFor`). |
| 5 | Select via `Pdp:Provider` | Setting the name selects the engine; unknown name fails closed. |
| 6 | Prove parity | Catalog-parity test green across all 22 scenarios; explanation + audit hooks attached. |
| 7 | Validate before trust | `shadow/catalog` `allAgree`; golden snapshot / policy version unchanged. |
| 8 | Worked example | Started from the smallest existing adapter (Casbin / ASP.NET) or a full-decision template (Cedar / OPA). |
| 9 | Extended-auth opt-in | Left unmarked (the factory guard fails `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass` closed with `ExtendedContextUnsupported`) **unless** the engine natively honours OBO / delegation / break-glass, in which case it implements `ISupportsExtendedAuthorizationContext`. |

## See also

- [PDP contract](pdp-contract.md) — the seam, request/response shapes, reason codes, obligations, and
  reference-provider rules this guide builds on.
- [Migration & portability](migration-and-portability.md) — config-driven swap, RBAC → ReBAC
  translation, and the shadow / dual-run gate.
- [ASP.NET Core + Casbin](adapters-aspnet-casbin.md), [OPA / Rego](opa-adapter.md),
  [Cedar](cedar-adapter.md) — the shipped adapters used as worked examples above.
