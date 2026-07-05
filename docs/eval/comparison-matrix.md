# Comparison matrix

This document is the authoritative, at-a-glance comparison of the authorization and entitlements
engines this project either **integrates in-repo** or **surveyed** as market alternatives. It is
organized on two axes:

- **Dimensions** (rows) — the properties that matter when choosing an engine: authorization
  model, consistency, latency, reverse-index, policy language, testability, auditability, ops
  burden, .NET support, AuthZEN alignment, licensing/maturity, and hosting.
- **Engines** (columns) — the engines integrated behind the unified PDP seam, plus the broader
  set surveyed for the market landscape.

The claims about the **integrated** engines are grounded in shipped code and docs in this
repository — every such cell cites the file or doc that substantiates it. The **surveyed** engines
are summarized here and treated in depth in the linked survey documents; those cells reflect
secondary research, not in-repo measurement.

Primary grounding sources:

- [Performance benchmarks (CS24)](performance-benchmarks.md) — the apples-to-apples latency
  methodology and the committed baseline.
- [AuthZ Playground & Audit Explorer (CS15)](../product/authz-playground-and-audit-explorer.md) —
  the cross-engine fan-out that produces the qualitative parity evidence (`allAgree`).
- [Migration & portability (CS20)](../authz/migration-and-portability.md) — config-driven engine
  swap, RBAC→ReBAC translation, and the shadow / dual-run gate.
- [Managed-vs-self-host TCO + cloud move (CS25)](managed-vs-selfhost-tco.md) — the cost /
  operational-economics and Azure cloud-move view that complements this feature matrix.

## How to read

### Integrated (real in-repo evidence) vs surveyed (secondary research)

Two tiers of evidence back the cells below.

**Integrated authorization engines** answer the same
[`IAuthorizationDecisionProvider`](../authz/pdp-contract.md) contract — the AuthZEN-aligned
`subject / action / resource / context` → `permit/deny + reasons + obligations` shape — and are
verified against the shared 22-scenario `FintechScenarioCatalog`:

- **In-process, Docker-free, deterministic:** `reference`, `aspnet`, `casbin`, `cedar`. These run
  entirely in-process with no container, server, or network, so they are part of the ordinary
  build/test gate.
- **Out-of-process, live, fail-closed-when-down:** `opa` (Rego over a REST decision server) and
  `openfga` (ReBAC / Zanzibar server). These answer only when their explicit-start container is
  running; when unreachable they **fail closed to Deny** and self-skip in benchmarks and tests.

**Integrated entitlements / feature-flag providers** sit behind the OpenFeature client seam:
`InMemory` (the deterministic, tested default) and `Unleash` (config-gated, requires a running
Unleash server; never on the default path) — see
[`FeatureProviderFactory`](../../src/AuthzEntitlements.Entitlements.Service/Features/FeatureProviderFactory.cs).

**Surveyed engines** — OpenFGA, SpiceDB, Cerbos, OPA, Cedar / AVP, Keto, Oso, Topaz, Permify,
Casbin, Warrant / WorkOS, Permit.io, and the entitlements/flags vendors — are compared at summary
granularity here and expanded in the [market survey](market-survey.md) and its sub-documents.

### Latency caveat

The latency cells cite the committed benchmark baseline
([`pdp-latency-baseline.json`](../../src/AuthzEntitlements.Benchmarks/baseline/pdp-latency-baseline.json)).
Per [CS24](performance-benchmarks.md), **absolute latencies are environment-specific** — the
figures below are one machine's numbers from a 2000-iteration / 500-warmup run and are included to
show the **relative** ordering of the in-process engines, not as a portable SLA. The durable
signals are the dashboard trend and the relative regression check, not the raw milliseconds. The
live out-of-process engines (`opa`, `openfga`) add a network/RPC hop that dominates their latency
and is not represented in the in-process baseline; they self-skip when offline.

## Matrix

### Integrated authorization engines (grounded)

