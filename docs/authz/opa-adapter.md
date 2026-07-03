# OPA / Rego adapter

> **Scope:** how the `opa` engine adapter plugs an out-of-process
> [Open Policy Agent](https://www.openpolicyagent.org/) (OPA) / Rego policy into the unified
> PDP. It documents the shipped adapter in
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): the decision contract
> the adapter speaks to OPA, how OPA's output maps back onto `AccessDecision`, provider selection,
> and the fail-closed posture. Read the [PDP contract](pdp-contract.md) first — this adapter answers
> the same `subject / action / resource / context` → `permit/deny + reasons + obligations` shape as
> the reference engine.

## What the OPA adapter is (and why)

OPA is a general-purpose policy engine that evaluates **Rego** policies out of process, exposed over
a REST decision API. The adapter
([`OpaDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Opa/OpaDecisionProvider.cs))
is an `IAuthorizationDecisionProvider` named `opa`: it forwards each `AccessRequest` to OPA and maps
the reply back onto the shared [`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs).

Delegating the rules to Rego demonstrates externalized, **policy-as-code** ABAC: the same
maker-checker, segregation-of-duties, tenant-isolation, and approval-threshold rules the in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
encodes in C# are expressed as declarative Rego and evaluated by an engine that can be updated
without redeploying the PDP. The Rego policy itself is owned under
[`infra/opa/policy`](../../infra/opa/policy) (a parallel deliverable of this clickstop).

## The decision contract

The adapter is a thin, fixed HTTP contract against OPA's data API.

- **Request:** `POST {BaseUrl}/{DecisionPath}` — default
  `POST http://localhost:8181/v1/data/authz/bank/decision` — with the `AccessRequest` serialized in
  camelCase and wrapped in OPA's `input` envelope:

  ```json
  {
    "input": {
      "subject": { "type": "user", "id": "user-teller1", "roles": ["Teller"], "tenant": "CONTOSO" },
      "action": { "name": "bank.transaction.create" },
      "resource": { "type": "transaction", "tenant": "CONTOSO", "amount": 15000, "makerId": "user-teller1" },
      "context": { "scopes": ["bank.transactions.write"] }
    }
  }
  ```

- **Response:** OPA returns the policy's decision rule value under `result`:

  ```json
  {
    "result": {
      "decision": "Permit",
      "reason": "Permit",
      "obligations": ["require_approval"]
    }
  }
  ```

  When the policy is **undefined** for an input, OPA returns `{}` (no `result`). The adapter treats
  that as fail-closed (see [below](#fail-closed-posture)).

The wire fields are the well-known vocabulary from the [PDP contract](pdp-contract.md#well-known-vocabulary):
`subject.{type,id,roles,tenant,branch}`, `action.name`, `resource.{type,id,tenant,branch,amount,makerId,status}`,
and `context.scopes`.

### Reason codes

`result.reason` is exactly one of the stable codes shared with the reference engine
([`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs)):
`Permit`, `MissingScope`, `TenantMismatch`, `RoleNotAuthorized`, `SubjectNotMaker`,
`MakerEqualsChecker`, `NotPending`, `UnknownAction`.

## How OPA output maps to `AccessDecision`

| OPA `result` | `AccessDecision` |
|---|---|
| `decision: "Permit"` | `AccessDecision.Permit(reason, obligations)` |
| `decision: "Deny"` | `AccessDecision.Deny(reason)` (no obligations) |
| `reason: "<code>"` | `Reasons[0]` = `new Reason("<code>", "OPA policy decision: <code>.")` |
| `obligations: ["require_approval"]` | `ObligationIds.RequireApproval` (permit only) |
| `obligations: ["post_immediately"]` | `ObligationIds.PostImmediately` (permit only) |

Obligations are only attached on a `Permit`; unknown obligation strings are dropped. The obligation
ids mirror those the reference engine attaches to a permitted `bank.transaction.create`
([`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs)):
`require_approval` for a transaction at/above the 10,000 approval threshold, `post_immediately`
below it.

## Provider selection

The adapter is registered alongside the reference engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
(a named `HttpClient` + `OpaOptions` + the provider). Selection stays config-driven via
`Pdp:Provider`, matched case-insensitively by
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs):

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "opa" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require a
live OPA. Configure the engine endpoint under the `Opa` section
([`OpaOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Opa/OpaOptions.cs)):

```json
"Opa": {
  "BaseUrl": "http://localhost:8181",
  "DecisionPath": "v1/data/authz/bank/decision",
  "TimeoutSeconds": 5
}
```

## Fail-closed posture

The contract requires `Deny = 0` to fail closed. Because OPA is out of process, the adapter can fail
for reasons the reference engine cannot — so it maps **any** failure to obtain a well-formed
`Permit`/`Deny` to a `Deny` with a provider-local `ProviderUnavailable` reason code, never a permit.
This happens on:

- a transport error (`HttpRequestException`),
- a client-side timeout (`TaskCanceledException`),
- a non-success HTTP status,
- an absent `result` (empty `{}` — policy undefined for the input),
- a missing reason code,
- an unrecognized `decision` string, or
- a JSON parse error.

`ProviderUnavailable` is **provider-local**: it is deliberately not part of the shared `ReasonCodes`
and never appears in the parity catalog, because OPA is reachable there and the Rego policy is total,
so it maps to no Bank.Api rule. It exists only so a real outage is a legible, machine-stable `Deny`.

## Parity and testing

The Rego policy targets the same **22-scenario parity bar** the reference engine satisfies — the two
engines must agree scenario-for-scenario. The policy is unit-tested with OPA's own test runner:

```bash
opa test infra/opa/policy
```

The adapter itself is unit-tested with a fake `HttpMessageHandler` (no live OPA) in
[`OpaDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/OpaDecisionProviderTests.cs),
covering request shaping, response mapping, and every fail-closed path.

### Running end to end

1. Start OPA with the policy — either the OPA Aspire container (start the `opa` resource, which is
   registered with explicit start, then `aspire run`) or directly:

   ```bash
   opa run --server infra/opa/policy
   ```

2. Point the PDP at the adapter:

   ```bash
   Pdp__Provider=opa
   ```

3. Run the scenario self-check and expect every scenario to pass:

   ```bash
   POST /api/authz/scenarios/verify   # expect AllPassed
   ```

> **Note:** the Rego ABAC-conditions showcase (amount / time / geo / risk / tier) is a policy-layer
> capability demonstration **beyond** the current PDP contract — it shows what externalized policy
> can express, and is not part of the 22-scenario parity bar the adapter maps.
