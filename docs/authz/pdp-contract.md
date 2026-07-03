# AuthZEN-aligned PDP contract

> **Scope:** the contract an engine-adapter author (Casbin, OpenFGA, OPA, Cedar) implements to
> plug a fine-grained authorization engine into the unified PDP. It documents the shipped
> `AuthzEntitlements.Authz.Pdp` project: the `IAuthorizationDecisionProvider` seam, the
> request/response wire shape, the reference provider's rules, the provider-selection seam, the
> scenario catalog, and the audit/OTel hooks. See
> [ARCHITECTURE.md](../../ARCHITECTURE.md) "Unified AuthZEN-aligned PDP abstraction" decision and the
> [coarse- vs. fine-grained boundary](../architecture/coarse-vs-fine-boundary.md).

> **Status / scope (CS05).** CS05 ships the *contract* + the in-process *reference provider* + the
> *scenario catalog* + audit/OTel *hooks*. Engine adapters are **CS06–CS09**; the live
> `Audit.Service` ingestion pipeline is **CS13**; the observability stack (collector/dashboards)
> is **CS12**. This document describes only what is in the code today. Anything labelled
> "out of scope for CS05" is owned by a later clickstop.

## Overview

The PDP (Policy Decision Point) answers one question — *may this subject perform this action on
this resource in this context?* — behind a single contract, `IAuthorizationDecisionProvider`.
Every engine (the built-in reference engine and the CS06–CS09 adapters) answers the **same**
question shape and returns the **same** decision shape, so engines swap by configuration without
touching calling code.

The shape mirrors the OpenID **AuthZEN** Access Evaluation API: a request of
**subject / action / resource / context** in, and a self-explaining decision of
**permit-or-deny + reasons + obligations** out.