Warm percentiles (`p50` / `p95` / `p99`, in ms) and `cold` are from the committed baseline; treat
them as relative, not absolute (see the caveat above).

| Dimension | `reference` | `aspnet` | `casbin` | `cedar` | `opa` | `openfga` |
|---|---|---|---|---|---|---|
| Hosting | In-process | In-process | In-process | In-process | Out-of-process (REST) | Out-of-process (server) |
| Model(s) | RBAC + ABAC | RBAC gate + shared ABAC | RBAC gate + shared ABAC | RBAC + ABAC (full decision in Cedar) | ABAC / policy-as-code (full decision in Rego) | ReBAC (Zanzibar) |
| Consistency | Strong (in-process, synchronous) | Strong (in-process, synchronous) | Strong (in-process, synchronous) | Strong (in-process, synchronous) | Per loaded policy/data bundle (stateless eval) | Per-request `consistency` (`MINIMIZE_LATENCY` / `HIGHER_CONSISTENCY`) |
| Policy language / DSL | C# pipeline | ASP.NET policy requirements | Casbin RBAC model + policy | Cedar policies + entities | Rego | OpenFGA model + relationship tuples |
| Who owns the decision | Whole decision (C#) | Role gate only; ABAC via `FintechRuleEvaluator` | Role gate only; ABAC via `FintechRuleEvaluator` | Whole decision natively | Whole decision natively | Relationship Check |
| Cold latency (ms) | 0.0011 | 0.0004 | 0.0008 | 1.1672 | live (self-skips offline) | live (self-skips offline) |
| Warm p50 / p95 / p99 (ms) | 0.0002 / 0.0023 / 0.0032 | 0.0008 / 0.0035 / 0.0043 | 0.0007 / 0.0035 / 0.0050 | 1.5508 / 3.0665 / 3.4257 | network-bound | network-bound |
| Reverse-index / list-objects | No | No | No | No | No (per-request decision) | **Yes** — `list-objects` / "who can view X" native |
| AuthZEN alignment | Yes (reference shape) | Yes | Yes | Yes | Yes | Yes (via the same seam) |
| Testability | Deterministic, no infra | Deterministic, no infra | Deterministic, no infra | Deterministic, no infra | `opa test` + faked HTTP handler unit tests | In-process parity resolver; live server for e2e |
| Auditability / explanation | `rule` (normalized) | `aspnet-requirement` (+ `rule`) | `casbin-rule` (+ `rule`) | `cedar-policy` (determining ids, precedence) | `rego-rule` (+ package path) | `relationship-tuple` (checked tuple) |
| Ops burden | None | None | None | None (in-proc engine) | Runs an OPA server / policy bundle | Runs an OpenFGA server + store |
| .NET support | First-class (this repo) | Shared framework, no package | `Casbin.NET` package | `MonoCloud.Cedar` package | HTTP client (any) | OpenFGA .NET SDK |
| Licensing / maturity | This repo (license: TBD — see README) | ASP.NET Core (MIT), mature | `Casbin.NET` (Apache-2.0), mature | Cedar (Apache-2.0, AWS), newer — `MonoCloud.Cedar` native .NET bindings | OPA (Apache-2.0), CNCF Graduated | OpenFGA (Apache-2.0), CNCF Incubating |
| Fail-closed when unavailable | N/A (in-proc) | N/A (in-proc) | N/A (in-proc) | Deny on engine error | Deny on transport/timeout/undefined | Deny on unreachable/error |

**Grounding notes for the cells above:**

- **Model / who-owns-the-decision.** `aspnet` and `casbin` are **role-gate-only**: each implements
  `IEngineRoleAuthorizer` and defers every ABAC rule (scope, tenant, subject-is-maker, pending, SoD,
  threshold obligation) to the shared `FintechRuleEvaluator` — see
  [adapters-aspnet-casbin.md](../authz/adapters-aspnet-casbin.md). `cedar` and `opa` express the
  **whole** decision natively in their DSL — see [cedar-adapter.md](../authz/cedar-adapter.md) and
  [opa-adapter.md](../authz/opa-adapter.md). `openfga` maps a request to a single forward `Check`
  (`subject → relation → object`) — see
  [`OpenFgaProvider.cs`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs).
- **Latency.** Numbers are the committed baseline
  ([`pdp-latency-baseline.json`](../../src/AuthzEntitlements.Benchmarks/baseline/pdp-latency-baseline.json)),
  one machine, 2000 iterations. The three RBAC-family in-process engines sit at **sub-microsecond
  to low-microsecond** warm latency; `cedar` is ~1–3 ms warm because it parses and evaluates a real
  policy set in-process. `opa` / `openfga` are network-bound and are **not** in the in-process
  baseline (they self-skip when their server is offline) — see
  [performance-benchmarks.md](performance-benchmarks.md).
- **Reverse-index.** Only `openfga` answers "who can view X / what can Y access" natively via
  `list-objects` reverse queries; the per-request decision engines do not — see
  [`RebacModel.cs`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacModel.cs) and
  [migration-and-portability.md](../authz/migration-and-portability.md).
- **Auditability.** Every engine attaches an engine-native `DecisionExplanation` normalized to a
  common `DeterminingRule` vocabulary — see [explainability.md](../authz/explainability.md) — and
  every enforced decision is forwarded to the tamper-evident hash-chained audit log — see
  [audit-pipeline.md](../authz/audit-pipeline.md).
- **AuthZEN alignment / portability.** All engines are swapped by the single `Pdp:Provider` config
  value with no calling-code change, and a shadow / dual-run gate proves a candidate agrees with the
  incumbent across the whole catalog before a swap is trusted — see
  [migration-and-portability.md](../authz/migration-and-portability.md). The
  [AuthZ Playground](../product/authz-playground-and-audit-explorer.md) fans one request across
  every engine and reports `allAgree`.

### Integrated entitlements / feature-flag providers (grounded)

| Dimension | `InMemory` (OpenFeature) | `Unleash` (OpenFeature) |
|---|---|---|
| Role | Default, deterministic, tested | Config-gated opt-in; never on default path |
| Hosting | In-process catalog | Out-of-process Unleash server |
| Evaluation seam | OpenFeature client (`planTier` context) | OpenFeature provider over `IUnleash.IsEnabled` |
| Fail-closed | Unknown flag → `false` default | Unknown flag → `false` default |
| Ops burden | None | Runs / connects to an Unleash server |

Grounded in
[`OpenFeatureGate.cs`](../../src/AuthzEntitlements.Entitlements.Service/Features/OpenFeatureGate.cs),
[`UnleashFeatureProvider.cs`](../../src/AuthzEntitlements.Entitlements.Service/Features/UnleashFeatureProvider.cs),
and
[`FeatureProviderFactory.cs`](../../src/AuthzEntitlements.Entitlements.Service/Features/FeatureProviderFactory.cs).
Feature evaluation carries the tenant's plan tier and fails closed to `false` on an unknown flag.

### Surveyed engines (market landscape — see survey docs)

Summary granularity; each family is expanded, with strengths / weaknesses / when-to-use, in the
linked survey document. `†` marks an engine also **integrated** in this repo.

| Engine | Family | Model | Consistency | Reverse-index | Hosting | Detail |
|---|---|---|---|---|---|---|
| OpenFGA † | Zanzibar / ReBAC | ReBAC | Per-request `consistency` (`MINIMIZE_LATENCY` / `HIGHER_CONSISTENCY`) | Yes (`list-objects`) | Self-host server | [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) |
| SpiceDB | Zanzibar / ReBAC | ReBAC | New-enemy-safe zedtokens | Yes | Self-host / managed (AuthZed) | [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) |
| Keto | Zanzibar / ReBAC | ReBAC | Tuple store | Yes | Self-host (Ory) | [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) |
| Permify | Zanzibar / ReBAC | ReBAC | Snap-token | Yes | Self-host / managed | [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) |
| Warrant / WorkOS | Zanzibar / ReBAC | ReBAC | Managed | Yes | Managed SaaS | [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) |
| OPA † | Policy engine | ABAC / policy-as-code | Per-request (stateless) | No | Self-host / sidecar | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Cedar / AVP † | Policy engine | RBAC + ABAC | Per-request | No | In-proc / AWS-managed (AVP) | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Cerbos | Policy engine | ABAC / RBAC | Per-request (stateless) | No | Self-host / sidecar | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Oso *(de-scoped)* | Policy engine / library | ABAC / ReBAC (Polar) | Per-request | Partial (Oso Cloud) | Library / managed | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Topaz | Hybrid (OPA + Zanzibar) | ABAC + ReBAC | Directory-backed | Yes | Self-host (Aserto) | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Casbin † | Access-control library | RBAC / ABAC | In-process | No | Library (in-proc) | [Policy & decision engines](survey/policy-and-decision-engines.md) |
| Permit.io | Managed control plane | RBAC + ABAC + ReBAC (over OPA/OpenFGA) | Managed | Yes (via OpenFGA) | Managed SaaS + sidecar | [Policy & decision engines](survey/policy-and-decision-engines.md) |

**Oso is evaluated → de-scoped** from the integration set — it remains *surveyed* (row above), not
integrated: there is no maintained in-process .NET/Polar library, and the only local option is an
unpinnable (`latest`-only), development-only dev-server, with production gated behind paid Oso Cloud.
See [ADR 0008](../adr/0008-oso-descoped-from-expansion-engines.md) (verified 2026-07-04).

Entitlements / feature-flag vendors — OpenMeter, Stigg, OpenFeature (the spec/SDK), Flagsmith,
Unleash `†`, and Microsoft Entra ID Governance — are surveyed in
[entitlements & flags](survey/entitlements-and-flags.md). The AuthZEN standard that unifies the
decision shape used throughout this repo is treated in [AuthZEN](survey/authzen.md).

For the taxonomy, selection guidance, and per-engine strengths / weaknesses / when-to-use, start at
the [market survey index](market-survey.md) and the decisions in the
[ADR index](../adr/README.md).

## Takeaways

- **In-process engines (`reference`, `aspnet`, `casbin`, `cedar`)** are the "lite" profile:
  sub-millisecond to low-single-digit-millisecond, Docker-free, deterministically testable, and
  ABAC-capable. `aspnet` and `casbin` supply only the RBAC role gate and lean on the shared
  `FintechRuleEvaluator` for ABAC; `cedar` expresses the whole decision in its policy DSL in-process.
- **ReBAC (`openfga`)** is the differentiator: it adds the **reverse-index / list-objects**
  capability and models genuine **relationships** — ownership, RM→customer, branch/region
  hierarchy, and delegation — that a flat role list structurally cannot express. The cost is running
  a server and a relationship store. RBAC migrates into it losslessly via the "roles as usersets"
  translation, and the richer relationship value is added on top.
- **Policy engines (`opa`, `cedar` / AVP)** own the **full decision** in an external (or embedded)
  policy DSL, giving the richest, most engine-native explanations and letting policy change without
  redeploying callers — at the cost (for OPA / AVP) of an out-of-process dependency.
- **Managed SaaS (AVP, Warrant / WorkOS, Permit.io, AuthZed)** lowers ops burden and centralizes
  policy/versioning/audit, in exchange for an external dependency and a per-request network call.
- **Portability is the through-line:** every integrated engine answers the same AuthZEN-aligned
  contract, is selected by one config value, is held to the same 22-scenario parity bar, and fails
  **closed** when an out-of-process engine is unreachable — so the choice of engine is a
  configuration and operational trade-off, not an application-code rewrite.
