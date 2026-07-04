# Relationship-based (Zanzibar-family) engines

> **Scope:** a market-survey deep-dive of the relationship-based access control (ReBAC) engines
> descended from Google's **Zanzibar** paper. It is one lane of the CS23 evaluation; see the
> [comparison matrix](../comparison-matrix.md) for the cross-engine scorecard and the
> [market survey index](../market-survey.md) for the full taxonomy (policy engines, entitlements,
> AuthZEN). OpenFGA is grounded in the repo's own shipped integration; every other engine's facts
> are verified against primary sources (linked per section). Where a fact could not be confirmed
> from a primary source it is marked as such rather than guessed.

Zanzibar is Google's global authorization system: permissions are stored as **relationship tuples**
of the form `object#relation@subject` (e.g. `document:roadmap#viewer@user:anna`), and an
authorization decision is answered by walking the relationship graph from an object's relation to a
subject. Two properties define the family: (1) permissions are *data* (tuples) evaluated against a
*schema*/authorization model rather than compiled policy, and (2) the graph is **reverse-indexable**,
so the system can answer not only "may Anna view this document?" (`Check`) but also "which documents
may Anna view?" and "who may view this document?" (`ListObjects` / `ListSubjects` / `Expand`). The
second property is the standout ReBAC strength that flat RBAC and stateless policy engines
structurally lack.

The engines below all implement some subset of that model. They differ mainly in **consistency**
guarantees (how they solve Zanzibar's "new-enemy" problem — where a stale cached relationship could
leak access after a permission was revoked), **hosting** (self-host vs. managed), **schema DSL**, and
**ecosystem maturity / .NET support**. This document treats each engine with a consistent
sub-structure and ends with a short "How these compare" synthesis.

---

## OpenFGA

**Overview / origin.** OpenFGA is a Zanzibar-inspired, fine-grained authorization engine originally
built by Auth0 (now part of Okta) and open-sourced in 2022. It was accepted into the **CNCF Sandbox**
in September 2022 and has since advanced to **CNCF Incubating** status. It is the engine this
repository actually runs behind its PDP seam, so the characterization below is grounded in the
shipped integration rather than only vendor docs.

**Repo grounding — how we use it.** OpenFGA plugs into the AuthZEN-aligned
[`IAuthorizationDecisionProvider`](../../authz/pdp-contract.md) seam as one selectable engine
(`Pdp:Provider = "openfga"`). `OpenFgaProvider.Evaluate` maps an AuthZEN
subject/action/resource request to a **single forward `Check`** (`subject -> relation -> object`) and
returns a self-explaining Permit/Deny; the checked tuple is surfaced as the "relationship path" in the
decision explanation. It is **fail-closed**: a not-configured (blank API URL), unreachable, or
otherwise-failing engine returns Deny, never a permit or an unhandled 500
(`src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs`). Reverse-index queries
("who can view X" / "what can Y access") are OpenFGA-native and exposed over the repo's
`OpenFgaRebacService` + ReBAC endpoints, beyond the synchronous `Check` contract.

**Data model.** OpenFGA authorization models use **schema 1.1**, expressed either in a DSL or the
JSON API form. The repo holds the hand-authored fintech model as canonical JSON
(`RebacModel.cs`): types `user`, `region`, `branch`, `customer`, `account`, with derived relations
built from `this`, `computedUserset`, and `tupleToUserset` unions — e.g. `account.can_view` composes a
direct grant plus owner/delegate/customer-indirection/branch-manager paths. Relationship tuples take
the `(user, relation, object)` shape in `type:id` form (`RebacSeedTuples.cs`), where the subject may be
a user (`user:carol`) or another object/userset (`customer:acme`, `region:emea`). The repo also ships a
mechanical **RBAC → ReBAC translator** ("roles as usersets"): each role becomes a userset and each
role→permission grant becomes one tuple, proven at parity with the source RBAC in-process
([migration & portability](../../authz/migration-and-portability.md)).

