# AuthZEN-aligned PDP contract

> **Scope:** the contract an engine-adapter author (Casbin, OpenFGA, OPA, Cedar) implements to
> plug a fine-grained authorization engine into the unified PDP. It documents the shipped
> `AuthzEntitlements.Authz.Pdp` project: the `IAuthorizationDecisionProvider` seam, the
> request/response wire shape, the reference provider's rules, the provider-selection seam, the
> scenario catalog, and the audit/OTel hooks. See
> [ARCHITECTURE.md](../../ARCHITECTURE.md) "Unified AuthZEN-aligned PDP abstraction" decision and the
> [coarse- vs. fine-grained boundary](../architecture/coarse-vs-fine-boundary.md). For the policy
> **lifecycle** built on this contract — versioning, what-if simulation, shadow / dual-run
> comparison, drift detection, AuthZEN conformance, and the policy test gate — see
> [policy lifecycle & testing](policy-lifecycle.md) (CS17).

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
| `ExtendedContextUnsupported` | The request carries CS19/CS21 extended-authorization context (`Subject.Actor`, `Context.Delegation`, or `Context.BreakGlass`) and the selected engine does **not** implement `ISupportsExtendedAuthorizationContext`; the shared factory guard denies (fail closed). See [the extended-authorization boundary](#extended-authorization-fail-closed-boundary). |

### Obligation ids

Source: [`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs). Attached
only to a **permitted** `bank.transaction.create`, keyed off the `10,000` approval threshold.

| Id | Attached when |
|---|---|
| `require_approval` | `Amount >= 10,000` (at/above threshold): the transaction obliges a second-person approval. |
| `post_immediately` | `Amount < 10,000` (below threshold): the transaction may post without a second approver. |

## Decision explanation (explainability)

> **CS16 addition.** The rest of this document is CS05 scope; this section documents the CS16
> explainability surface. Full detail — the determining-rule vocabulary, per-engine extraction, and
> the explanation-quality comparison — lives in [explainability.md](explainability.md).

CS16 attaches a first-class, engine-agnostic
[`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) to
**every** `AccessDecision` (permit or deny, every engine), so a decision explains not just *what*
and a reason *code* but *which normalized rule* determined it and *which engine-native artifact*
produced it:

```csharp
public sealed record DecisionExplanation(
    string Engine,
    string DeterminingRule,
    IReadOnlyList<PolicyReference> PolicyReferences,
    string Narrative);

public sealed record PolicyReference(string Kind, string Reference, string? Detail = null);
```

It hangs off `AccessDecision` as an additive, nullable property with a copy-method to enrich:

```csharp
public DecisionExplanation? Explanation { get; init; }
public AccessDecision WithExplanation(DecisionExplanation explanation) => this with { Explanation = explanation };
```

- **An adapter SHOULD attach a rich, engine-native explanation** via `WithExplanation(...)` — the
  determining Cedar policy id, OPA rule id, Casbin policy line, ASP.NET requirement, or OpenFGA
  tuple.
- **The service guarantees a baseline otherwise.**
  [`PdpDecisionService`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs)
  attaches [`DecisionExplanations.Baseline`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanations.cs)
  (normalized from the primary reason code, with a `reason-code` reference) when a provider returns
  none, so **no decision the service returns is ever unexplained**.

The normalized `DeterminingRule` values come from
[`DeterminingRules`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs)
(`all-rules-satisfied`, `scope`, `role`, `tenant`, `subject-is-maker`, `pending-status`,
`segregation-of-duties`, `relationship`, `unknown-action`, `engine-unavailable`); the artifact
`Kind` values come from
[`PolicyReferenceKinds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs)
(`reason-code`, `rule`, `rego-rule`, `cedar-policy`, `casbin-rule`, `aspnet-requirement`,
`relationship-tuple`).

The explanation surfaces on the `POST /api/authz/evaluate` response, in each
`POST /api/authz/scenarios/verify` result, and — flattened — on the [audit event](#audit)
(`DeterminingRule`, `PolicyReferences`, `Narrative`). **UI rendering** (playground / audit explorer)
is **CS15** per the CS16 plan-review amendment; CS16 delivers only the explanation data. See
[explainability.md](explainability.md) for the reason-code → determining-rule table, the per-engine
reference formats, and the explanation-quality comparison.

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
> Later clickstops add `opa` (out-of-process OPA / Rego — see
> [opa-adapter.md](opa-adapter.md)) and `cedar` (in-process Cedar / ABAC, with Amazon
> Verified Permissions as the managed option — see [cedar-adapter.md](cedar-adapter.md)).

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

### Extended-authorization fail-closed boundary

The CS19/CS21 request fields `Subject.Actor` (on-behalf-of), `Context.Delegation` (manager→delegate
grant), and `Context.BreakGlass` (emergency elevation) demand *extended-authorization* semantics: the
decision must be constrained to the human/actor intersection, honour grant expiry, and enforce the
delegation/break-glass invariants. An engine that maps only the human subject would evaluate an
on-behalf-of call by the human's rights alone and could **permit** access the reference engine
**denies** — a silent **fail-open** on an engine swap.

The contract closes this centrally. A provider **declares** that it natively honours the extended
context by implementing the empty marker
[`ISupportsExtendedAuthorizationContext`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ISupportsExtendedAuthorizationContext.cs).
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
wraps every provider that does **not** declare it in the fail-closed
[`ExtendedContextGuardProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ExtendedContextGuardProvider.cs),
which **denies** any request carrying `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass`
with the distinct reason `ExtendedContextUnsupported` — never a permit, never a throw — while passing
every other (non-delegated) request through to the engine unchanged. Capable providers pass through
unwrapped and apply their own semantics; today only the `reference` engine declares the marker.

Because the guard lives at the **factory seam**, it covers the enforced path
([`PdpDecisionService`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs)) **and**
the factory-resolved what-if surfaces (`ShadowRunner`, `WhatIfEvaluator`, `PlaygroundFanoutService`),
for every current and future non-capable engine — with no per-adapter code. The reason is deliberately
distinct from the `ProviderUnavailable` / `EngineUnavailable` outage codes (it contains no
`"unavailable"` substring), so the playground never misclassifies this deliberate semantic boundary as
an engine outage.

**How a new engine opts in:** implement `ISupportsExtendedAuthorizationContext` on the provider **once
it natively honours** on-behalf-of, delegation, and break-glass — i.e. it constrains the decision to
the human/actor intersection, honours grant expiry, and never elevates an integrity invariant. Until
then, leave it unmarked and the factory fails it closed on those requests automatically.

## Out-of-process engine adapter safety

An engine that runs **out-of-process** — and especially a **full-decision** engine that owns the whole
fintech decision — must apply the **subset** of the four safety invariants below that matches its transport
and decision role. Each invariant states its own applicability condition, so an adapter takes only the ones
that apply: the h2c switch is **cleartext-gRPC-specific**, the gRPC-metadata casing rule is
**gRPC-specific** (cleartext or TLS), the response-mapping checklist is **full-decision-specific**, and the
env-gated integration test applies to **every** out-of-process adapter. All four are load-bearing and easy to get silently wrong, and each was
surfaced by a shipped adapter, so they are captured here for the next adapter author instead of being
re-derived from prior adapters and PR review logs. The shipped adapters are SpiceDB and Cerbos
(cleartext-h2c gRPC), Keto (HTTP REST), and Topaz (full-decision over TLS); worked examples are cited by
**file + concept** (not line number, which drifts across edits), with the SpiceDB and `Adapters/Cerbos`
check services as the primary transport-pattern examples.

### Cleartext HTTP/2 (h2c) gRPC

A local dev container is typically reached over cleartext HTTP/2 (h2c) gRPC, but .NET's
`SocketsHttpHandler` refuses HTTP/2-over-cleartext by default. Enable it by setting the
`System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` `AppContext` switch — and set it **before
the first `SocketsHttpHandler` / `GrpcChannel` is constructed** (a **static constructor** on the adapter's
check service is the recommended placement). The ordering is load-bearing: the runtime caches the flag on
first handler construction, so setting it after the first handler/channel exists is silently inert and the
live adapter cannot connect even when the container is running. The
offline suite still passes, so the failure is invisible until a real container is exercised.

Pair the switch with **fail-closed rejection of `https://` endpoints** — the h2c path is
cleartext-only, so a `https://` endpoint is a misconfiguration, not a fallback. Validate the endpoint
with `Uri.TryCreate` (absolute URI) plus an explicit scheme check so a bad endpoint fails closed with
a clear message rather than a confusing transport error.

Worked examples (by concept):
[`SpiceDbCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs)
sets the switch in its static constructor and its `BuildClients` rejects `https://` via `Uri.TryCreate`
+ scheme checks;
[`CerbosCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosCheckService.cs)
mirrors both — the static-constructor switch and the `BuildClient` `https://` rejection.

### Lowercase gRPC metadata / `CallCredentials` keys

Every gRPC metadata / `CallCredentials` key **must be lowercase** (`authorization`, never
`Authorization`) — the gRPC/HTTP2 wire format requires lowercase header keys. A mis-cased key can be
dropped or rejected by the gRPC stack (the exact behavior varies by client), so a correctly-configured
credential (e.g. a SpiceDB preshared key) can fail to authenticate with no error at
configuration time. Because the failure only shows up against a live engine, the recommended enforcement is
an **offline casing regression test** that asserts the header/key casing, so a future edit cannot
silently re-break live auth without a container.

Worked example (by concept):
[`SpiceDbCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs)
adds the credential via `CallCredentials.FromInterceptor`, writing the lowercase `authorization`
metadata key.

### Full-decision fail-closed response-mapping checklist

When the engine owns the whole decision, mapping its response back to `AccessDecision` is a fresh
fail-open surface: an ambiguity that is resolved leniently can **permit** access the reference engine
would deny. A full-decision adapter must **fail closed on every response-mapping ambiguity**. Walk the
checklist for every engine output:

- **Unknown / typo obligation token → deny.** Never drop an unrecognized obligation — dropping a
  permit-obligation (e.g. the maker-checker requirement) would permit without the constraint. Map an
  unknown token to a deny.
- **Known action returning no output row → `ProviderUnavailable`.** Never misclassify a missing output
  as `UnknownAction` (a business "action not modelled" answer); an action the engine *does* know that
  yields no row is an engine/policy fault and must surface as the transient/unavailable class.
- **Multiple / ambiguous output rows → deny.** Never arbitrarily pick one row when the engine returns
  more than one — an ambiguous activation is a deny, not a coin-flip.
- **More generally:** enumerate every way the engine output can be unknown, empty, or ambiguous, and
  fail each closed, with an explicit test per branch.

Worked examples (by concept):
[`CerbosDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosDecisionProvider.cs)
— `TryMapObligations` returns `false` on an unknown obligation token (fail closed), and the
no-output branch maps a known action with no result to the unavailable class rather than
`UnknownAction`;
[`CerbosCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosCheckService.cs)
— `ExtractOutputToken` returns `null` when `outputs.Count != 1`, so a multi-rule activation fails
closed.

### Env-gated integration-test convention

A green offline suite is **necessary but not sufficient** for an out-of-process engine: it proves the
mapping code, not the live wire/policy surface (the engine's policy/schema, its reason codes, and the
ordered checks are never exercised by an offline build or by CI, which runs no engine container).
Every out-of-process adapter therefore carries an **env-gated integration test** keyed on
`<ENGINE>_TEST_ENDPOINT` (e.g. `CERBOS_TEST_ENDPOINT`, `SPICEDB_TEST_ENDPOINT`) that **soft-skips when
the variable is unset** — so the offline suite and CI stay Docker-free and green, while a documented
local run validates the CI-invisible surface against a **pinned** container image. What the live test
must prove depends on the engine class:

- **Full-decision adapter:** `Decision` + primary reason-code parity with the reference provider
  across the scenario catalog.
- **ReBAC adapter:** live schema / seed / relationship-check semantics (the PDP reason mapping stays
  covered by the offline suite).

Worked examples (by concept): each soft-skips unless its endpoint variable is set —
[`CerbosIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CerbosIntegrationTests.cs)
(`CERBOS_TEST_ENDPOINT`),
[`SpiceDbIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/SpiceDbIntegrationTests.cs)
(`SPICEDB_TEST_ENDPOINT`),
[`KetoIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/KetoIntegrationTests.cs)
(`KETO_TEST_ENDPOINT`, plus `KETO_WRITE_TEST_ENDPOINT` for the write port), and
[`TopazIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/TopazIntegrationTests.cs)
(`TOPAZ_TEST_ENDPOINT`).

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
| `DeterminingRule` | `string` | Normalized determining rule (CS16 — see [Decision explanation](#decision-explanation-explainability)) |
| `PolicyReferences` | `IReadOnlyList<string>` | Engine-native references flattened as `"kind:reference"` (CS16) |
| `Narrative` | `string` | Human-readable "why" (CS16) |

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
