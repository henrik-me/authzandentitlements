# Keto adapter

> **Scope:** how the `keto` engine adapter plugs an out-of-process
> [Ory Keto](https://www.ory.sh/keto) relationship-based (ReBAC / Zanzibar) authorization engine into
> the unified PDP. It documents the shipped adapter in
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): the Ory Permission Language
> (OPL) namespace model it defines, how a Keto permission check maps back onto `AccessDecision`, its
> dual-port (read / write) REST design, provider selection, and the fail-closed posture. Read the
> [PDP contract](pdp-contract.md) first — this adapter answers the same
> `subject / action / resource / context` → `permit/deny + reasons + obligations` shape as the reference
> engine. Keto is the **third head-to-head ReBAC engine** alongside
> [SpiceDB](spicedb-adapter.md) and OpenFGA; all three model one domain and are seeded from one graph.

## What the Keto adapter is (and why)

[Ory Keto](https://www.ory.sh/keto) is an open-source, Zanzibar-inspired permission server built by
[Ory](https://www.ory.sh/). Like OpenFGA and SpiceDB it answers **relationship** questions — *is this
subject related to this resource by this permission?* — by traversing a graph of relationship tuples,
rather than evaluating attribute/role rules. The adapter
([`KetoProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoProvider.cs)) is an
`IAuthorizationDecisionProvider` named `keto`: it forwards each account-shaped `AccessRequest` to Keto
over **HTTP REST** and maps the reply back onto the shared
[`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs).

The adapter is a **faithful mirror of the
[SpiceDB adapter](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbProvider.cs)** (and,
transitively, the OpenFGA adapter): it models the **same fintech relationship graph** and is seeded
from the **same relationship tuples**
([`RebacSeedTuples`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacSeedTuples.cs)), so
Keto, SpiceDB, and OpenFGA answer the identical account questions. That is deliberate — the three
Zanzibar-style engines are the ReBAC set this project runs side by side, and a fair head-to-head
requires them to model one domain. See the
[ReBAC / Zanzibar survey](../eval/survey/relationship-based-zanzibar.md) for the broader landscape.

## The Keto model (OPL)

Keto defines its namespaces and permissions in the **Ory Permission Language** (OPL) — a TypeScript
subset where each `class` is a namespace, its `related` block lists directly-assigned relations, and
its `permits` block computes permissions with `.includes(...)` (direct membership) and `.traverse(...)`
(follow a relation and evaluate a permission on the target). The model
([`infra/keto/namespaces.keto.ts`](../../infra/keto/namespaces.keto.ts)) is the direct translation of
the SpiceDB schema
([`SpiceDbSchema`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbSchema.cs)) and the
OpenFGA model — the same four fintech relationship categories:

- **ownership** — `account.owner` (a `user` or a `customer` owns the account).
- **RM → customer** — `customer.relationship_manager` feeds `customer.can_view`, which flows to the
  customer's accounts via `account.customer`.
- **branch / region hierarchy** — `branch.manage` inherits `region.manage`, and both flow to accounts
  via `account.branch` (and via `customer.branch`).
- **delegation** — `account.delegate` grants a specific user `can_view` on one account.

`can_view` composes the direct `viewer` grant plus owner / delegate / customer→can_view /
branch→manage; `can_transact` is the tighter set — the direct `transactor` grant plus owner /
customer→can_view. The translation rules from the SpiceDB schema:

| SpiceDB construct | Keto / OPL construct |
|---|---|
| `definition <name>` | `class <name> implements Namespace` (lowercase — the namespace equals the class name verbatim) |
| Directly-assigned `relation` (e.g. `owner`, `delegate`) | a `related` entry of the same name |
| Computed `permission` (e.g. `can_view`) | a `permits` arrow function returning a `\|\|` union |
| `computedUserset` (a direct relation in a union, e.g. `owner`) | `this.related.owner.includes(ctx.subject)` |
| arrow (e.g. `customer->can_view`) | `this.related.customer.traverse((c) => c.permits.can_view(ctx))` |
| A subject that is a whole object (e.g. `customer:acme`) | a **subject set** with an **empty relation** — `{ namespace, object, relation: "" }` |

Because all three engines are seeded from the **shared** `RebacSeedTuples` — the `KetoCheckService`
writes those exact tuples into Keto via the write API — the OPL model is verified to reproduce the
shared forward-check catalog
([`RebacScenarioCatalog.Forward`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacScenarioCatalog.cs))
**identically to SpiceDB and OpenFGA**.

### Subject ids vs. subject sets

A Zanzibar tuple's subject is either a **user** (`user:carol`) or **another object**
(`customer:acme`, `region:emea`, `branch:london`). The
[`KetoCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoCheckService.cs) maps
each accordingly: a user becomes a bare **`subject_id`** (`"carol"`), while a whole-object subject
becomes a **`subject_set`** with an **empty relation** (`{ namespace: "customer", object: "acme",
relation: "" }`). The empty relation is what Keto's OPL `.includes(...)` matches and `.traverse(...)`
follows — this is how Zanzibar *userset rewrites* resolve, and getting it right is what makes the
branch / region / customer hierarchies evaluate the same as they do in SpiceDB.

## The dual-port design

Keto deliberately splits its API across **two HTTP ports**, and the adapter honours that with two
endpoints ([`KetoOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoOptions.cs)):

| Port (default) | API | Adapter use |
|---|---|---|
| `4466` — **read** | permission checks (`/relation-tuples/check`) | `ReadEndpoint` — every forward check |
| `4467` — **write** | relationship mutations (`/admin/relation-tuples`) | `WriteEndpoint` — the one-time seed of `RebacSeedTuples` |

Both must be configured before a live check can run: the seed is written to `WriteEndpoint` during the
idempotent bootstrap, then each check is issued against `ReadEndpoint`. Unlike the SpiceDB adapter
there is **no gRPC / h2c** concern — Keto is plain HTTP REST, so `https://` endpoints are equally valid
and no `Http2UnencryptedSupport` switch is needed. Unlike SpiceDB, the namespace/permission **schema is
loaded by the container** from the bind-mounted OPL file, **not pushed via the API** — so the
adapter's bootstrap only seeds tuples.

## The decision contract

The adapter answers **account-shaped relationship checks only** — the same boundary the SpiceDB and
OpenFGA adapters enforce. The pure
[`KetoRequestMapper`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoRequestMapper.cs) projects
an `AccessRequest` onto a Keto permission check using the **shared** action→relation map
([`RebacActionMap`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacRelations.cs)):

| Bank action | Keto permission |
|---|---|
| `bank.account.read` | `can_view` |
| `bank.transaction.create` (on an `account` resource) | `can_transact` |

A forward check is issued over REST
([`KetoCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoCheckService.cs)) via the
generated [`Ory.Keto.Client`](https://www.nuget.org/packages/Ory.Keto.Client) `PermissionApi`:

```csharp
// GET {ReadEndpoint}/relation-tuples/check?namespace=account&object=acme-checking
//     &relation=can_view&subject_id=rm-anne
var result = await permissionApi.CheckPermissionAsync(
    _namespace: "account",
    _object: "acme-checking",
    relation: "can_view",
    subjectId: "rm-anne",
    subjectSetNamespace: null, subjectSetObject: null, subjectSetRelation: null,
    maxDepth: null,
    cancellationToken: ct);
// result.Allowed == true → Permit; false → Deny.
```

### Reason codes and explanation

The adapter reuses the **shared** ReBAC reason codes
([`RebacReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacReasonCodes.cs) and
[`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs)) — no Keto-specific strings —
so it explains itself the same way as SpiceDB and OpenFGA:

| Outcome | `AccessDecision` | Reason code |
|---|---|---|
| `allowed = true` | `Permit` | `Permit` |
| `allowed = false` | `Deny` | `NoRelationship` |
| unmapped action | `Deny` (at the mapper) | `UnknownAction` |
| non-`account` resource | `Deny` (at the mapper) | `UnsupportedResourceType` |
| blank resource id | `Deny` (at the mapper) | `MissingResourceId` |
| engine unreachable / HTTP error / blank endpoint | `Deny` (fail closed) | `EngineUnavailable` |

Each decision carries a CS16
[`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) with
`Engine = "keto"`, `DeterminingRule = relationship`, and the checked relationship tuple
(`user:rm-anne#can_view@account:acme-checking`) as the `relationship-tuple`
[`PolicyReference`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) — the same
shape SpiceDB and OpenFGA surface, so the three engines' explanations line up in the
[playground](../../src/AuthzEntitlements.Bank.Web/Components/Pages/Playground.razor).

## Fail-closed posture

The contract requires `Deny = 0` to fail closed. Because Keto is out of process, the adapter can fail
for reasons the reference engine cannot — so it maps **any** failure to obtain a well-formed check to a
`Deny` with the shared `EngineUnavailable` reason, **never a permit and never a thrown exception**
through `/api/authz/evaluate`. This happens on a blank endpoint (Keto not configured), a malformed
endpoint, an unreachable server, an HTTP error, or an unrecognized result. The specific cause is
**logged** (sanitized via [`LogSanitizer`](../../src/AuthzEntitlements.ServiceDefaults) — CWE-117);
callers get only the stable, non-sensitive message. The REST clients are built **lazily** (only on
first check), so DI registration and the default deterministic run never touch a server.

## Provider selection

The adapter is registered alongside the reference engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
(the `KetoOptions` binding + the lazy `KetoCheckService` singleton + the `IKetoCheckClient` seam + the
provider). Selection stays config-driven via `Pdp:Provider`, matched case-insensitively by
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs):

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "keto" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require a
live Keto. Configure the engine coordinates under the `Pdp:Keto` section
([`KetoOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoOptions.cs)):

```json
"Pdp": {
  "Keto": {
    "ReadEndpoint": "http://localhost:4466",
    "WriteEndpoint": "http://localhost:4467"
  }
}
```

Both are plain `http://` addresses for the dev container's cleartext REST API; `https://` is accepted
equally (Keto is REST, not gRPC). Keto's dev configuration disables auth, so — unlike SpiceDB's
preshared key — there is no credential to inject; a production Keto behind an authenticating proxy is a
documented follow-on, out of scope for the lab.

## Pre-release client and version skew

`Ory.Keto.Client` ships **only as pre-release** — the adapter pins `0.11.0-alpha.0`, the latest
published version, because there is no stable line. It is an auto-generated OpenAPI client, so its
type/method names (`PermissionApi.CheckPermissionAsync`, `RelationshipApi.CreateRelationshipAsync`,
`KetoCreateRelationshipBody`, `KetoSubjectSet`) are the surface the adapter binds to; an alpha bump may
rename them. The client is deliberately paired with a **pinned server image** (`oryd/keto:v26.2.0` in
[`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs)) so the client/server pair is
reproducible despite the version skew between the two numbering schemes. The adapter's **offline** unit
suite does not depend on the client's wire behaviour at all — it exercises the provider through the
`IKetoCheckClient` seam — so an alpha drift can only affect the env-gated integration path, where it
surfaces as a clear build/deserialize error rather than a silent wrong answer.

## Parity and testing

The adapter is unit-tested **offline-first**, exactly like the SpiceDB and OpenFGA adapters:

- **Mapper tests**
  ([`KetoRequestMapperTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/KetoRequestMapperTests.cs))
  cover each action mapping and every fail-closed boundary (unknown action / non-account / blank id),
  fully offline — `KetoRequestMapper` is a pure function.
- **Provider tests**
  ([`KetoProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/KetoProviderTests.cs)) use a fake
  `IKetoCheckClient` (LRN-038) to force allowed / denied / throwing and assert
  Permit / NoRelationship-Deny / EngineUnavailable-Deny with the right reason codes and explanation —
  plus registration + selection through the CS05 seam. No container.
- **Config tests**
  ([`KetoCheckServiceConfigTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/KetoCheckServiceConfigTests.cs))
  assert that a blank or malformed **read or write** endpoint fails closed with a clear
  `InvalidOperationException` before any network call — the provider turns those into a fail-closed
  deny.
- **Integration test**
  ([`KetoIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/KetoIntegrationTests.cs)) is
  **env-gated**: it soft-skips unless `KETO_TEST_ENDPOINT` (the read port) is set — the write port
  defaults to `http://localhost:4467` and can be overridden with `KETO_WRITE_TEST_ENDPOINT` — so the
  default `dotnet test` stays green with no Docker. When a server is present it runs the **shared**
  `RebacScenarioCatalog.Forward` — the very scenarios the SpiceDB and OpenFGA integration suites run —
  so a green run is a genuine head-to-head.

### Running end to end

1. Start Keto — either the Aspire `keto` container (registered with explicit start:
   [`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs), which bind-mounts
   [`infra/keto`](../../infra/keto) with the OPL model) or directly:

   ```bash
   keto serve --config infra/keto/keto.yaml
   ```

2. Point the PDP at the adapter (the AppHost injects `Pdp__Keto__ReadEndpoint` +
   `Pdp__Keto__WriteEndpoint` automatically when the container runs):

   ```bash
   Pdp__Provider=keto
   ```

3. Fan the request across engines in the
   [AuthZ Playground](../../src/AuthzEntitlements.Bank.Web/Components/Pages/Playground.razor) (select
   `keto`) and compare its decision/explanation against `spicedb` and `openfga` side by side.

## See also

- [PDP contract](pdp-contract.md) — the seam, request/response shapes, reason codes, and obligations.
- [SpiceDB adapter](spicedb-adapter.md) — the gRPC ReBAC counterpart this adapter mirrors.
- [Adding an engine adapter](adding-an-engine-adapter.md) — the step-by-step checklist this adapter
  follows.
- [OPA / Rego adapter](opa-adapter.md) — the out-of-process ABAC counterpart.
- [ReBAC / Zanzibar survey](../eval/survey/relationship-based-zanzibar.md) — the broader engine
  landscape.