**Consistency.** OpenFGA exposes a per-request `consistency` option with two modes:
`MINIMIZE_LATENCY` (default; may read slightly stale cached data) and `HIGHER_CONSISTENCY`
(bypasses caches for the freshest read), letting a caller trade latency against freshness per call.
Unlike SpiceDB's ZedTokens, OpenFGA does **not** currently expose a Zanzibar-style zookie /
consistency-token that bounds a read to "at least as fresh as" a specific prior write; a
`HIGHER_CONSISTENCY` read is its mitigation for the stale-cache ("new-enemy") window.

**Reverse-index / listing.** Provides `Check`, `Expand`, `ListObjects` ("which objects of type T does
user U have relation R on?"), and `ListUsers` (the inverse). These are the ReBAC-defining queries the
repo surfaces through its ReBAC service/endpoints.

**API / SDKs incl. .NET.** HTTP/JSON and gRPC APIs with an **official .NET SDK** (`OpenFGA.Sdk` on
NuGet, maintained under `openfga/dotnet-sdk`), which the repo's provider consumes (bridging the async
SDK to the synchronous `Evaluate` seam). Official SDKs also exist for Go, JS/TS, Python, Java, and
more.

**Hosting.** Self-hostable server (single binary / container) backed by Postgres or MySQL (in-memory
for tests). A managed option exists as **Okta/Auth0 FGA**, the commercial hosted service built on the
same model.

**Licensing & maturity.** **Apache 2.0**, vendor-neutral **CNCF** governance. Broad adoption and an
active ecosystem make it a low-risk open-source choice; it is the most "standard" of the family and the
one we validated against directly.

**Strengths.** CNCF-neutral governance; mature, official multi-language SDKs incl. .NET; a clean DSL and
the JSON model form that is easy to test offline; tunable consistency; a documented migration path from
RBAC.
**Weaknesses.** Contextual/ABAC conditions are more limited than a full policy engine (the repo keeps
scope/tenant/threshold checks in ABAC-capable engines, not smuggled into tuples); operating a
tuple store at scale is real ops work.
**When to use.** You want a vendor-neutral, standards-track ReBAC engine with first-class .NET support
and an offline-testable model — the default pick for this repo.

**Sources.**
- <https://openfga.dev/> · <https://github.com/openfga/openfga> (Apache 2.0)
- <https://openfga.dev/docs/modeling/consistency> · <https://github.com/openfga/dotnet-sdk>
- <https://www.cncf.io/projects/openfga/> (CNCF status)
- Repo: `Providers/OpenFga/OpenFgaProvider.cs`, `RebacModel.cs`, `RebacSeedTuples.cs`,
  [`migration-and-portability.md`](../../authz/migration-and-portability.md)

---

## SpiceDB

**Overview / origin.** SpiceDB is an open-source, Zanzibar-inspired permissions database built by
**AuthZed**. It is frequently cited as the most faithful open re-implementation of the Zanzibar paper,
including its consistency machinery.

**Data model.** A schema language defines `definition` object types with `relation`s and `permission`s
(computed from relations via `+`, `&`, `-` and arrow `->` traversal). Data is stored as
**relationships** (`resource:id#relation@subject:id`). Caveats add typed, parameterized conditions
(CEL-based) for lightweight ABAC on top of relationships.

**Consistency.** SpiceDB directly implements Zanzibar's **zookie** concept as **ZedTokens**. Callers
choose a consistency level per request: `minimize_latency`, `at_least_as_fresh` (pass a ZedToken),
`at_exact_snapshot`, or `fully_consistent`. This is its explicit mitigation of the new-enemy problem.

