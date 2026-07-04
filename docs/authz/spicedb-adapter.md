# SpiceDB adapter

> **Scope:** how the `spicedb` engine adapter plugs an out-of-process
> [SpiceDB](https://authzed.com/spicedb) relationship-based (ReBAC / Zanzibar) authorization engine
> into the unified PDP. It documents the shipped adapter in
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): the SpiceDB schema it
> models, how a SpiceDB permission check maps back onto `AccessDecision`, provider selection, and the
> fail-closed posture. Read the [PDP contract](pdp-contract.md) first — this adapter answers the same
> `subject / action / resource / context` → `permit/deny + reasons + obligations` shape as the
> reference engine. SpiceDB is the **head-to-head ReBAC counterpart to OpenFGA**; the two are compared
> in depth in [SpiceDB vs. OpenFGA](../eval/spicedb-vs-openfga.md).

## What the SpiceDB adapter is (and why)

[SpiceDB](https://authzed.com/spicedb) is an open-source, Zanzibar-inspired permissions database
built by [AuthZed](https://authzed.com/). Like OpenFGA it answers **relationship** questions — *is
this subject related to this resource by this permission?* — by traversing a graph of relationship
tuples, rather than evaluating attribute/role rules. The adapter
([`SpiceDbProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbProvider.cs)) is
an `IAuthorizationDecisionProvider` named `spicedb`: it forwards each account-shaped `AccessRequest`
to SpiceDB over **gRPC** and maps the reply back onto the shared
[`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs).

The adapter is a **faithful mirror of the
[OpenFGA adapter](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs)**: it
models the **same fintech relationship graph** and is seeded from the **same relationship tuples**, so
SpiceDB and OpenFGA answer the identical account questions. That is deliberate — the two Zanzibar-style
engines are the ReBAC pair this project runs side by side, and a fair head-to-head requires them to
model one domain. See the [ReBAC / Zanzibar survey](../eval/survey/relationship-based-zanzibar.md) for
the broader engine landscape.

## The SpiceDB model

SpiceDB uses its own **schema language** (definitions with `relation`s and computed `permission`s),
where OpenFGA uses a JSON authorization model. The adapter's schema
([`SpiceDbSchema`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbSchema.cs)) is the
direct translation of the OpenFGA model
([`RebacModel`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacModel.cs)) — the same four
fintech relationship categories:

- **ownership** — `account.owner` (a `user` or a `customer` owns the account).
- **RM → customer** — `customer.relationship_manager` feeds `customer.can_view`, which flows to the
  customer's accounts via `account.customer`.
- **branch / region hierarchy** — `branch.manage` inherits `region.manage`, and both flow to accounts
  via `account.branch` and via `customer.branch`.
- **delegation** — `account.delegate` grants a specific user `can_view` on one account.

`can_view` composes the direct `viewer` grant plus owner / delegate / customer→can_view /
branch→manage; `can_transact` is the tighter set — the direct `transactor` grant plus owner /
customer→can_view. The
translation rules from the OpenFGA model:

| OpenFGA construct | SpiceDB construct |
|---|---|
| Directly-assigned `relation` (e.g. `owner`, `delegate`) | `relation` of the same name |
| Computed `relation` (e.g. `can_view`, `branch.manager`) | `permission` (its own `+` union) |
| `tupleToUserset` (e.g. account.can_view via `branch->manager`) | arrow — `branch->manage` |
| `computedUserset` (e.g. account.can_view via `owner`) | direct relation reference in the union |
| A `this{}` self-grant on a computed relation | a base `relation` (e.g. `viewer`/`transactor`) unioned into the permission |

Because both engines are seeded from the **shared**
[`RebacSeedTuples`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacSeedTuples.cs) — the
`SpiceDbCheckService` writes those exact tuples into SpiceDB via `WriteRelationships` — the SpiceDB
schema is verified to reproduce the shared forward-check catalog
([`RebacScenarioCatalog.Forward`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacScenarioCatalog.cs))
**identically to OpenFGA**.

## The decision contract

The adapter answers **account-shaped relationship checks only** — the same boundary the OpenFGA
adapter enforces. The pure
[`SpiceDbRequestMapper`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbRequestMapper.cs)
projects an `AccessRequest` onto a SpiceDB `CheckPermission` using the **shared** action→relation map
([`RebacActionMap`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacRelations.cs)):

| Bank action | SpiceDB permission |
|---|---|
| `bank.account.read` | `can_view` |
| `bank.transaction.create` (on an `account` resource) | `can_transact` |

A forward check is issued over gRPC
([`SpiceDbCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs))
with `Consistency = FullyConsistent`, so a freshly-seeded relationship is visible to the first check:

```csharp
permissionsService.CheckPermissionAsync(new CheckPermissionRequest
{
    Resource = new ObjectReference { ObjectType = "account", ObjectId = "acme-checking" },
    Permission = "can_view",
    Subject = new SubjectReference
    {
        Object = new ObjectReference { ObjectType = "user", ObjectId = "rm-anne" },
    },
    Consistency = new Consistency { FullyConsistent = true },
});
// HasPermission → Permit; NoPermission → Deny.
```

### Reason codes and explanation

The adapter reuses the **shared** ReBAC reason codes
([`RebacReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacReasonCodes.cs) and
[`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs)) — no SpiceDB-specific
strings — so it explains itself the same way as OpenFGA:

| Outcome | `AccessDecision` | Reason code |
|---|---|---|
| `HasPermission` | `Permit` | `Permit` |
| `NoPermission` | `Deny` | `NoRelationship` |
| unmapped action | `Deny` (at the mapper) | `UnknownAction` |
| non-`account` resource | `Deny` (at the mapper) | `UnsupportedResourceType` |
| blank resource id | `Deny` (at the mapper) | `MissingResourceId` |
| engine unreachable / gRPC error / blank endpoint | `Deny` (fail closed) | `EngineUnavailable` |

Each decision carries a CS16 [`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs)
with `Engine = "spicedb"`, `DeterminingRule = relationship`, and the checked relationship tuple
(`user:rm-anne#can_view@account:acme-checking`) as the `relationship-tuple`
[`PolicyReference`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) — the same
shape OpenFGA surfaces, so the two engines' explanations line up in the
[playground](../../src/AuthzEntitlements.Bank.Web/Components/Pages/Playground.razor).

## Fail-closed posture

The contract requires `Deny = 0` to fail closed. Because SpiceDB is out of process, the adapter can
fail for reasons the reference engine cannot — so it maps **any** failure to obtain a well-formed
check to a `Deny` with the shared `EngineUnavailable` reason, **never a permit and never a thrown
exception** through `/api/authz/evaluate`. This happens on a blank endpoint (SpiceDB not configured),
an unreachable server, a gRPC error, or an unrecognized result. The specific cause is **logged**
(sanitized via [`LogSanitizer`](../../src/AuthzEntitlements.ServiceDefaults) — CWE-117); callers get
only the stable, non-sensitive message. The gRPC client is built **lazily** (only on first check), so
DI registration and the default deterministic run never touch a server.

## Provider selection

The adapter is registered alongside the reference engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
(the `SpiceDbOptions` binding + the lazy `SpiceDbCheckService` singleton + the `ISpiceDbCheckClient`
seam + the provider). Selection stays config-driven via `Pdp:Provider`, matched case-insensitively by
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs):

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "spicedb" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require a
live SpiceDB. Configure the engine coordinates under the `Pdp:SpiceDb` section
([`SpiceDbOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbOptions.cs)):

```json
"Pdp": {
  "SpiceDb": {
    "Endpoint": "http://localhost:50051",
    "PresharedKey": "spicedb-dev-key"
  }
}
```

`Endpoint` is a plain `http://` address so the .NET gRPC client uses **h2c** (cleartext HTTP/2) — the
transport the dev container serves. `PresharedKey` is sent as an `Authorization: Bearer <key>` gRPC
metadata header (SpiceDB's `serve --grpc-preshared-key` auth). A TLS SpiceDB (an `https://` endpoint
with `SslCredentials`) is a documented follow-on, out of scope for the lab.

## Parity and testing

The adapter is unit-tested **offline-first**, exactly like the OpenFGA adapter:

- **Mapper tests**
  ([`SpiceDbRequestMapperTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/SpiceDbRequestMapperTests.cs))
  cover each action mapping and every fail-closed boundary (unknown action / non-account / blank id),
  fully offline — `SpiceDbRequestMapper` is a pure function.
- **Provider tests**
  ([`SpiceDbProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/SpiceDbProviderTests.cs)) use
  a fake `ISpiceDbCheckClient` (LRN-038) to force allowed / denied / throwing and assert
  Permit / NoRelationship-Deny / EngineUnavailable-Deny with the right reason codes and explanation —
  plus registration + selection through the CS05 seam. No container.
- **Integration test**
  ([`SpiceDbIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/SpiceDbIntegrationTests.cs))
  is **env-gated**: it soft-skips unless `SPICEDB_TEST_ENDPOINT` (and `SPICEDB_TEST_PRESHARED_KEY`) is
  set, so the default `dotnet test` stays green with no Docker. When a server is present it runs the
  **shared** `RebacScenarioCatalog.Forward` — the very scenarios the OpenFGA integration suite runs —
  so a green run is a genuine head-to-head.

### Running end to end

1. Start SpiceDB — either the Aspire `spicedb` container (registered with explicit start:
   [`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs)) or directly:

   ```bash
   spicedb serve --grpc-preshared-key spicedb-dev-key --datastore-engine memory
   ```

2. Point the PDP at the adapter (the AppHost injects `Pdp__SpiceDb__Endpoint` +
   `Pdp__SpiceDb__PresharedKey` automatically when the container runs):

   ```bash
   Pdp__Provider=spicedb
   ```

3. Fan the request across engines in the
   [AuthZ Playground](../../src/AuthzEntitlements.Bank.Web/Components/Pages/Playground.razor) (select
   `spicedb`) and compare its decision/explanation against `openfga` side by side.

## See also

- [PDP contract](pdp-contract.md) — the seam, request/response shapes, reason codes, and obligations.
- [Adding an engine adapter](adding-an-engine-adapter.md) — the step-by-step checklist this adapter
  follows.
- [OPA / Rego adapter](opa-adapter.md) — the out-of-process ABAC counterpart.
- [SpiceDB vs. OpenFGA](../eval/spicedb-vs-openfga.md) — the head-to-head between the two ReBAC engines.
- [ReBAC / Zanzibar survey](../eval/survey/relationship-based-zanzibar.md) — the broader engine
  landscape.
