# OPA / Rego adapter bundle (CS08)

This directory holds the [Open Policy Agent](https://www.openpolicyagent.org/)
policy bundle that backs the **`opa`** engine of the Authz PDP. The Rego policy
is a drop-in alternative to the in-process reference engine: it answers the same
[PDP decision contract](../../docs/authz/pdp-contract.md) and is measured against
the same 22-scenario parity bar.

The in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
stays the deterministic default, so a normal `aspire run` / build / test never
needs Docker or OPA. OPA is opt-in (see [AppHost wiring](#apphost-wiring)).

## Layout

| File | Purpose |
|---|---|
| [`policy/authz.rego`](policy/authz.rego) | `package authz.bank` — the decision policy (parity with the reference engine). |
| [`policy/authz_test.rego`](policy/authz_test.rego) | `opa test` unit tests: the 22 parity scenarios + fail-closed / boundary edges. |
| [`policy/conditions.rego`](policy/conditions.rego) | `package authz.bank.conditions` — an ABAC capability **showcase** (not part of the decision contract). |
| [`policy/conditions_test.rego`](policy/conditions_test.rego) | `opa test` unit tests for the showcase. |

## The decision contract

The C# `OpaDecisionProvider` (`Name = "opa"`) shapes an `AccessRequest` into the
camelCase evaluate wire shape and queries the total `decision` rule:

```
POST {Opa__BaseUrl}/v1/data/authz/bank/decision
Content-Type: application/json

{ "input": <AccessRequest> }
```

`decision` is a **total** rule (a `default` covers every unrecognized action),
so the query always resolves to an object — the unknown-action path fails
closed.

### Input (`input`)

The exact `/api/authz/evaluate` wire shape (camelCase; optional fields may be
absent or null):

```json
{
  "subject":  { "type": "user", "id": "user-teller1", "roles": ["Teller"], "tenant": "CONTOSO" },
  "action":   { "name": "bank.transaction.create" },
  "resource": { "type": "transaction", "tenant": "CONTOSO", "amount": 15000, "makerId": "user-teller1" },
  "context":  { "scopes": ["bank.transactions.write"] }
}
```

### Output (`result.decision`)

```json
{
  "decision": "Permit",
  "reason": "Permit",
  "obligations": ["require_approval"]
}
```

- `decision`: `"Permit"` or `"Deny"`.
- `reason`: exactly one of `Permit`, `MissingScope`, `TenantMismatch`,
  `RoleNotAuthorized`, `SubjectNotMaker`, `MakerEqualsChecker`, `NotPending`,
  `UnknownAction`.
- `obligations`: `[]` for everything except a **permitted
  `bank.transaction.create`**, which is `["require_approval"]` when
  `amount >= 10000` else `["post_immediately"]`.

The ordered checks per action (first failing check wins) mirror
`ReferenceDecisionProvider` exactly; see the
[reference provider semantics](../../docs/authz/pdp-contract.md#reference-provider-semantics)
table for the canonical order.

## The 22-scenario parity bar

`authz_test.rego`'s `test_scenario_*` cases mirror the
[`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
one-for-one: same inputs asserting the same `decision` + `reason` + obligations
the reference engine returns. Additional `test_edge_*` cases add fail-closed and
boundary coverage (missing/blank tenant on either side, missing scope on approve,
account-create's no-scope path, the 9,999 / 10,000 / missing-amount threshold
edges, and extra unknown-action verbs).

## Running the tests

```bash
opa test infra/opa/policy -v
```

Formatting and static checks:

```bash
opa check infra/opa/policy
opa fmt --list infra/opa/policy   # empty output == already formatted
```

## AppHost wiring

The Aspire host
([`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs)) registers an
`opa` container that runs OPA as an out-of-process REST decision server and
**bind-mounts this `policy/` directory** into the container at `/policy`:

```csharp
var opa = builder.AddContainer("opa", "openpolicyagent/opa", "1.18.2-static")
    .WithHttpEndpoint(targetPort: 8181, name: "http")
    .WithBindMount("../../infra/opa/policy", "/policy", isReadOnly: true)
    .WithArgs("run", "--server", "--addr=0.0.0.0:8181", "--log-level=error", "/policy")
    .WithExplicitStart();
```

Its `http` endpoint is injected into the `authz-pdp` project as
`Opa__BaseUrl`. The container uses `.WithExplicitStart()` and has **no**
`WaitFor`, so it stays off the default `aspire run` critical path — the
deterministic reference provider remains the default. To exercise OPA, start the
`opa` container from the Aspire dashboard and set `Pdp__Provider=opa` on
`authz-pdp`.

## Conditions showcase — boundary

[`conditions.rego`](policy/conditions.rego) (`package authz.bank.conditions`) is
a **policy-layer demonstration** of OPA's ABAC reach over richer attributes —
`amount`, `time`, `geo`, `risk`, and customer `tier`. It illustrates the kind of
attribute-based rules OPA makes natural to express.

It is deliberately **not** part of the `authz/bank/decision` contract and is
**not** queried by the C# adapter. The current CS05 PDP request shape carries
only `amount` on the resource (see
[`Resource.cs`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Resource.cs) and
the [contract](../../docs/authz/pdp-contract.md)); wiring subject location,
request time, risk scoring, and customer tier end-to-end is a **future
clickstop**. The showcase's richer input shape is local to that module.