**Reverse-index / listing.** `CheckPermission`, `Expand`, `LookupResources` ("which resources can this
subject access?"), and `LookupSubjects` ("who can access this resource?") — a complete reverse-index
surface.

**API / SDKs incl. .NET.** gRPC-first (with an HTTP gateway). AuthZed publishes an **official .NET
client**, `authzed-dotnet` (NuGet `Authzed.Net`), Apache-2.0 licensed, alongside Go, Python, JS/TS, Java,
and Ruby.

**Hosting.** Self-hostable (backed by Postgres, CockroachDB, Cloud Spanner, or MySQL). A managed
offering, **AuthZed Dedicated / Cloud** (and the serverless-style hosted product), is available from the
vendor.

**Licensing & maturity.** Core SpiceDB is **Apache 2.0**; governance is vendor-led (AuthZed) rather than
a foundation. Mature and widely adopted, with an especially strong consistency story.

**Strengths.** Closest to the Zanzibar design; rich consistency controls (ZedTokens); strong
performance/scale story; official .NET SDK.
**Weaknesses.** gRPC-centric API can be a slightly higher integration bar than plain REST; single-vendor
governance (no foundation) is a consideration for some adopters.
**When to use.** You need faithful Zanzibar semantics and fine-grained consistency control, and are
comfortable with a gRPC-first, vendor-stewarded OSS engine.

**Sources.**
- <https://authzed.com/docs/> · <https://github.com/authzed/spicedb> (Apache 2.0)
- <https://authzed.com/docs/spicedb/concepts/consistency> (ZedTokens / consistency)
- <https://github.com/authzed/authzed-dotnet> (official .NET client, Apache 2.0)

---

## Ory Keto

**Overview / origin.** Ory Keto is the authorization server of the open-source **Ory** stack, and was
one of the first open implementations of the Zanzibar model. It focuses squarely on relationship-based
access control.

**Data model.** Access is stored as **relation tuples** (`namespace:object#relation@subject`), grouped
into **namespaces**. Subjects can be direct subjects or **subject sets** (`namespace:object#relation`),
which give it userset semantics. Namespaces are configured (traditionally via config; Ory has iterated on
an Ory Permission Language, OPL, for expressing permission rules).

**Consistency.** Keto is Zanzibar-derived but its consistency guarantees are more limited than SpiceDB's
ZedToken model; it does not expose the same rich per-request snapshot-token consistency menu. Treat its
consistency story as **weaker / less configurable** than SpiceDB or OpenFGA unless verified against the
current docs for a specific version.

**Reverse-index / listing.** Exposes a **Check** API, an **Expand** API (expand the subject tree of an
object's relation), and relationship-query/list APIs. Its reverse-index surface is present but less rich
than the `ListObjects`/`LookupResources` story of OpenFGA/SpiceDB.

**API / SDKs incl. .NET.** REST and gRPC APIs. Ory generates client SDKs from its OpenAPI spec across
many languages; a **.NET/C# client is available** as part of the Ory generated SDKs, though it is a
generated API client rather than a hand-crafted ergonomic SDK.

**Hosting.** Self-hostable (single binary/container). Also available as a managed service via the **Ory
Network** cloud platform, which hosts Keto alongside the other Ory services.

**Licensing & maturity.** **Apache 2.0**, vendor-stewarded (Ory). Mature project and part of a broader,
well-known identity stack; adoption for Keto specifically is smaller than for OpenFGA/SpiceDB.

**Strengths.** Clean Zanzibar-faithful tuple/namespace model; integrates with the wider Ory identity
stack; simple to stand up.
**Weaknesses.** Thinner consistency controls and reverse-index ergonomics than OpenFGA/SpiceDB; .NET
support is a generated client.
**When to use.** You already run the Ory stack (Kratos/Hydra/Oathkeeper) and want a matching ReBAC
service, or you want a lightweight Zanzibar tuple store.

**Sources.**
- <https://www.ory.sh/keto/> · <https://github.com/ory/keto> (Apache 2.0)
- <https://www.ory.sh/docs/keto/> (relation tuples, namespaces, check/expand APIs)

---

## Topaz / Aserto

**Overview / origin.** Topaz is an open-source authorization system from **Aserto**. Its distinguishing
design is a **hybrid**: it combines an Open Policy Agent (OPA) policy engine with a **Zanzibar-style
relationship directory**, so decisions can use both Rego policy and relationship data. It also ships as a
low-latency local "edge authorizer" sidecar.

**Data model.** A built-in **directory** stores objects and relationships (Zanzibar-style), with a
manifest describing object types, relations, and permissions. Decisions are made by **OPA/Rego** policies
that can query the directory — blending ReBAC relationship data with policy-as-code (ABAC) in one engine.

**Consistency.** Topaz is typically deployed as a **local sidecar** whose directory data is synced/replicated
to the edge, favouring low-latency local decisions over globally strong, token-based consistency. It does not
market a Zanzibar-zookie-equivalent per-request consistency menu the way SpiceDB does; freshness is a
function of the replication/sync model.

**Reverse-index / listing.** Provides relationship graph queries over the directory (get/traverse relations)
and OPA decision queries. Its reverse-index/listing ergonomics are directory-driven; confirm the exact
`list`-style query capabilities against current docs for a given version.

**API / SDKs incl. .NET.** gRPC/REST APIs. Aserto publishes an **official .NET SDK**
(`aserto-dev/aserto-dotnet`, Apache 2.0) with ASP.NET Core middleware, in addition to Go, JS/TS, Python,
and others.

**Hosting.** Fully self-hostable (Topaz is the open-source core, commonly run as an edge sidecar). Aserto
offers a **managed control plane / SaaS** for policy and directory management on top of Topaz.

**Licensing & maturity.** Topaz core and the SDKs are **Apache 2.0**; governance is vendor-led (Aserto).
Younger and smaller-adoption than OpenFGA/SpiceDB, but notable for the OPA + Zanzibar combination.

**Strengths.** Unifies policy-as-code (Rego/ABAC) with relationship data (ReBAC) in one engine; edge/sidecar
model gives low-latency local decisions; official .NET SDK with ASP.NET Core middleware.
**Weaknesses.** More moving parts (OPA + directory); consistency is replication-driven rather than
token-tunable; smaller community.
**When to use.** You want both policy-as-code and relationship-based checks together, and value a
low-latency local authorizer over globally strong consistency.

**Sources.**
- <https://www.topaz.sh/> · <https://github.com/aserto-dev/topaz> (Apache 2.0)
- <https://github.com/aserto-dev/aserto-dotnet> (official .NET SDK, Apache 2.0)
- <https://docs.aserto.com/> (OPA + directory model, edge authorizer)

---

## Permify

**Overview / origin.** Permify is an open-source, Zanzibar-inspired authorization service focused on a
schema-first developer experience and horizontal scalability.

**Data model.** A schema **DSL** defines `entity` types with `relation`s and `permission`s (permissions
composed from relations with `or`/`and`/`not` and relation traversal). Authorization data is stored as
relationship tuples; **attributes** can be attached for ABAC-style conditions layered on relationships.

**Consistency.** Permify implements Zanzibar-style **Snap Tokens** to provide consistent snapshots for
checks, letting callers trade freshness against latency — conceptually equivalent to Zanzibar zookies /
SpiceDB ZedTokens.

**Reverse-index / listing.** Provides `Check`, `Expand`, and lookup APIs — **LookupEntity** ("which
entities can this subject access?") and **LookupSubject** ("who can access this entity?") — a full
reverse-index surface.

**API / SDKs incl. .NET.** gRPC and REST APIs. Official SDKs are published for **Go, Node/TypeScript,
Python, and Java**. A first-party **.NET SDK is not among the primary official SDKs** at the time of
writing — .NET integration would typically go through the REST/gRPC API or a community client; verify the
current SDK list before relying on native .NET support.

**Hosting.** Self-hostable (single binary/container, backed by Postgres). A managed **Permify Cloud**
offering is available from the vendor.

**Licensing & maturity.** **Apache 2.0**, vendor-led (Permify). A growing project with an active
community; adoption is smaller and younger than OpenFGA/SpiceDB.

**Strengths.** Clean schema-first DSL; full reverse-index API; Snap Token consistency; both OSS and cloud
hosting.
**Weaknesses.** No first-party .NET SDK (as of writing); smaller ecosystem/maturity than the leaders.
**When to use.** You want a schema-first Zanzibar engine with strong lookup APIs and are comfortable
integrating over REST/gRPC from .NET.

**Sources.**
- <https://permify.co/> · <https://github.com/Permify/permify> (Apache 2.0)
- <https://docs.permify.co/> (schema DSL, snap tokens, LookupEntity/LookupSubject)

---

## Warrant (now WorkOS FGA)

**Overview / origin.** Warrant was a Zanzibar-inspired, centralized fine-grained authorization service
exposing relationship "warrants" via check/query APIs. **WorkOS acquired Warrant in 2024**, and the
technology now lives on primarily as **WorkOS FGA**, part of the broader commercial WorkOS platform
(SSO, Directory Sync, etc.). The standalone open-source Warrant offering has been wound down in favour of
the WorkOS product.

**Data model.** Relationship-based: **warrants** are tuples of the form `subject relation object`
(e.g. `user:1 editor document:x`), with object types and relations defined in a schema — the standard
Zanzibar object/relation/subject shape.

**Consistency.** As a Zanzibar-derived system it targets strong/consistent checks; the current WorkOS FGA
consistency guarantees are a managed-service property. Treat precise per-request consistency options as
**not independently verified here** — confirm against current WorkOS FGA docs if it matters.

**Reverse-index / listing.** Provides a **Check** endpoint plus **Query** APIs to list the resources a
subject can access and the subjects related to a resource — the ReBAC reverse-index capability, delivered
as a managed service.

**API / SDKs incl. .NET.** REST/HTTP API. WorkOS publishes SDKs across common languages; a **native .NET
SDK for WorkOS FGA is not clearly advertised** — .NET consumers would typically call the HTTP API directly.
(The legacy open-source Warrant offered its own SDKs.) Verify current .NET support on the WorkOS docs.

**Hosting.** Now primarily a **managed/cloud** offering as part of WorkOS. The legacy open-source Warrant
server was self-hostable; the current productized path is the hosted WorkOS FGA service.

**Licensing & maturity.** The **legacy open-source Warrant was Apache 2.0**. Post-acquisition, **WorkOS
FGA is a commercial platform feature, not a standalone OSS project** — so it is not a self-hosted OSS choice
the way the others in this survey are. Backed by an established vendor (WorkOS).

**Strengths.** Fully managed, low-ops; integrates with the broader WorkOS auth platform; Zanzibar-style
model.
**Weaknesses.** No longer a self-hostable OSS engine; ties you to the WorkOS platform; .NET support is via
raw HTTP rather than a first-party SDK.
**When to use.** You are already adopting WorkOS for auth (SSO/SCIM) and want managed FGA without running
your own tuple store — and OSS/self-host is not a requirement.

**Sources.**
- <https://workos.com/docs/fga> (WorkOS FGA product docs)
- <https://github.com/warrant-dev/warrant> (legacy OSS Warrant, Apache 2.0)
- WorkOS acquisition of Warrant (2024) — WorkOS blog/announcements

---

## How these compare

All six share the Zanzibar core — relationship tuples over a schema, reverse-indexable via
`Check`/`Expand`/list queries — so the differentiators are consistency, hosting, ecosystem, and .NET
support rather than the fundamental model. **OpenFGA** (the engine this repo runs) and **SpiceDB** are the
two mature, foundation- or vendor-backed leaders with first-class **official .NET SDKs**, tunable
consistency (OpenFGA's `consistency` modes; SpiceDB's ZedTokens), and full reverse-index surfaces; OpenFGA's
CNCF-neutral governance and offline-testable JSON model make it the natural default here. **Ory Keto** is a
faithful, lightweight tuple store that shines if you already run the Ory stack, but has thinner consistency
and reverse-index ergonomics. **Topaz/Aserto** is the outlier that fuses OPA policy-as-code with a Zanzibar
directory — the pick when you want ABAC and ReBAC in one engine at the edge. **Permify** offers a clean
schema-first DSL and full lookup APIs with Snap-Token consistency, but lacks a first-party .NET SDK today.
**Warrant/WorkOS FGA** is the managed, no-longer-OSS option that fits teams already standardizing on WorkOS.
For a decision that must run natively on .NET with vendor-neutral governance and an offline-testable model,
the survey points at OpenFGA (with SpiceDB the strong second) — consistent with the repo's shipped choice.
See the [comparison matrix](../comparison-matrix.md) for the scored, side-by-side view and the
[market survey](../market-survey.md) for how these ReBAC engines sit next to policy engines, entitlements,
and AuthZEN.