The PDP is the **fine-grained** gate. It answers the contextual, per-resource question the coarse
edge gateway structurally cannot ("does this caller own *this* account?", "is the checker
different from the maker?", "is the amount over the approval threshold?"). As defence in depth it
**re-checks the coarse OAuth scopes** even though the edge already enforced them — see the
[coarse- vs. fine-grained boundary](../architecture/coarse-vs-fine-boundary.md). The reference
provider deliberately encodes the same rules as `Bank.Api` so the two agree.

## The contract

Every engine implements
[`IAuthorizationDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/IAuthorizationDecisionProvider.cs):

```csharp
public interface IAuthorizationDecisionProvider
{
    // Stable, config-selectable engine name (e.g. "reference"). Matched
    // case-insensitively against PdpOptions.Provider by the factory.
    string Name { get; }

    // Evaluates one access request and returns a self-explaining decision.
    AccessDecision Evaluate(AccessRequest request);
}
```

- **`Name`** is the config-selectable engine id. It is matched **case-insensitively** against the
  `Pdp:Provider` setting by `AuthorizationDecisionProviderFactory` (see
  [Provider selection](#provider-selection-how-cs06cs09-plug-in)). The reference engine's name is
  `reference`; an adapter picks a unique name such as `casbin`, `openfga`, `opa`, or `cedar`.
- **`Evaluate`** is synchronous. The reference provider is a pure, deterministic function of its
  input; an out-of-process adapter may compute asynchronously internally and return the result
  here.

## Request shape

An [`AccessRequest`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessRequest.cs) is four
records: `Subject` + `ActionRequest` (bound to the JSON field `action`) + `Resource` +
`EvaluationContext` (bound to `context`).

### Subject — who is asking

| Field | Type | Meaning | Example |
|---|---|---|---|
| `Type` | `string` | AuthZEN subject type | `"user"` |
| `Id` | `string` | Stable subject id (matched against a transaction's maker) | `"user-teller1"` |
| `Roles` | `IReadOnlyList<string>` | Fintech roles the subject holds (see [Roles](#roles)); JSON array | `["Teller"]` |
| `Tenant` | `string?` | Owning tenant; **null/empty fails closed** on tenant checks | `"CONTOSO"` |
| `Branch` | `string?` | Carried, but not yet evaluated (branch ABAC is deferred) | `null` |

### ActionRequest — what they want to do

| Field | Type | Meaning | Example |
|---|---|---|---|
| `Name` | `string` | One of the well-known verbs in [Actions](#actions) | `"bank.transaction.create"` |

The record is named `ActionRequest` (not `Action`) to avoid clashing with `System.Action`; the
JSON field is `action`.

### Resource — what is being acted on

| Field | Type | Meaning | Example |
|---|---|---|---|
| `Type` | `string` | `"account"`, `"transaction"`, `"tenant"`, or `"branch"` | `"transaction"` |
| `Id` | `string?` | Resource id (optional) | `null` |
| `Tenant` | `string?` | Owning tenant; compared to the subject's tenant | `"CONTOSO"` |
| `Branch` | `string?` | Owning branch; carried, not yet evaluated | `null` |
| `Amount` | `decimal?` | Transaction amount; drives the approval-threshold obligation | `15000` |
| `MakerId` | `string?` | Subject id that created the transaction (maker) | `"user-teller1"` |
| `Status` | `string?` | Transaction status; only `"Pending"` may be approved/rejected | `"Pending"` |

Attributes a rule does not need stay `null`.

### EvaluationContext — request-time facts

| Field | Type | Meaning | Example |
|---|---|---|---|
| `Scopes` | `IReadOnlyList<string>` | Coarse OAuth scopes the PDP re-checks (see [Scopes](#scopes)); JSON array | `["bank.read"]` |

### JSON on the wire

Minimal-API JSON uses **camelCase** property names and serializes **enums as names** (via
`JsonStringEnumConverter`, configured in
[`Program.cs`](../../src/AuthzEntitlements.Authz.Pdp/Program.cs)). The request carries no enum
fields — actions, roles, and scopes are plain strings — so the enum note matters mainly for the
`decision` field in the [response](#response-shape). Optional (`null`) fields may be omitted on
input; reads are case-insensitive.

A concrete request as it arrives at `POST /api/authz/evaluate` (a teller creating a $15,000
transfer as themselves):

```json
{
  "subject": {
    "type": "user",
    "id": "user-teller1",
    "roles": ["Teller"],
    "tenant": "CONTOSO"
  },
  "action": { "name": "bank.transaction.create" },
  "resource": {
    "type": "transaction",
    "tenant": "CONTOSO",
    "amount": 15000,
    "makerId": "user-teller1"
  },
  "context": { "scopes": ["bank.transactions.write"] }
}
```

### Well-known vocabulary

The vocabulary is defined once as string constants so the reference provider and the scenario
catalog agree. Use the **exact** string values below.

#### Actions

Source: [`ActionNames`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ActionNames.cs).

| Constant | String value |
|---|---|
| `AccountRead` | `bank.account.read` |
| `AccountCreate` | `bank.account.create` |
| `TransactionCreate` | `bank.transaction.create` |
| `TransactionApprove` | `bank.transaction.approve` |
| `TransactionReject` | `bank.transaction.reject` |

#### Roles

Source: [`RoleNames`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/RoleNames.cs).

| Constant | String value |
|---|---|
| `Teller` | `Teller` |
| `BranchManager` | `BranchManager` |
| `ComplianceOfficer` | `ComplianceOfficer` |
| `Auditor` | `Auditor` |

#### Scopes

Source: [`ScopeNames`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ScopeNames.cs).

| Constant | String value |
|---|---|
| `Read` | `bank.read` |
| `TransactionsWrite` | `bank.transactions.write` |
| `ApprovalsWrite` | `bank.approvals.write` |

## Response shape

An [`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs) is the
decision plus the reasons that explain it and any obligations to honour on a permit.

| Field | Type | Meaning |
|---|---|---|
| `Decision` | `Decision` enum | `Permit` or `Deny`. **`Deny = 0`**, so the zero value **fails closed** |
| `Reasons` | `IReadOnlyList<Reason>` | Why. `Reasons[0]` is the **primary** reason (parity + audit key on it) |
| `Obligations` | `IReadOnlyList<Obligation>` | Post-decision requirements to honour on a permit (empty on deny) |

A `Reason` is a machine-stable `Code` + a human `Message`; the `Code` is the contract other layers
match on and stays stable even if the wording changes. An `Obligation` is an `Id` plus optional
`Properties` (`string→string`). Every decision carries **at least one** reason (the factory methods
`AccessDecision.Permit` / `AccessDecision.Deny` guarantee it).

A permit with a threshold obligation:

```json
{
  "decision": "Permit",
  "reasons": [
    { "code": "Permit", "message": "Request satisfies all applicable rules." }
  ],
  "obligations": [
    { "id": "require_approval", "properties": null }
  ]
}
```

A deny (cross-tenant read):

```json
{
  "decision": "Deny",
  "reasons": [
    { "code": "TenantMismatch",
      "message": "The subject's tenant does not match the resource's tenant." }
  ],
  "obligations": []
}
```

### Reason codes

Source: [`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs). Codes are shared
by the reference provider and every adapter, cross-referenced to the
[reference provider rules](#reference-provider-semantics).

| Code | Returned when |
|---|---|
| `Permit` | All applicable rules pass — the primary reason on every permit. |
| `MissingScope` | The action's required scope is absent from `Context.Scopes`. |
| `TenantMismatch` | Subject and resource tenant differ, or either is null/empty (fails closed). |
| `RoleNotAuthorized` | The subject lacks the role the action requires. |
| `SubjectNotMaker` | `bank.transaction.create` where `Subject.Id` is not the resource's `MakerId`. |
| `MakerEqualsChecker` | Approve/reject where the checker is the maker (segregation of duties). |
| `NotPending` | Approve/reject where `Resource.Status` is not `"Pending"`. |
| `BranchNotInTenant` | Declared in the vocabulary but **not emitted** by the reference provider; reserved for branch-level ABAC (deferred to a later clickstop). |
| `UnknownAction` | The action is outside `ActionNames` — the fail-closed deny. |

### Obligation ids

Source: [`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs). Attached
only to a **permitted** `bank.transaction.create`, keyed off the `10,000` approval threshold.

| Id | Attached when |
|---|---|
| `require_approval` | `Amount >= 10,000` (at/above threshold): the transaction obliges a second-person approval. |
| `post_immediately` | `Amount < 10,000` (below threshold): the transaction may post without a second approver. |

## Reference provider semantics

[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
(`Name = "reference"`) is the in-process baseline: a pure, deterministic function that **mirrors
the `Bank.Api` enforcement rules**. It encodes the rules locally (it does not depend on
`Bank.Api`):

- `ApprovalThreshold` = `10,000`.
- Maker-eligible roles (may originate a transaction): `Teller`, `BranchManager`,
  `ComplianceOfficer`.
- Checker-eligible roles (may decide an approval): `BranchManager`, `ComplianceOfficer`.
- `Auditor` is read-only (neither maker- nor checker-eligible).
- Only a `"Pending"` transaction may be approved or rejected.
- Tenant match **fails closed**: a missing subject *or* resource tenant is a mismatch.

Each action runs its checks **in order** and returns the first failure's code; if all pass it
permits. The tables below list the ordered checks per action.

### `bank.account.read`

| # | Check | Deny code on failure |
|---|---|---|
| 1 | `bank.read` scope present | `MissingScope` |
| 2 | Subject tenant matches resource tenant | `TenantMismatch` |
| ✓ | otherwise | **Permit** (`Permit`) |

### `bank.account.create`

Role-gated only — **no scope check** (matching the boundary doc: account creation is gated by role
at the service, not by a dedicated scope).

| # | Check | Deny code on failure |
|---|---|---|
| 1 | Subject has the `BranchManager` role | `RoleNotAuthorized` |
| 2 | Subject tenant matches resource tenant | `TenantMismatch` |
| ✓ | otherwise | **Permit** (`Permit`) |

### `bank.transaction.create`

| # | Check | Deny code on failure |
|---|---|---|
| 1 | `bank.transactions.write` scope present | `MissingScope` |
| 2 | Subject has a maker-eligible role | `RoleNotAuthorized` |
| 3 | `Subject.Id` equals the resource's `MakerId` | `SubjectNotMaker` |
| 4 | Subject tenant matches resource tenant | `TenantMismatch` |
| ✓ | otherwise | **Permit** + obligation: `require_approval` if `Amount >= 10,000`, else `post_immediately` |

### `bank.transaction.approve` / `bank.transaction.reject`

| # | Check | Deny code on failure |
|---|---|---|
| 1 | `bank.approvals.write` scope present | `MissingScope` |
| 2 | Subject has a checker-eligible role | `RoleNotAuthorized` |
| 3 | Subject tenant matches resource tenant | `TenantMismatch` |
| 4 | `Resource.Status` is `"Pending"` | `NotPending` |
| 5 | Checker is **not** the maker (`Subject.Id != MakerId`) | `MakerEqualsChecker` |
| ✓ | otherwise | **Permit** (`Permit`) |

Pending is checked **before** segregation of duties, mirroring Bank.Api's `Approval.Decide`,
which rejects an already-decided approval before the maker-equals-checker check. A self-approval
of an already-decided transaction therefore denies `NotPending`.

### Any other action

An action outside `ActionNames` is denied with `UnknownAction` (fail closed) — never permitted.

## Provider selection (how CS06–CS09 plug in)

> **Shipped adapters:** CS06 ships the first two engines against this seam — `aspnet`
> (ASP.NET Core policies) and `casbin` (Casbin.NET RBAC). See
> [adapters-aspnet-casbin.md](adapters-aspnet-casbin.md) for their design and selection.

This is the seam an adapter author implements against. Three steps:

**1. Implement `IAuthorizationDecisionProvider`** with a unique `Name` (e.g. `"casbin"`,
`"openfga"`, `"opa"`, `"cedar"`).

**2. Register it in DI alongside the reference provider.** Add one line to
[`PdpServiceCollectionExtensions.AddPdp`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
— every engine is registered as `IAuthorizationDecisionProvider`, and the factory selects among
them by name:

```csharp
public static IServiceCollection AddPdp(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<PdpOptions>(configuration.GetSection(PdpOptions.SectionName));

    services.AddSingleton<IAuthorizationDecisionProvider, ReferenceDecisionProvider>();
    // CS06-CS09: register each engine adapter here, alongside the reference provider, e.g.
    // services.AddSingleton<IAuthorizationDecisionProvider, CasbinDecisionProvider>();

    services.AddSingleton<AuthorizationDecisionProviderFactory>();
    services.AddSingleton<IPdpDecisionAuditSink, LoggingPdpDecisionAuditSink>();
    services.AddSingleton<PdpDecisionService>();

    return services;
}
```

**3. Select it via config `Pdp:Provider`.** The default is `reference` so builds, tests, and
`aspire run` never depend on an external engine. To activate an adapter, set its `Name` in
[`appsettings.json`](../../src/AuthzEntitlements.Authz.Pdp/appsettings.json):

```json
{
  "Pdp": {
    "Provider": "casbin"
  }
}
```

### Selection is fail-closed

[`AuthorizationDecisionProviderFactory.GetActiveProvider()`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
matches the configured `Pdp:Provider` against each registered provider's `Name`
**case-insensitively**. If no provider matches, it **throws** an `InvalidOperationException` naming
the unknown provider and listing the available ones (rather than silently defaulting to some
engine):

```text
No IAuthorizationDecisionProvider named 'casbin' is registered.
Available providers: [reference]. Set "Pdp:Provider" to one of these.
```

`PdpDecisionService` resolves the active provider **once** at construction, and
[`Program.cs`](../../src/AuthzEntitlements.Authz.Pdp/Program.cs) force-resolves that service at
startup — so a misconfigured `Pdp:Provider` fails fast at boot, not on the first request.

### The parity bar

An adapter **must return the same `Decision` and the same primary reason code
(`Reasons[0].Code`) as the reference provider for every scenario in the
[catalog](#scenario-catalog)**. That parity is exactly what `POST /api/authz/scenarios/verify`
checks — it is the adapter's acceptance test.

## Scenario catalog

[`FintechScenarioCatalog.Scenarios`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
is the engine-agnostic question set, expressed once. Each
[`AuthorizationScenario`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/AuthorizationScenario.cs)
carries an `Id`, a `Description`, a fully built `AccessRequest`, the `Expected` decision, and the
`ExpectedReasonCode`. The same scenario dispatches unchanged to any provider, so engines are
compared apples-to-apples. The catalog covers permit and deny across tenant isolation, role
eligibility, the maker-checker threshold (including the exact-`10,000` boundary), segregation of
duties, subject-is-maker, missing scopes, and the fail-closed unknown-action path.

The catalog currently holds **22** scenarios; treat it as *"the catalog"*, not a frozen count — it
grows as rules are added, and adapters are validated against whatever it contains.

Run it two ways, both in
[`ScenarioCatalogRunner`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/ScenarioCatalogRunner.cs):

```csharp
// Parity-check path (adapters, unit tests): dispatch straight to a provider.
ScenarioRunReport report = ScenarioCatalogRunner.Run(FintechScenarioCatalog.Scenarios, provider);
bool ok = report.AllPassed;
```

A scenario **passes** when the actual `Decision` equals `Expected` **and** the primary reason code
(`Reasons[0].Code`) equals `ExpectedReasonCode`. `ScenarioRunReport` reports `Results`, `AllPassed`,
`Passed`, and `Total`.

The HTTP self-check `POST /api/authz/scenarios/verify` runs the catalog through the active provider
(via `PdpDecisionService`, so the audit + OTel hooks fire on every scenario) and returns **200** when
all pass, **500** otherwise.

## Audit & telemetry hooks (contracts/hooks only)

Per the CS05 plan-review amendment, CS05 wires **hooks**, not a live pipeline.
[`PdpDecisionService`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs) wraps
the selected provider so **exactly one** audit event and **one** span + counter increment fire per
decision. Endpoints and the scenario self-check call `PdpDecisionService`, never a provider
directly, so no decision escapes the hooks.

### Audit

Each decision emits one
[`PdpDecisionAuditEvent`](../../src/AuthzEntitlements.Authz.Pdp/Audit/PdpDecisionAuditEvent.cs) via
[`IPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/IPdpDecisionAuditSink.cs):

| Field | Type | Meaning |
|---|---|---|
| `TimestampUtc` | `DateTimeOffset` | When the decision was made |
| `TraceId` | `string` | Correlating trace id (empty if no active trace) |
| `Provider` | `string` | Active engine `Name` |
| `SubjectId` | `string` | `Subject.Id` |
| `Action` | `string` | Action name |
| `ResourceType` | `string` | `Resource.Type` |
| `ResourceId` | `string?` | `Resource.Id` (null if the request omitted it) |
| `Decision` | `string` | `"Permit"` / `"Deny"` |
| `Reason` | `string` | Primary reason code |
| `Tenant` | `string?` | `Subject.Tenant` |

The default sink,
[`LoggingPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/LoggingPdpDecisionAuditSink.cs),
writes one structured `ILogger` event per decision with every field named. The real, append-only
`Audit.Service` ingestion pipeline is **CS13** (out of scope for CS05); this interface is the seam
CS13 replaces or augments.

### Telemetry

[`PdpTelemetry`](../../src/AuthzEntitlements.Authz.Pdp/Telemetry/PdpTelemetry.cs) exposes an
`ActivitySource` and a `Meter`, both named **`AuthzEntitlements.Authz.Pdp`**:

- **Span:** one `pdp.evaluate` span per decision, tagged `pdp.provider`, `pdp.action`,
  `pdp.decision`, `pdp.reason`.
- **Counter:** `pdp.decisions.total`, tagged `provider`, `action` (normalized to a known verb or `unknown` so caller-supplied action names cannot blow up metric label cardinality), `decision`, `reason`.

`Program.cs` registers this source/meter on the OpenTelemetry pipeline from `ServiceDefaults`. The
observability **stack** that consumes them (collector, Grafana/Prometheus dashboards) is **CS12**
(out of scope for CS05); CS05 only exposes the hooks.

## HTTP surface

The endpoints are defined in
[`PdpEndpoints`](../../src/AuthzEntitlements.Authz.Pdp/Endpoints/PdpEndpoints.cs) (plus the root in
`Program.cs`). All are **anonymous in CS05** — this is an in-process reference host, not yet wired
behind the edge/token pipeline.

| Method & path | Purpose | Request → response | Status codes |
|---|---|---|---|
| `GET /` | Service metadata (name, description, endpoint list) | — → JSON metadata | `200` |
| `GET /api/authz/scenarios` | List the catalog (id, description, expected, expected reason code) | — → JSON array | `200` |
| `POST /api/authz/evaluate` | Evaluate one `AccessRequest` | `AccessRequest` → `AccessDecision` | `200`; `400` on a null/empty body |
| `POST /api/authz/scenarios/verify` | Run the catalog through the active provider | — → per-scenario results + `allPassed`/`passed`/`total` | `200` all pass; `500` otherwise |

`Program.cs` also calls `MapDefaultEndpoints()` from `ServiceDefaults`, which adds the standard
health/liveness endpoints.
