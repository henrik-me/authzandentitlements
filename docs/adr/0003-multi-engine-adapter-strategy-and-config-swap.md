# 0003. Multi-engine adapter strategy + config-driven swap

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS05–CS09, CS20

## Status

Accepted.

## Context

A core goal of this repository is to *evaluate* fine-grained authorization engines against a
realistic fintech workload, not merely to pick one. That requires running several engines —
a native reference, ASP.NET Core policies, Casbin, Cedar, OPA/Rego, OpenFGA — against the same
scenarios and being able to move between them cheaply. With the unified seam
([ADR 0001](0001-unified-authzen-aligned-pdp-abstraction.md)) and an oracle
([ADR 0002](0002-reference-engine-as-parity-oracle.md)) in place, we needed a strategy for how
engines plug in, how one is selected, and how a swap is *trusted* before it goes live.

## Decision

Adopt an **adapter-per-engine** strategy behind the PDP seam, selected purely by configuration:

- Each engine ships an `IAuthorizationDecisionProvider` with a unique, stable, lowercase
  `Name` (`reference`, `aspnet`, `casbin`, `cedar`, `opa`, `openfga`), registered in
  [`AddPdp`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs).
- The active engine is the `Pdp:Provider` config value, resolved case-insensitively by
  [`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs).
  There is exactly one engine-agnostic call site; **no app code changes to swap engines**. A
  blank value falls back to `reference`; an unknown non-blank name fails closed (throws /
  `400`), never a silent wrong-engine answer.
- Two proven integration styles: **full-decision** engines (OPA, Cedar) own the entire fintech
  decision in their own policy language; **role-gate-only** engines (ASP.NET Core, Casbin)
  natively decide just the RBAC dimension via `IEngineRoleAuthorizer` and delegate the rest to
  the shared
  [`FintechRuleEvaluator`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/FintechRuleEvaluator.cs).
- A swap is only trusted after a **shadow / dual-run** gate: `ShadowRunner.RunCatalog` compares
  a candidate against the trusted engine across the whole catalog and requires `AllAgree` (also
  exposed at `POST /api/authz/shadow/catalog`). The gate is proven non-vacuous — it catches a
  deliberately drifting engine.
- Out-of-process / container engines (OPA, OpenFGA) are opt-in: options bind and clients build
  lazily, and the Aspire AppHost starts them with `WithExplicitStart()` and no hard `WaitFor`,
  so the deterministic Docker-free default is undisturbed.

## Consequences

**Positive**

- New engines plug in without touching business code; evaluation is a config change plus a
  parity test.
- The shadow gate turns "trust me, it's equivalent" into evidence before a production swap.
- The Docker-free default keeps builds/tests fast and deterministic.

**Negative / trade-offs**

- Every adapter must be kept at parity with the oracle across the catalog — real ongoing cost.
- The role-gate-only path only natively models the RBAC dimension; the shared evaluator owns
  the contextual rules, so those engines are not exercising their full native power.
- Config-driven selection needs fail-closed discipline (unknown name → deny) to avoid a
  silent wrong-engine answer.

## Alternatives considered

- **Compile-time engine choice (DI wiring per build).** Rejected: it makes A/B evaluation and a
  runtime swap a redeploy, and precludes shadow-running two engines side by side.
- **One engine only.** Rejected: it defeats the repository's evaluation purpose and locks in a
  vendor.
- **Swap without a shadow gate.** Rejected: a green unit test is not evidence that a live swap
  decides identically across the whole catalog.

## When to use / when not

- **Use** an adapter + `Pdp:Provider` to trial or run any supported engine; **use** the
  role-gate-only path when an engine natively models only roles, and the full-decision path when
  it can own the whole decision.
- **Always** clear the shadow / dual-run gate (`AllAgree`) before trusting a swap in a running
  system.
- **Not** a license to add engines casually — each new adapter is a parity and maintenance
  commitment, so add one when it earns its place in the evaluation.
