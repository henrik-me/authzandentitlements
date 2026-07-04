# Explainability — why allowed / why denied

> **Scope:** how the PDP makes *"why allowed / why denied"* a first-class, engine-agnostic
> output on **every** decision (CS16). It documents the shipped
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp) surface: the normalized
> [`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs)
> record, the determining-rule vocabulary, where the explanation surfaces (the `/evaluate`
> response, the scenario self-check, and the audit event), the central baseline guarantee, and
> how each engine extracts its native determining artifact(s). Read the
> [PDP contract](pdp-contract.md) first — this builds on the same
> `subject / action / resource / context` → `permit/deny + reasons + obligations` shape.

> **Status / scope (CS16).** CS16 delivers the explanation **data**: a normalized, engine-agnostic
> explanation attached to every decision, surfaced in the API response, the scenario verify
> results, and the audit event (for CS13 ingestion). **UI rendering** — the interactive playground
> and the audit explorer that *display* and *compare* explanations — is **CS15** per the plan-review
> R1 amendment; CS16 ships only the data those views consume. The live `Audit.Service` pipeline that
> ingests the audit event remains **CS13**.

## What CS16 adds

Before CS16, an `AccessDecision` carried the decision, its `Reasons`, and any obligations — enough
to know *what* the answer was and a machine-stable *code* for why, but not a normalized,
cross-engine account of *which rule* determined the outcome or *which engine-native policy artifact*
produced it.

CS16 adds a first-class, engine-agnostic
[`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) to
**every** decision — permit or deny, from **every** engine:

```csharp
public sealed record DecisionExplanation(
    string Engine,
    string DeterminingRule,
    IReadOnlyList<PolicyReference> PolicyReferences,
    string Narrative);

// One engine-native artifact that contributed to the decision.
public sealed record PolicyReference(string Kind, string Reference, string? Detail = null);
```

- **`Engine`** — the engine that produced the decision (`"reference"`, `"casbin"`, `"aspnet"`,
  `"opa"`, `"cedar"`, `"openfga"`).
