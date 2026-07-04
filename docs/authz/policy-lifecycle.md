# Policy lifecycle, validation & testing

> **Scope:** how the PDP treats its authorization policy as **code with a lifecycle** — versioned,
> validated in tests, previewed with what-if simulation, compared across engines with shadow /
> dual-run, and watched for drift. It documents the shipped CS17 surface in
> `AuthzEntitlements.Authz.Pdp`: the golden-decision snapshot + drift detection, the shadow-run
> harness, what-if simulation, the AuthZEN conformance mapping, and the policy test suite that
> gates changes. Builds on the [PDP contract](pdp-contract.md), the reference provider, and the
> engine adapters (CS06–CS09).

> **Status / scope (CS17).** CS17 adds the policy *lifecycle + validation* layer on top of the
> CS05 PDP contract and the CS06–CS09 engine adapters. It ships: the golden snapshot + drift
> detection, what-if simulation, the shadow / dual-run comparison harness, AuthZEN Access
> Evaluation conformance, and the golden / negative / property-based test suite. The live audit
> pipeline is **CS13**; the observability stack is **CS12**; the playground UI is **CS15**. This
> document describes only what is in the code today.

## Overview

Authorization policy is **code**: the reference provider's rules, the engine adapters, and the
shared [scenario catalog](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs).
Treating it as code means the same disciplines apply — a versioned known-good baseline, tests that
gate every change, a way to preview a change before it ships, a way to run a candidate engine in
shadow before trusting it, and automated drift detection. CS17 adds those disciplines:

