# Cerbos adapter

> **Scope:** how the `cerbos` engine adapter plugs an out-of-process
> [Cerbos](https://www.cerbos.dev/) policy decision point into the unified PDP. It documents the
> shipped adapter in [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): the
> decision contract the adapter speaks to Cerbos over gRPC, how Cerbos' output maps back onto
> `AccessDecision`, provider selection, and the fail-closed posture. Read the
> [PDP contract](pdp-contract.md) first — this adapter answers the same
> `subject / action / resource / context` → `permit/deny + reasons + obligations` shape as the
> reference engine.

## What the Cerbos adapter is (and why)

Cerbos is a policy-decision engine that evaluates declarative **YAML/CEL** policies out of process,
exposed over a gRPC (and HTTP) Check API. The adapter
([`CerbosDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosDecisionProvider.cs))
is an `IAuthorizationDecisionProvider` named `cerbos`: it forwards each `AccessRequest` to Cerbos and
maps the reply back onto the shared
[`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs).

Like the [OPA adapter](opa-adapter.md), Cerbos is a **full-decision** engine: the same maker-checker,
segregation-of-duties, tenant-isolation, and approval-threshold rules the in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
encodes in C# are expressed as a declarative Cerbos **resource policy** and evaluated by an engine that
can be updated without redeploying the PDP. This is the head-to-head with OPA over the same fintech
question — Cerbos speaks **gRPC** where OPA speaks REST. The Cerbos policy itself is owned under
[`infra/cerbos/policies`](../../infra/cerbos/policies) (a parallel deliverable of this clickstop).

## The decision contract

The adapter forwards each request as a single-principal, single-resource, single-action
[`CheckResources`](https://docs.cerbos.dev/cerbos/latest/api/index.html) call (built by the pure,
offline-testable
[`CerbosRequestMapper`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosRequestMapper.cs)):

- **Principal:** the subject's `id` and `roles`, plus the well-known fintech attributes the policy
  reads — `tenant` and the coarse `scopes` list (`P.attr.tenant`, `P.attr.scopes`). A role-less
  subject gets a placeholder role so the request is well-formed and still denies any role gate.
- **Resource:** kind `bank`, carrying `tenant`, `amount`, `makerId`, and `status`
  (`R.attr.*`) when present. Only **non-blank** attributes are attached, so the policy's `has(...)`
  guards reflect true presence (a blank tenant fails the same-tenant check, matching the reference
  engine's fail-closed tenant rule). `scopes` is always attached (possibly empty).
- **Action:** the single `AccessRequest.Action.Name` (e.g. `bank.transaction.create`).

The Cerbos `bank` policy computes, per action, an ordered **outcome token** in a CEL variable — the
first failing check's reason code, or `Permit` / `Permit:require_approval` / `Permit:post_immediately`
— mirroring the reference engine's short-circuit ordering. Two mutually-exclusive rules per action
translate that token into `EFFECT_ALLOW` (token starts with `Permit`) / `EFFECT_DENY` (otherwise) and
emit the token via `output.when.ruleActivated`. The adapter reads the single string output and the
allow/deny effect back off the matching result entry.

The wire fields are the well-known vocabulary from the
[PDP contract](pdp-contract.md#well-known-vocabulary):
`subject.{id,roles,tenant}`, `action.name`, `resource.{id,tenant,amount,makerId,status}`, and
`context.scopes`.

### Reason codes

The output token's reason segment is one of the stable
[`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs) the Cerbos policy emits —
the same subset the reference engine emits:
`Permit`, `MissingScope`, `TenantMismatch`, `RoleNotAuthorized`, `SubjectNotMaker`,
`MakerEqualsChecker`, `NotPending`, `SodConflict`, `UnknownAction`. (`ReasonCodes` also declares
`BranchNotInTenant`, reserved for branch-level ABAC and not emitted by either engine.) The adapter
validates the returned code against this bounded vocabulary and **fails closed** on any code outside
it, so an out-of-process engine cannot surface an unbounded reason to callers.

## How Cerbos output maps to `AccessDecision`

| Cerbos result | `AccessDecision` |
|---|---|
| `EFFECT_ALLOW` + output `Permit` | `AccessDecision.Permit(reason)` |
| `EFFECT_ALLOW` + output `Permit:require_approval` | `Permit` + `ObligationIds.RequireApproval` |
| `EFFECT_ALLOW` + output `Permit:post_immediately` | `Permit` + `ObligationIds.PostImmediately` |
| `EFFECT_ALLOW` + output `Permit:<unknown>` | **fail closed** (`Deny` / `ProviderUnavailable`) |
| `EFFECT_DENY` + output `<code>` | `AccessDecision.Deny(new Reason("<code>", …))` (no obligations) |
| `EFFECT_DENY` + **no** output, **unknown** action | `Deny` with `ReasonCodes.UnknownAction` (no rule matched) |
| `EFFECT_DENY` + **no** output, **known** action | **fail closed** (`Deny` / `ProviderUnavailable`) — malformed policy output |

Obligations are only attached on a `Permit`; a **non-null unknown/empty** obligation token **fails
closed** — it can never permit while silently dropping the maker-checker approval requirement. The obligation ids
mirror those the reference engine attaches to a permitted `bank.transaction.create`
([`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs)): `require_approval`
for a transaction at/above the 10,000 approval threshold, `post_immediately` below it.

Every decision carries a CS16
[`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) with
`Engine = "cerbos"`, the normalized determining rule for the reason, and Cerbos-native policy
references (the determining check id `<action-short>.<Reason>` plus the stable resource-policy id
`resource.bank.vdefault`).

> **Policy-reference kind.** The contract's
> [`PolicyReferenceKinds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) has
> no Cerbos-specific kind (OPA has `rego-rule`, Cedar has `cedar-policy`, …). Adding a `cerbos-policy`
> kind is a shared-Contracts change out of this adapter's scope, so both references use the generic
> normalized `rule` kind. A dedicated kind is a documented follow-on.

## Provider selection

The adapter is registered alongside the reference engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
(the `CerbosOptions` + the lazy `CerbosCheckService` client seam + the provider). Selection stays
config-driven via `Pdp:Provider`, matched case-insensitively by
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs):

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "cerbos" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require a
live Cerbos. Configure the engine endpoint under the `Pdp:Cerbos` section
([`CerbosOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosOptions.cs)):

```json
"Pdp": {
  "Cerbos": {
    "Endpoint": "http://localhost:3593"
  }
}
```

Under `aspire run` the AppHost injects `Pdp__Cerbos__Endpoint` automatically once the opt-in `cerbos`
container is started.

### Cleartext gRPC (h2c)

The adapter speaks Cerbos' **cleartext** gRPC (`http://`, HTTP/2 over plaintext — h2c). grpc-dotnet
requires the process to opt into unencrypted HTTP/2 before any h2c call; `CerbosCheckService` sets
`AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` in a static
constructor (in addition to the SDK's `.WithPlaintext()`). An `https://` endpoint fails closed with a
clear message — TLS is a documented follow-on.

## Fail-closed posture

The contract requires `Deny = 0` to fail closed. Because Cerbos is out of process, the adapter can
fail for reasons the reference engine cannot — so it maps **any** failure to obtain a well-formed
`Permit`/`Deny` to a `Deny` with a provider-local `ProviderUnavailable` reason code, never a permit.
This happens on:

- a not-configured endpoint (blank), or a malformed / non-`http://` endpoint,
- an unreachable server or any gRPC transport error,
- an absent result entry for the requested resource,
- an unrecognized reason code (outside the bounded `ReasonCodes` vocabulary),
- a decision/reason inconsistency (a permit carrying a non-`Permit` reason, or a deny carrying
  `Permit`),
- a permit with no policy output token, a permit with an **unknown obligation token** (so a malformed
  obligation can never permit while silently dropping the maker-checker approval requirement), a
  **known** action denied with no output token, or an ambiguous **multi-rule** output.

The client is built **lazily** on first use and the endpoint is validated (well-formed absolute
`http://` URI) before any network call, so a misconfiguration fails closed with an actionable message
rather than a cryptic gRPC/Uri error. `ProviderUnavailable` is **provider-local**: it is deliberately
not part of the shared `ReasonCodes` and never appears in the parity catalog (Cerbos is reachable
there and its policy is total), so it maps to no Bank.Api rule. It exists only so a real outage is a
legible, machine-stable `Deny`.

### Boundary — delegation / on-behalf-of / break-glass (enforced at the factory seam)

The Cerbos `bank` policy models the **base** fintech decision (coarse scopes, role eligibility, tenant
isolation, maker-checker, SoD, and the threshold obligation). The CS19/CS21 **on-behalf-of**
(`Subject.Actor`), **manager→delegate delegation** (`Context.Delegation`), and **break-glass**
(`Context.BreakGlass`) constraints are **not** encoded in the policy, so a delegation-aware request
evaluated against this delegation-*unaware* engine could **fail open** — Cerbos permitting an OBO call
the reference engine denies for a missing delegated scope.

That guard is **not** in this adapter. As of CS45 it lives once, authoritatively, at the
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
seam: the factory wraps every engine that does not declare
[`ISupportsExtendedAuthorizationContext`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ISupportsExtendedAuthorizationContext.cs)
— `cerbos` included — in the fail-closed
[`ExtendedContextGuardProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ExtendedContextGuardProvider.cs),
which **denies** any request carrying `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass`
with the distinct reason `ExtendedContextUnsupported` **before** it reaches this adapter. Because the
guard sits at the factory, it covers the enforced path **and** the shadow / what-if / playground
surfaces, and it protects every current and future non-capable engine with no per-adapter code — so
this adapter deliberately **no longer carries its own** (previously CS26) short-circuit, avoiding two
divergent guards. See
[the PDP contract's extended-authorization boundary](pdp-contract.md#extended-authorization-fail-closed-boundary).

Encoding these constraints into the Cerbos policy is a documented follow-on; until then the `cerbos`
engine is a faithful choice for the base (non-delegated) fintech decision — exactly the 22-scenario
parity bar it satisfies — and it would opt into native support by implementing the capability marker
once the policy honours the extended context.

## Parity and testing

The Cerbos policy targets the same **22-scenario parity bar** the reference engine satisfies
([`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs))
— the two engines must agree scenario-for-scenario on both the `Decision` and the primary reason code.

The adapter's mapping and fail-closed layer are unit-tested fully **offline** (no live Cerbos) via a
fake `ICerbosCheckClient` seam in
[`CerbosDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CerbosDecisionProviderTests.cs),
and the endpoint-validation / h2c bootstrap in
[`CerbosCheckServiceConfigTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CerbosCheckServiceConfigTests.cs).
Because the YAML/CEL policy path is only exercised by a running server, full **policy** parity is
proven by the env-gated
[`CerbosIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/CerbosIntegrationTests.cs),
which soft-skips unless `CERBOS_TEST_ENDPOINT` is set — so the default `dotnet test` stays green with
no container.

### Running end to end

1. Start Cerbos with the policy — either the Cerbos Aspire container (start the `cerbos` resource,
   which is registered with explicit start, then `aspire run`) or directly with Docker:

   ```bash
   docker run --rm -p 3592:3592 -p 3593:3593 \
     -v "$(pwd)/infra/cerbos/policies:/policies" \
     ghcr.io/cerbos/cerbos:0.53.0 server --config=/policies/.cerbos.yaml
   ```

2. Point the PDP at the adapter (and, outside `aspire run`, give it the endpoint):

   ```bash
   Pdp__Provider=cerbos
   Pdp__Cerbos__Endpoint=http://localhost:3593
   ```

3. Run the scenario self-check and expect every scenario to pass:

   ```bash
   POST /api/authz/scenarios/verify   # expect AllPassed
   ```

   Or run the env-gated integration suite directly:

   ```bash
   CERBOS_TEST_ENDPOINT=http://localhost:3593 dotnet test \
     tests/AuthzEntitlements.Authz.Pdp.Tests \
     --filter FullyQualifiedName~CerbosIntegrationTests
   ```