- **`DeterminingRule`** — the *normalized*, engine-agnostic rule that decided the outcome (one of
  the [`DeterminingRules`](#the-normalized-determiningrule-vocabulary) vocabulary), so decisions
  compare across engines.
- **`PolicyReferences`** — the *engine-native* artifact(s) that determined the decision: an OPA rule
  id, a Cedar policy id, a Casbin policy line, an ASP.NET requirement, or an OpenFGA tuple. Each is a
  `Kind` + a stable `Reference` + optional human-readable `Detail`.
- **`Narrative`** — the human-readable "why", taken from the decision's primary reason message.

The explanation is attached to
[`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs) via an additive
nullable property and a `WithExplanation` copy method — the decision, its reasons, and its
obligations are untouched:

```csharp
public DecisionExplanation? Explanation { get; init; }

public AccessDecision WithExplanation(DecisionExplanation explanation) =>
    this with { Explanation = explanation };
```

## The normalized `DeterminingRule` vocabulary

Every engine maps its decision onto exactly one normalized rule from
[`DeterminingRules`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs), so a
deny for the *same reason* reads the same regardless of engine. The exact constant values:

| Constant | Value | Meaning |
|---|---|---|
| `AllRulesSatisfied` | `all-rules-satisfied` | Permit — every applicable rule passed. |
| `Scope` | `scope` | A required coarse scope was absent. |
| `Role` | `role` | The subject lacks the role the action requires. |
| `Tenant` | `tenant` | Subject/resource tenant mismatch (or either blank — fails closed). |
| `SubjectIsMaker` | `subject-is-maker` | The caller is not the transaction's maker. |
| `PendingStatus` | `pending-status` | The target transaction is not `Pending`. |
| `SegregationOfDuties` | `segregation-of-duties` | The checker is the maker. |
| `Relationship` | `relationship` | A ReBAC relationship was present / absent (OpenFGA). |
| `UnknownAction` | `unknown-action` | The action is outside the known vocabulary (fail closed). |
| `EngineUnavailable` | `engine-unavailable` | A provider-local fail-closed outcome (engine error/outage). |

### `RuleForReason` — reason code → determining rule

The normalization lives once in
[`DecisionExplanations.RuleForReason`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanations.cs),
reused by both the central service (for the baseline) and the per-engine adapters, so there is one
mapping rather than several. It maps a decision's **primary reason code** (`Reasons[0].Code`) to a
determining rule:

| ReasonCode | DeterminingRule |
|---|---|
| `Permit` | `all-rules-satisfied` |
| `MissingScope` | `scope` |
| `RoleNotAuthorized` | `role` |
| `TenantMismatch` | `tenant` |
| `BranchNotInTenant` | `tenant` |
| `SubjectNotMaker` | `subject-is-maker` |
| `NotPending` | `pending-status` |
| `MakerEqualsChecker` | `segregation-of-duties` |
| `UnknownAction` | `unknown-action` |
| `NoRelationship` (OpenFGA ReBAC) | `relationship` |
| any other code (e.g. `ProviderUnavailable`, `EngineUnavailable`) | `engine-unavailable` |

`NoRelationship` is matched by string literal (with a comment naming its source) rather than by
importing the provider constant, because `Contracts` must not depend on provider namespaces.
Anything outside the known set falls back to `engine-unavailable` — the fail-closed default —
which each real engine overrides with a richer, engine-native explanation.

## Where it surfaces (data, not UI)

CS16 surfaces the explanation as **data** in three places. Rendering it (a playground view, an
audit explorer) is **CS15**.

### 1. The `/api/authz/evaluate` response

[`PdpEndpoints`](../../src/AuthzEntitlements.Authz.Pdp/Endpoints/PdpEndpoints.cs) returns the whole
`AccessDecision` — including `Explanation` — from `POST /api/authz/evaluate`. A deny now carries
its normalized rule, native artifact(s), and narrative:

```json
{
  "decision": "Deny",
  "reasons": [
    { "code": "TenantMismatch",
      "message": "The subject's tenant does not match the resource's tenant." }
  ],
  "obligations": [],
  "explanation": {
    "engine": "reference",
    "determiningRule": "tenant",
    "policyReferences": [
      { "kind": "rule", "reference": "tenant", "detail": null }
    ],
    "narrative": "The subject's tenant does not match the resource's tenant."
  }
}
```

### 2. The `/api/authz/scenarios/verify` results

The scenario self-check `POST /api/authz/scenarios/verify` includes each result's
`explanation` alongside the actual decision/reason, so the parity report is also an
explanation report across every catalog scenario.

### 3. The audit event (for CS13 ingestion)

[`PdpDecisionAuditEvent`](../../src/AuthzEntitlements.Authz.Pdp/Audit/PdpDecisionAuditEvent.cs)
gains three CS16 fields so the (CS13) audit pipeline can ingest the explanation verbatim:

| Field | Type | Meaning |
|---|---|---|
| `DeterminingRule` | `string` | The normalized determining rule. |
| `PolicyReferences` | `IReadOnlyList<string>` | The engine-native references, each flattened as a `"kind:reference"` string (audit-ingestion-friendly). |
| `Narrative` | `string` | The human narrative. |

The `PolicyReferences` are flattened in
[`PdpDecisionService`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs) as
`$"{p.Kind}:{p.Reference}"` — so an OPA tenant deny records, e.g.,
`["rego-rule:transaction.create.TenantMismatch", "rego-rule:data.authz.bank.decision"]`. (The
optional `Detail` is not included in the flattened audit string.)

## The central guarantee — no decision is ever unexplained

[`PdpDecisionService`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs) — the
single orchestration point every decision path goes through — guarantees an explanation is present
even if a provider attaches none:

```csharp
var explained = decision.Explanation is not null
    ? decision
    : decision.WithExplanation(DecisionExplanations.Baseline(_provider.Name, decision));
```

When a provider returns a decision with no `Explanation`, the service attaches the shared
[`DecisionExplanations.Baseline`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanations.cs):
the determining rule is normalized from the primary reason code, and the single `PolicyReference` is
a `reason-code` reference (`kind = "reason-code"`) carrying that code. So audit, telemetry, and the
returned decision **always** carry a "why", and every real engine enriches on top of that floor.

## Per-engine explanation extraction

Each engine attaches its own `DecisionExplanation` with the exact `PolicyReference.Kind` and
reference-string format below. The kinds are defined in
[`PolicyReferenceKinds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs):
`reason-code`, `rule`, `rego-rule`, `cedar-policy`, `casbin-rule`, `aspnet-requirement`,
`relationship-tuple`.

### Reference engine (`reference`)

[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
owns its own role set (it does not delegate role checks), so it surfaces a **single normalized**
pipeline-rule reference — including for role denials — derived from the primary reason.

- **Kind:** `rule`
- **Reference:** `<normalized-rule>` — the determining rule value, e.g. `tenant`, `scope`,
  `role`, `all-rules-satisfied`.
- **Audit form:** `rule:tenant`.

This deliberately mirrors the normalized `rule:` references the adapters' shared evaluator emits, so
reference and adapter explanations compare cleanly.

### Casbin (`casbin`)

[`CasbinDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Casbin/CasbinDecisionProvider.cs)
owns only the **role gate**; the shared
[`FintechRuleEvaluator`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/FintechRuleEvaluator.cs)
composes the ABAC checks (scope, tenant, subject-is-maker, pending, SoD) as normalized `rule:`
references on top.

- **Role gate — Kind:** `casbin-rule`
- **Reference:** the Casbin policy line the subject matched, `p, <role>, <action>` — e.g.
  `p, BranchManager, bank.account.create`. When no subject role matched, it lists the policy lines
  the action requires instead.
- **ABAC checks — Kind:** `rule` (the normalized pipeline rule, e.g. `rule:tenant`).
- **Audit form (account.create tenant deny):** `casbin-rule:p, BranchManager, bank.account.create`
  then `rule:tenant`.

### ASP.NET Core (`aspnet`)

[`AspNetCorePolicyProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/AspNetCore/AspNetCorePolicyProvider.cs)
likewise owns only the role gate — a genuine `RolesAuthorizationRequirement` (the type behind
`[Authorize(Roles=...)]`) — and the shared evaluator adds the normalized ABAC `rule:` references.

- **Role gate — Kind:** `aspnet-requirement`
- **Reference:** `RolesAuthorizationRequirement[<roles>]` — the requirement rendered with its
  eligible roles, e.g. `RolesAuthorizationRequirement[BranchManager]`. An action with no role
  requirement yields `RolesAuthorizationRequirement[]`.
- **ABAC checks — Kind:** `rule`.
- **Audit form (account.create role deny):**
  `aspnet-requirement:RolesAuthorizationRequirement[BranchManager]`.

### OPA / Rego (`opa`)

[`OpaDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Opa/OpaDecisionProvider.cs)
surfaces the `rule` field the Rego policy
([`infra/opa/policy/authz.rego`](../../infra/opa/policy/authz.rego)) emits, plus the stable
package-path entry point.

- **Kind:** `rego-rule`
- **Rule reference:** `<action-short>.<Reason>` — the determining check id the policy names, e.g.
  `transaction.create.TenantMismatch`, `read.Permit`, `unknown.UnknownAction`. (This mirrors the
  Cedar policy-id scheme so the two compare.)
- **Package reference:** `data.authz.bank.decision` — the Rego decision entry point, **always**
  present so a policy that predates the `rule` field still yields a usable explanation.
- **Audit form:** `rego-rule:transaction.create.TenantMismatch` then
  `rego-rule:data.authz.bank.decision`.

A missing `rule` degrades the explanation (the package path remains) but never fails the decision —
fail-closed applies to *decisions*, not *explanations*.

### Cedar (`cedar`)

[`CedarDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cedar/CedarDecisionProvider.cs)
natively owns the **full** decision, so its trace is the actual determining Cedar policy ids the
engine echoes (see
[`CedarPolicyModel`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cedar/CedarPolicyModel.cs)).

- **Kind:** `cedar-policy`
- **Deny reference(s):** the determining forbid ids, in precedence order (**first = primary /
  first-failing**), e.g. `transaction.create.TenantMismatch`. Each carries its reason code as
  `Detail`.
- **Permit reference(s):** the matched permit id(s) the engine echoes, e.g.
  `transaction.create.Permit` (falling back to the per-action permit id if the engine echoes none).
- **Audit form (deny):** `cedar-policy:transaction.create.TenantMismatch`.

### OpenFGA (`openfga`)

[`OpenFgaProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs)
explains along a **relationship** axis: the tuple it actually Checked (built by
[`OpenFgaRequestMapper`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaRequestMapper.cs)).

- **Kind:** `relationship-tuple`
- **Reference:** `user:<id>#<relation>@account:<id>` — the checked tuple, e.g.
  `user:teller1#can_view@account:acme-checking` (a `bank.account.read` maps to the `can_view`
  relation; `bank.transaction.create` maps to `can_transact`). `Detail` states whether a
  relationship path grants the relation.
- **DeterminingRule:** `relationship` (for both permit and deny).
- **Audit form:** `relationship-tuple:user:teller1#can_view@account:acme-checking`.

A full multi-hop path (via OpenFGA `Expand`) is **not** shipped in CS16 — the checked tuple is the
offline-testable minimum determining artifact for a Check result; the richer trace is a future
scope.

## Explanation-quality comparison

The engines differ in how *natively* and *precisely* they can name what determined a decision. The
table compares the shipped explanation surface per engine.

| Engine | Native artifact (`Kind`) | Determining axis | How native is the trace | Limitations |
|---|---|---|---|---|
| `reference` | `rule` | Normalized pipeline rule | Baseline — the normalized rule *is* the artifact | No engine-native ids; it is the reference the others are compared against. |
| `casbin` | `casbin-rule` (+ `rule`) | Role gate + normalized ABAC | Native for the **role gate** (the matched `p, role, action` policy line) | ABAC checks are the *shared* evaluator's normalized rules, not Casbin's — Casbin only decides role eligibility. |
| `aspnet` | `aspnet-requirement` (+ `rule`) | Role gate + normalized ABAC | Native for the **role gate** (the actual `RolesAuthorizationRequirement`) | Same split as Casbin — ASP.NET owns only the role requirement; ABAC is the shared evaluator's normalized rules. |
| `opa` | `rego-rule` | Full decision | Native — a rule id the **policy itself** emits (`<action-short>.<Reason>`) plus the package path | The rule id is one our policy chooses to emit (it mirrors Cedar's id scheme); it degrades gracefully to just the package path if absent. |
| `cedar` | `cedar-policy` | Full decision | Most native — the **actual determining policy ids** the engine echoes, with forbid **precedence ordering** (first = primary) | Ids are our stable policy ids (set explicitly), not free-form; obligations are computed adapter-side. |
| `openfga` | `relationship-tuple` | Relationship (ReBAC) | Native along the **relationship** axis — the exact tuple Checked | Explains *which relation on which object* was (not) granted, not *which sub-path* — a multi-hop `Expand` trace is future scope. |

**Assessment.** Cedar and OPA give the richest, most engine-native traces because both engines own
the *whole* decision: Cedar surfaces the actual determining policy ids with precedence ordering, and
OPA surfaces a rule id the Rego policy emits (deliberately mirroring Cedar's id scheme) alongside a
stable package-path entry point. The RBAC-family adapters (Casbin, ASP.NET Core) are engine-native
only for the **role gate** — the one question their engine answers — while the shared fintech ABAC
checks (scope, tenant, subject-is-maker, pending, segregation of duties) surface as the *normalized*
`rule:` references the shared evaluator emits; this is honest about the coarse-vs-fine split rather
than dressing shared C# logic up as engine output. OpenFGA explains along a fundamentally different
axis — a *relationship* rather than a rule ladder — naming the exact tuple it Checked; a full
multi-hop path is deliberately deferred. The reference engine is the normalized baseline every other
engine is compared against. In all cases the normalized `DeterminingRule` gives a common axis so the
engine-native detail is *additional* precision, not a prerequisite for comparison.

## Related documents

- [PDP contract](pdp-contract.md) — the request/response shape, reason codes, and the
  [decision-explanation section](pdp-contract.md#decision-explanation-explainability).
- [OPA / Rego adapter](opa-adapter.md) — the `rego-rule` source.
- [Cedar adapter](cedar-adapter.md) — the `cedar-policy` source.
- [ASP.NET Core & Casbin adapters](adapters-aspnet-casbin.md) — the `aspnet-requirement` /
  `casbin-rule` sources.