| Concern | Mechanism | Where |
|---|---|---|
| Versioning | Golden-snapshot content hash (`GoldenDecisionSnapshot.Version`) | [`Lifecycle/GoldenDecisionSnapshot.cs`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/GoldenDecisionSnapshot.cs) |
| Validation (CI gate) | Golden / negative / property-based / conformance tests | [`tests/AuthzEntitlements.Authz.Pdp.Tests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests) |
| What-if simulation | `WhatIfEvaluator` + `POST /api/authz/whatif` | [`Lifecycle/WhatIfEvaluator.cs`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/WhatIfEvaluator.cs) |
| Shadow / dual-run | `ShadowRunner` + `POST /api/authz/shadow{,/catalog}` | [`Lifecycle/ShadowRunner.cs`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/ShadowRunner.cs) |
| Drift detection | `GoldenDecisionSnapshot.Diff` + `GET /api/authz/policy/version` | [`Lifecycle/GoldenDecisionSnapshot.cs`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/GoldenDecisionSnapshot.cs) |
| AuthZEN conformance | `AuthZenMapper` + `POST /api/authz/authzen/evaluation` | [`Lifecycle/AuthZen`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/AuthZen) |

## Policy version & the golden snapshot

The [golden snapshot](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/GoldenDecisionSnapshot.cs) is
the **committed, reviewed, known-good decision** for every catalog scenario: the decision, the
primary reason code, and the obligations. It is authored **independently** of the catalog's own
`Expected` fields and of any single engine, on purpose — if a rule change silently moves a
decision, reason, or obligation, the diff catches it even when the catalog expectations were
changed in lock-step.

`GoldenDecisionSnapshot.Version` is a stable SHA-256 hash of the snapshot content — the **policy
version id**. It changes only when the golden baseline changes, so a moved baseline is observable
(pin it, compare it, alert on it).

`GET /api/authz/policy/version` returns the version, the scenario count, the active engine, and a
live **drift** report (`GoldenDecisionSnapshot.Diff(Golden, Compute(activeEngine))`). Empty drift
means the enforced engine still matches the reviewed baseline.

## What-if simulation

`POST /api/authz/whatif` previews what a chosen engine (or the active one) **would** decide for a
hypothetical request, returning the full self-explaining decision (reasons + obligations). It is a
**simulation, not an enforced decision**: it deliberately bypasses `PdpDecisionService`, so a
preview never emits a real authorization-audit event or decision metric.

```jsonc
// POST /api/authz/whatif
{ "engine": "cedar",                 // optional; omit/blank => active engine
  "request": { "subject": { "type": "user", "id": "user-teller1", "roles": ["Teller"], "tenant": "CONTOSO" },
               "action": { "name": "bank.transaction.create" },
               "resource": { "type": "transaction", "tenant": "CONTOSO", "amount": 15000, "makerId": "user-teller1" },
               "context": { "scopes": ["bank.transactions.write"] } } }
// => { "engine": "cedar", "decision": "Permit", "reasons": [...], "obligations": [ { "id": "require_approval" } ] }
```

An unknown engine name fails closed with a `400` naming the available engines — never a wrong-engine
result.

## Shadow / dual-run comparison

The [`ShadowRunner`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/ShadowRunner.cs) evaluates the
**same** input against a primary engine and one or more shadow engines and reports where they
diverge (decision, primary reason, or obligations). This is the "run a candidate engine in shadow
before trusting it" tool — the migration/rollout safety net and the head-to-head parity proof.

- `POST /api/authz/shadow` — one request against a primary (or active) engine + shadow engines.
  Blank `shadows` falls back to the deterministic in-process RBAC family
  (`reference`, `aspnet`, `casbin`, `cedar`).
- `POST /api/authz/shadow/catalog` — one shadow engine against a primary across the **whole**
  scenario catalog, returning per-scenario divergences. `allAgree == true` is the parity verdict a
  migration gate checks.

Obligation comparison is order-insensitive. `OpenFGA` (ReBAC — a different model and catalog) and
`OPA` (needs a live out-of-process server) are **excluded** from the default shadow family so the
comparison stays deterministic and dependency-free; name them explicitly to compare them.

## Validation: the policy test suite (the gate)

Policy changes are gated by the
[PDP test suite](../../tests/AuthzEntitlements.Authz.Pdp.Tests). CS17 adds these layers on top of
the existing per-engine catalog-parity tests:

| Layer | File | What it catches |
|---|---|---|
| Golden / drift | `GoldenDecisionTests.cs` | Any engine's decision/reason/obligations drifting from the reviewed baseline; missing/extra scenarios |
| Property-based | `PolicyInvariantTests.cs` | Non-total/throwing evaluation, permit/deny reason-contract violations, missing threshold obligations, **cross-engine divergence** over a generated request space |
| Shadow harness | `ShadowRunnerTests.cs` | Divergence-detection correctness; full-catalog RBAC parity |
| What-if | `WhatIfEvaluatorTests.cs` | Engine targeting; fail-closed unknown engine |
| AuthZEN conformance | `AuthZenConformanceTests.cs` | Wire-shape attribute extraction/coercion; decision projection; full-catalog round-trip |

Run the gate:

```bash
dotnet test tests/AuthzEntitlements.Authz.Pdp.Tests/AuthzEntitlements.Authz.Pdp.Tests.csproj
```

> **CI note.** This repository's GitHub Actions deliberately run **process gates only**
> (`harness lint` + template-drift); .NET build/test is the **local** code-correctness gate that
> every CS's PR must pass before merge (see `CONTEXT.md`). The policy test suite above **is** that
> gate. A project that wants the suite enforced in GitHub Actions can adopt a path-filtered
> workflow — this is an **opt-in** posture change (left to the maintainer, not enabled by CS17):
>
> ```yaml
> # .github/workflows/policy-tests.yml (adoption example — not enabled by default)
> name: policy-tests
> on:
>   pull_request:
>     branches: [main]
>     paths:
>       - 'src/AuthzEntitlements.Authz.Pdp/**'
>       - 'tests/AuthzEntitlements.Authz.Pdp.Tests/**'
>       - 'infra/opa/policy/**'
>       - 'global.json'
> permissions:
>   contents: read
> jobs:
>   policy-tests:
>     runs-on: ubuntu-latest
>     steps:
>       - uses: actions/checkout@v6            # pin to a SHA to satisfy the workflow-pins gate
>       - uses: actions/setup-dotnet@v5        # reads the SDK from global.json
>         with: { global-json-file: global.json }
>       - run: dotnet test tests/AuthzEntitlements.Authz.Pdp.Tests/AuthzEntitlements.Authz.Pdp.Tests.csproj
> ```

## AuthZEN conformance

The PDP speaks the OpenID **AuthZEN** Authorization API 1.0 "Access Evaluation" wire shape natively
via [`AuthZenMapper`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/AuthZen/AuthZenMapper.cs) and
`POST /api/authz/authzen/evaluation`. The request carries the fintech attributes (roles, tenant,
branch, amount, maker_id, status, scopes) inside the AuthZEN `properties` bags; the response is the
required boolean `decision` plus an explainability `context` (reason code, reason messages,
obligations). Unlike what-if/shadow, this is a **real** decision — it runs through
`PdpDecisionService`, so audit + OTel hooks fire. Conformance is proven by a full-catalog round-trip
test (our request → AuthZEN wire → back → decision matches the golden).

## Rollout & rollback

The golden snapshot is the rollout/rollback anchor:

1. **Author** a policy change (reference rules and/or an engine adapter) on a branch.
2. **Preview** with what-if; **compare** the candidate engine against the trusted one with
   `POST /api/authz/shadow/catalog` (expect `allAgree` unless the change is intentional).
3. **Update** the golden snapshot to the new known-good and **review the diff** — the review of the
   snapshot diff IS the policy-change review. `Version` changes, making the rollout observable.
4. **Gate**: the policy test suite must be green (golden matches every RBAC engine; invariants hold).
5. **Rollback** = revert the golden + rule change; drift detection (`GET /api/authz/policy/version`)
   flags any deployed engine that no longer matches the reviewed baseline.

## Endpoint reference

| Endpoint | Purpose | Enforced? |
|---|---|---|
| `POST /api/authz/whatif` | Preview a decision against a chosen/active engine | No (simulation) |
| `POST /api/authz/shadow` | Compare one request across primary + shadow engines | No (comparison) |
| `POST /api/authz/shadow/catalog` | Compare two engines across the whole catalog | No (comparison) |
| `GET /api/authz/policy/version` | Policy version hash + live drift of the active engine | No (introspection) |
| `POST /api/authz/authzen/evaluation` | AuthZEN-conformant Access Evaluation | Yes (audited) |
