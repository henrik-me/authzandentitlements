# SpiceDB vs. OpenFGA — the ReBAC head-to-head

> **Scope:** a focused comparison of the two Zanzibar-style **relationship-based** engines this
> project runs behind the unified PDP: [OpenFGA](https://openfga.dev/) (`openfga`) and
> [SpiceDB](https://authzed.com/spicedb) (`spicedb`). Both are shipped adapters modeling the **same**
> fintech relationship graph and seeded from the **same** tuples, so this is a like-for-like
> comparison, not a survey. For the broader engine landscape see the
> [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) and the
> [comparison matrix](comparison-matrix.md); for the adapter internals see the
> [SpiceDB adapter](../authz/spicedb-adapter.md) and the OpenFGA provider
> ([`OpenFga`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga)).

## Why a head-to-head at all

OpenFGA and SpiceDB are the two leading open-source implementations of Google's **Zanzibar** model:
authorization as a graph of **relationship tuples**, with a forward **Check** (*does subject have
permission on resource?*) and reverse-index lookups (*who can access X* / *what can Y access*). They
solve the same problem the same way, which makes them the natural pair to run side by side. This repo
does exactly that: the [SpiceDB adapter](../authz/spicedb-adapter.md) is a deliberate mirror of the
OpenFGA adapter, so the **only** variables are the engine, its schema language, and its transport —
the domain, the seed graph, and the questions asked are identical.

Because both are seeded from the shared
[`RebacSeedTuples`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacSeedTuples.cs) and
verified against the shared
[`RebacScenarioCatalog.Forward`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacScenarioCatalog.cs),
a green run on both is proof they agree scenario-for-scenario on one relationship graph.

## At a glance

| Dimension | OpenFGA (`openfga`) | SpiceDB (`spicedb`) |
|---|---|---|
| Origin | CNCF sandbox (Auth0 / Okta lineage) | AuthZed (Zanzibar authors' alumni) |
| Model | Zanzibar / ReBAC | Zanzibar / ReBAC |
| Schema language | JSON authorization model (`type_definitions`, `computedUserset`, `tupleToUserset`) | Native **schema DSL** (`definition`, `relation`, `permission`, arrows) |
| .NET client | [`OpenFga.Sdk`](https://www.nuget.org/packages/OpenFga.Sdk) — REST/HTTP | [`Authzed.Net`](https://www.nuget.org/packages/Authzed.Net) — **gRPC** |
| Transport | HTTP/JSON (default `:8080`) | gRPC / HTTP2 (`:50051`) |
| Auth (dev) | none (open API in the lab container) | **preshared key** (`Authorization: Bearer`) |
| Consistency | `consistency` request modes (e.g. higher-consistency) | **ZedTokens** (zookies) + per-request `Consistency` (this adapter uses `FullyConsistent`) |
| Reverse index | `ListObjects` / `ListUsers` | `LookupResources` / `LookupSubjects` (streamed) |
| Store / model versioning | explicit store + immutable authorization-model ids (pinning — see LRN-031) | schema is written per-store; relationships are revisioned by ZedToken |
| Datastore (lab) | Postgres (shared server) | in-memory (dev container) |
| Adapter | [`OpenFgaProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs) | [`SpiceDbProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbProvider.cs) |

## Schema language: JSON model vs. schema DSL

The starkest surface difference is how the graph is expressed.

**OpenFGA** models the domain as a JSON **authorization model**
([`RebacModel`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacModel.cs)): every type is
an object with `relations`, and computed relations are trees of `union` / `computedUserset` (same-type
rewrite) / `tupleToUserset` (follow a relation to another object, then compute). It is verbose but
uniform, and it is data — the model is written to the store via `WriteAuthorizationModel` and pinned
by an immutable id.

**SpiceDB** models the domain in a purpose-built **schema language**
([`SpiceDbSchema`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbSchema.cs)):

```
definition account {
	relation owner: user | customer
	relation customer: customer
	relation branch: branch
	relation delegate: user
	relation viewer: user
	relation transactor: user
	permission can_view = viewer + owner + delegate + customer->can_view + branch->manage
	permission can_transact = transactor + owner + customer->can_view
}
```

Key language differences the adapters had to bridge:

- **`relation` vs. `permission`.** SpiceDB draws a hard line between a stored `relation` (directly
  assignable) and a computed `permission`. OpenFGA blurs them: a single relation can be BOTH directly
  assignable (`this{}`) AND computed (a `union`). The translation therefore **splits** an OpenFGA
  relation that is both into a SpiceDB base `relation` plus a `permission` — e.g. the direct
  `this{}` self-grant on `account.can_view` becomes a `viewer` relation unioned into the `can_view`
  permission.
- **Arrows vs. tupleToUserset.** SpiceDB's `branch->manage` arrow is the concise form of OpenFGA's
  `tupleToUserset { tupleset: branch, computedUserset: manager }`.
- **Union operator.** SpiceDB's `+` union of relations/arrows in one `permission` line replaces
  OpenFGA's nested `union.child[]` JSON array.

The semantics are equivalent for this domain — the SpiceDB schema is verified to reproduce the OpenFGA
forward-check catalog exactly — but the DSL is materially more readable, while the JSON model is more
machine-friendly (diff, generate, pin).

## Transport: gRPC vs. REST SDK

The [`SpiceDbCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs)
speaks **gRPC** via `Authzed.Net`, constructing a `GrpcChannel` and calling
`SchemaService.WriteSchema`, `PermissionsService.WriteRelationships`, and
`PermissionsService.CheckPermission`. The
[`OpenFgaRebacService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaRebacService.cs)
speaks **REST/JSON** via `OpenFga.Sdk`. Practical consequences:

- **Auth.** SpiceDB's dev server requires a **preshared key** on every call, attached as an
  `Authorization: Bearer <key>` gRPC metadata header via a `CallCredentials` interceptor; the lab
  OpenFGA container is an open HTTP API with no token. Because SpiceDB serves gRPC over **h2c**
  (cleartext HTTP/2) in the lab, the channel pairs insecure transport credentials with per-call bearer
  credentials (`UnsafeUseInsecureChannelCallCredentials`).
- **Idempotent seeding.** SpiceDB's `WriteRelationships` supports a **`TOUCH`** operation
  (create-or-update), so the adapter seeds the whole graph idempotently in one call. OpenFGA's `Write`
  is **not** idempotent and errors on a duplicate, so its adapter reconciles each seed tuple with a
  targeted existence `Read` before writing (see LRN-031). SpiceDB's TOUCH is the simpler bootstrap.
- **Streaming reverse index.** SpiceDB's `LookupResources` / `LookupSubjects` return **server
  streams**; OpenFGA's `ListObjects` / `ListUsers` return a materialized page. (This adapter exposes
  only the forward Check per the PDP contract; the reverse-index directions are engine-native on both.)

## Consistency model

Both engines confront the Zanzibar "new-enemy" problem (a stale read granting access a just-revoked
relationship should deny), and both expose a per-request consistency knob:

- **SpiceDB** implements Zanzibar's zookie directly as **ZedTokens** — an opaque revision token a
  caller passes to bound read staleness. It also offers `FullyConsistent` (read at the freshest
  revision) and `MinimizeLatency`. This adapter uses **`FullyConsistent`** so a freshly-seeded
  relationship is guaranteed visible to the first Check — deterministic.
- **OpenFGA** exposes `consistency` request modes (e.g. `HIGHER_CONSISTENCY`) rather than a
  caller-held revision token; it does not (currently) surface a zookie/zedtoken equivalent (see the
  [survey](survey/relationship-based-zanzibar.md)).

Only the SpiceDB adapter explicitly pins consistency (`FullyConsistent`); the OpenFGA adapter issues
its Check with the SDK/server default consistency (unspecified → minimize-latency), not an explicit
strong-consistency pin. In the lab — a single node with a freshly-seeded graph and no replication lag
— both return identical results, so consistency is not a confound in the head-to-head; but in production SpiceDB's ZedTokens give finer, caller-driven control of the
latency/consistency trade-off.

## Operational trade-offs

- **Datastore.** The lab runs OpenFGA on **Postgres** (a shared server, with a one-shot
  `openfga-migrate` step) and SpiceDB on its **in-memory** datastore (zero migration, ephemeral). In
  production SpiceDB also supports Postgres/CockroachDB/Spanner; the in-memory choice keeps the dev
  container deterministic and dependency-free.
- **Model lifecycle.** OpenFGA's immutable, pinnable authorization-model ids give explicit,
  auditable model versioning (and the per-boot-growth pitfall LRN-031 guards against). SpiceDB writes
  the schema per store and versions the **data** by revision (ZedToken) rather than pinning a model id.
- **Off the default path.** Both are opt-in: their Aspire containers use `.WithExplicitStart()` with
  **no** hard `WaitFor` on `authz-pdp` ([`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs)),
  and both clients are built lazily, so `dotnet build`, `dotnet test`, and `aspire run` stay
  Docker-free on the deterministic reference engine.

## Verdict for this project

For a shipped ReBAC engine the repo runs **OpenFGA** as the primary (CS07); SpiceDB is the
**head-to-head counterpart** (CS26) that proves the abstraction holds across two independent Zanzibar
implementations. Neither is strictly "better" for the fintech account graph — they agree
scenario-for-scenario. SpiceDB's schema DSL is more ergonomic and its ZedToken consistency menu is
richer; OpenFGA's JSON model + immutable model-id pinning is more amenable to GitOps-style versioning
and is the engine this project's ADRs selected
([ADR-0004](../adr/0004-rebac-with-openfga-for-relationships.md)). Running both behind one seam is the
point: the [playground](../../src/AuthzEntitlements.Bank.Web/Components/Pages/Playground.razor) fans a
request across both and shows they decide alike.

## See also

- [SpiceDB adapter](../authz/spicedb-adapter.md) — the `spicedb` adapter internals.
- [Adding an engine adapter](../authz/adding-an-engine-adapter.md) — how a new engine plugs in.
- [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) — OpenFGA, SpiceDB, Keto, Topaz,
  and the broader landscape.
- [Comparison matrix](comparison-matrix.md) — the at-a-glance cross-engine feature matrix.
