# Policy-as-code / decision engines

> **Scope:** a market-survey deep-dive of the **policy-as-code / decision-engine** family — engines
> whose primary artefact is a *policy* (declarative rules over subject / action / resource / context)
> rather than a *relationship graph*. It is the companion to the
> [ReBAC / Zanzibar survey](relationship-based-zanzibar.md) and feeds the
> [comparison matrix](../comparison-matrix.md) and the [market survey index](../market-survey.md).

Two authorization philosophies dominate this repo's evaluation lab:

- **Policy-as-code / PBAC (this document).** Rules are code — Rego, Cedar, Polar, Casbin matchers,
  Cerbos YAML. A **Policy Decision Point (PDP)** evaluates each `subject / action / resource /
  context` request against those rules and returns *permit / deny (+ reasons + obligations)*. The
  engine is largely **stateless**: the caller supplies the facts (the resource row, the subject's
  roles/attributes) at request time. This family expresses **ABAC**, **RBAC**, and **PBAC** cleanly;
  most members express **ReBAC** only awkwardly, because they do not own a relationship store.
- **Data-driven ReBAC (the [Zanzibar family](relationship-based-zanzibar.md)).** Permissions are
  derived from a persisted graph of relationship tuples the engine itself stores and indexes. That
  is what makes reverse queries ("*which* documents can Alice read?") native there and rare here.

This repo grounds three of these engines in **shipped .NET adapters** behind a single
AuthZEN-aligned [PDP contract](../../authz/pdp-contract.md): the out-of-process
[OPA / Rego adapter](../../authz/opa-adapter.md), the in-process
[Cedar adapter](../../authz/cedar-adapter.md), and the role-gate-only
[Casbin adapter](../../authz/adapters-aspnet-casbin.md). Every engine below is measured against how
well it could plug into that same seam — either as a **full-decision** engine (owns the whole
decision, like OPA and Cedar) or a **role-gate-only** engine (decides role eligibility, with the
shared harness composing the ABAC rules, like Casbin).

Each engine follows the same sub-structure: origin, policy model, decision/PDP model, reverse-index
capability, API/.NET support, hosting, licensing/maturity, and strengths/weaknesses/when-to-use.
Claims beyond what the repo docs substantiate carry a per-engine **Sources** list.

---

## Open Policy Agent (OPA) / Rego

### Overview / origin

OPA is a general-purpose policy engine created by Styra and donated to the CNCF, where it is a
**graduated** project — the highest maturity tier, signalling broad production adoption (Kubernetes
admission control, Envoy/Istio, Terraform, CI/CD gates). It evaluates **Rego** policies and is the
de-facto standard for cloud-native policy-as-code.

### Policy language / model

Policies are written in **Rego**, a declarative, Datalog-derived query language. Rego is
general-purpose: it expresses **ABAC**, **RBAC**, and arbitrary **PBAC** rules, and can encode
relationship-style checks over caller-supplied data (though it does not persist a graph). In this
repo the same fintech rules the in-process reference engine encodes in C# — maker-checker,
segregation-of-duties, tenant isolation, approval thresholds — are re-expressed as declarative Rego
and required to agree scenario-for-scenario (see [`opa-adapter.md`](../../authz/opa-adapter.md)).

### Decision / PDP model

OPA runs **out of process** as a REST decision server; the caller POSTs an `input` envelope and OPA
returns the decision rule's value under `result`. In this repo's adapter that maps to
`POST http://localhost:8181/v1/data/authz/bank/decision`, with the reply's `decision` / `reason` /
`obligations` mapped onto the shared `AccessDecision`. An **undefined** policy returns `{}` and is
treated fail-closed. OPA also supports an **in-process WASM** route (compile a Rego entrypoint to a
WebAssembly module and evaluate it inside the host), which trades the network hop for an in-proc
dependency; the repo ships the out-of-process REST path and documents WASM as an alternative.

### Reverse-index / query

OPA has no persisted relationship store, so it cannot natively answer "which resources can X
access?" by enumeration. It does offer **partial evaluation**: given a partially-known input, OPA
compiles a residual policy (e.g. into SQL/where-clause fragments) that a data store can execute to
filter candidate rows. That is the closest OPA gets to a reverse query, and it requires the caller
to translate the residual into a data-store query.

### API / SDKs incl. .NET support

The decision API is language-neutral REST/gRPC. Official SDKs target Go; **.NET support is
community-maintained** (thin REST clients over the decision API) rather than an official OPA .NET
SDK. This repo's adapter is a bespoke `HttpClient` against the decision endpoint, not a third-party
package.

### Hosting

Sidecar or central server (the common operating model), or in-process via the WASM SDK. Styra DAS
is a commercial control plane for managing OPA fleets, but the engine itself is self-hosted.

### Licensing & maturity

**Apache-2.0**, CNCF **graduated**, very large adoption and ecosystem. Mature, battle-tested,
strong tooling (`opa test`, `opa fmt`, coverage, bundles).

### Strengths / weaknesses / when to use

- **Strengths:** maximally expressive, ecosystem ubiquity, decoupled policy lifecycle (update
  policy without redeploying callers), strong test tooling, partial evaluation for data filtering.
- **Weaknesses:** Rego has a real learning curve; general-purpose power means more foot-guns;
  out-of-process adds a network hop and a second process to operate; no built-in relationship store,
  so reverse queries need partial-eval plumbing.
- **When to use:** you want an externalized, auditable policy layer across heterogeneous services,
  can operate a policy server, and value a single language for infra + app authorization.

**Sources**

- <https://www.openpolicyagent.org/>
- <https://www.cncf.io/projects/open-policy-agent-opa/>
- <https://www.openpolicyagent.org/docs/latest/policy-language/>
- Repo: [`docs/authz/opa-adapter.md`](../../authz/opa-adapter.md)

---

## Cedar (+ Amazon Verified Permissions)

### Overview / origin

Cedar is a purpose-built authorization policy language and engine open-sourced by AWS (2023). It is
the language behind **Amazon Verified Permissions (AVP)** and AWS Cloud Infrastructure Entitlement
work. Unlike Rego's general-purpose stance, Cedar is *narrow by design* — it does one thing
(authorization) and is built to be analysable and fast.

### Policy language / model

Policies are `permit` / `forbid` rules over a fixed request quad — `principal`, `action`,
`resource`, `context` — evaluated against a set of typed **entities**. Cedar natively expresses
**RBAC**, **ABAC**, and relationship-style checks (group/parent membership via the entity
hierarchy), making it a strong **PBAC** engine. In this repo the fintech domain is encoded as
embedded Cedar policies plus per-request entities (`User`, `Account`, `Transaction`), and — unlike
the role-gate-only adapters — Cedar expresses the **whole** decision (scope re-check, tenant
isolation, role eligibility, subject-is-maker, pending status, SoD) as policy conditions (see
[`cedar-adapter.md`](../../authz/cedar-adapter.md)).

### Decision / PDP model

Cedar's combining semantics: a request is permitted only if some `permit` matches and no `forbid`
matches. The repo runs Cedar **in process** via the `MonoCloud.Cedar` NuGet package (native .NET
bindings over the Cedar engine) — no server, no container, no policy files on disk; policies parse
once at construction. The adapter maps the **determining** `forbid`'s stable policy id to a reason
code and selects the highest-precedence reason to match the reference engine. Obligations
(maker-checker threshold) are attached by the adapter, since Cedar itself emits none.

### Reverse-index / query

Core Cedar answers a single yes/no `isAuthorized` per request and does not maintain a resource
index, so it does not natively enumerate "which resources can X access?". Cedar's design *does*
emphasise static analysis (the language is decidable), and tooling exists for policy analysis, but
reverse enumeration is not a core decision-API feature.

### API / SDKs incl. .NET support

Cedar has official implementations in Rust with bindings; this repo consumes **native .NET
bindings** (`MonoCloud.Cedar`, a fork over `cedar-policy/cedar-java`). For the **managed** path,
**AVP** exposes an `IsAuthorized` API with an **official AWS SDK for .NET**
(`AWSSDK.VerifiedPermissions` on NuGet). The policy language and decision semantics are identical
between embedded Cedar and AVP; only *where* evaluation happens changes.

### Hosting

- **Embedded Cedar** — in-process library; zero infra, deterministic, offline.
- **Amazon Verified Permissions** — AWS-managed policy store; a network call per decision, versioned
  policies editable without redeploying callers, centralized audit/history.

### Licensing & maturity

**Cedar language/engine: Apache-2.0** (`cedar-policy/cedar`). AVP is a proprietary AWS managed
service (usage-priced). Cedar is younger than OPA but AWS-backed and growing quickly; the language
is stable and well-specified.

### Strengths / weaknesses / when to use

- **Strengths:** authorization-specific and easy to read; analysable/decidable language; genuine
  in-process option with no infra; a clean managed upgrade path (AVP) with identical semantics;
  first-class .NET story on both paths.
- **Weaknesses:** narrower than Rego (only authorization); managed reach centres on AWS/AVP; no
  native reverse-index; obligations must be layered by the host.
- **When to use:** you want a readable, analysable authorization language, prefer an embedded engine
  with an optional managed AWS path, and are on (or comfortable adopting) the AWS ecosystem.

**Sources**

- <https://www.cedarpolicy.com/>
- <https://github.com/cedar-policy/cedar> (Apache-2.0)
- <https://aws.amazon.com/verified-permissions/>
- <https://www.nuget.org/packages/AWSSDK.VerifiedPermissions/>
- Repo: [`docs/authz/cedar-adapter.md`](../../authz/cedar-adapter.md)

---

## Casbin

### Overview / origin

Casbin is a lightweight, embeddable open-source access-control library with a large multi-language
footprint (Go, .NET, Java, Node, Python, Rust and more). It centres on a small **model + policy**
abstraction rather than a full policy language.

### Policy language / model

Casbin separates a **model** (`.conf` — request/policy/effect definitions plus a `matchers`
expression) from a **policy** (the `(sub, obj, act)`-style rules, often a CSV or a store). It
supports **ACL**, **RBAC** (with role hierarchies and domains/tenants), and **ABAC** via matcher
expressions over attributes. In this repo Casbin is deliberately used as a **role-gate-only**
engine: a minimal RBAC model with `(role, action)` policy pairs added in memory at construction,
answering only "is the subject's role eligible for this action?" — every ABAC rule (scope, tenant,
maker, pending, SoD, obligations) is composed by the shared `FintechRuleEvaluator`, not by Casbin
(see [`adapters-aspnet-casbin.md`](../../authz/adapters-aspnet-casbin.md)).

### Decision / PDP model

`enforcer.Enforce(...)` returns a boolean per request against the in-memory model and policy — a
pure, deterministic, **in-process** function. No server, no files on disk in this repo's adapter
(the model is a text constant; policies are added programmatically via `AddPolicy`). Casbin has no
built-in notion of obligations or structured reason codes; those are the harness's job here.

### Reverse-index / query

Casbin offers management-API helpers (e.g. list permissions/roles for a user), but it is not a
relationship store and does not answer resource-enumeration reverse queries at scale; it evaluates
the rules and policy you load into it.

### API / SDKs incl. .NET support

**Official `Casbin.NET`** library (Apache-2.0, CPM-pinned in this repo) with good parity to the Go
core: models, adapters (persistence), role managers, domains. Embedded library, not a server (though
server distributions exist in the wider ecosystem).

### Hosting

Embedded in-process library. Policy persistence is pluggable via adapters (files, DB, etc.), but the
enforcement runs inside the host.

### Licensing & maturity

**Apache-2.0**, very widely adopted across languages, long-lived and stable. Not a CNCF project, but
a mature de-facto standard for embeddable RBAC/ABAC.

### Strengths / weaknesses / when to use

- **Strengths:** tiny, zero-infra, official .NET support, flexible model DSL, easy RBAC/ABAC for
  classic web apps; trivial to embed as a role gate.
- **Weaknesses:** the model/matcher DSL is less expressive and less auditable than Rego/Cedar for
  rich decisions; no native obligations/reason codes; not a relationship store; matcher-based ABAC
  gets unwieldy for complex, ordered rule sets (hence the role-gate-only use here).
- **When to use:** you need a lightweight embedded RBAC/ABAC gate inside a .NET (or polyglot) app and
  don't need an externalized policy language or a managed control plane.

**Sources**

- <https://casbin.org/>
- <https://github.com/casbin/Casbin.NET> (Apache-2.0)
- Repo: [`docs/authz/adapters-aspnet-casbin.md`](../../authz/adapters-aspnet-casbin.md)

---

## Oso (Polar) and Oso Cloud

> **Disposition (this lab): de-scoped.** Oso is **evaluated → de-scoped** from the expansion-engine
> adapter set — there is no in-process .NET/Polar library, and Oso's only self-hostable artifact is a
> **development-only** dev-server (pinnable to versioned tags, but scoped by the vendor to local
> dev/testing, not a production server). Production runs on the paid, proprietary managed Oso Cloud,
> which is off the self-host-first posture of
> [ADR 0007](../../adr/0007-self-host-first-authz-with-managed-optionality.md). Re-evaluate only if
> Oso ships either a maintained in-process .NET/Polar library **or** a self-hostable,
> production-supported server (not development-only, not paid-cloud-only). See
> [ADR 0008](../../adr/0008-oso-descoped-from-expansion-engines.md) (verified 2026-07-05).

### Overview / origin

Oso is an authorization framework from Oso HQ. It began as an **embedded library** built around the
**Polar** logic language and has since pivoted its active development to **Oso Cloud**, a managed,
centralized authorization service. The open-source embedded library is now in a legacy/maintenance
posture (critical fixes, not active feature work); Oso Cloud is the recommended path for new work.

### Policy language / model

Policies are written in **Polar**, a declarative logic (Prolog-influenced) language purpose-built for
authorization. Polar expresses **RBAC**, **ABAC**, and **ReBAC** (relationships, org hierarchies,
custom roles, fine-grained permissions) in one language, making Oso unusually broad across models
for a policy engine.

### Decision / PDP model

- **Embedded library:** Polar rules evaluate in-process against application objects/classes you
  register, returning allow/deny (and query results).
- **Oso Cloud:** policies and **facts/relationship data** are stored centrally; the app calls a
  managed API/SDK to check permissions and to list authorized resources. Oso Cloud thus blends
  policy-as-code (Polar) with a stored relationship/fact model, moving it partway toward the ReBAC
  camp.

### Reverse-index / query

Because Oso Cloud stores facts centrally, it supports **list/filter** style queries ("which
resources can this user access?") in a way the purely stateless engines above cannot — a notable
differentiator versus core OPA/Cedar/Casbin.

### API / SDKs incl. .NET support

Oso Cloud provides official SDKs across languages **including C#/.NET**. The legacy embedded library
historically targeted Python, Node, Go, Rust, Ruby, and Java (its .NET/C# story was weaker than the
Cloud SDK's).

### Hosting

Embedded library (in-process, legacy) **or** Oso Cloud (managed SaaS, centralized policy + fact
store). For local use Oso ships a **dev-server** (`public.ecr.aws/osohq/dev-server`, pinnable to
versioned tags such as `:v1.2.3`, or a downloadable native binary) — but Oso scopes it to **local
development and testing**, not production (ephemeral state; the on-disk format is not a stability
guarantee). There is **no self-hostable production server**; production runs on the **paid, managed
Oso Cloud** (as of 2026-07-05).

### Licensing & maturity

The open-source Oso library is **Apache-2.0** but in maintenance mode; **Oso Cloud is a proprietary
managed service** (commercial pricing). Verify current licensing/support status against Oso's site,
as the OSS library's status has shifted.

### Strengths / weaknesses / when to use

- **Strengths:** single language (Polar) spanning RBAC/ABAC/ReBAC; Oso Cloud adds a stored fact model
  with list/reverse queries and a .NET SDK; good developer ergonomics.
- **Weaknesses:** the embedded OSS library is legacy (feature-frozen), so new investment funnels
  toward the managed, proprietary Cloud; adopting Oso Cloud is a managed-dependency commitment.
- **When to use:** you want one language across authorization models and are willing to adopt Oso
  Cloud (managed) — especially if you need reverse/list queries and central policy management.

**Sources**

- <https://www.osohq.com/>
- <https://www.osohq.com/docs>
- <https://github.com/osohq/oso>

---

## Cerbos (+ Cerbos Hub)

### Overview / origin

Cerbos is an open-source, **stateless** decoupled authorization layer from Cerbos (formerly Cerbos
by the team behind it). It ships as a self-contained PDP you run beside your services, plus **Cerbos
Hub**, a managed control plane for policy authoring, testing, distribution, and audit.

### Policy language / model

Policies are authored in **YAML** (resource policies and principal/derived-role policies), with
conditions expressed in **CEL** (Common Expression Language). It is oriented around **RBAC** with
**ABAC** conditions (attributes on principal/resource/context) and **derived roles** — a good fit
for the same fintech-style rules this repo encodes, without a bespoke logic language.

### Decision / PDP model

Cerbos runs as a **stateless PDP** (self-hosted binary/container) exposed over gRPC and REST. The
caller sends principal + resource(s) + actions; Cerbos returns per-action allow/deny. Being
stateless, it holds no relationship data — the caller supplies attributes at request time, exactly
matching the *full-decision* integration style of this repo's PDP contract.

### Reverse-index / query

Cerbos does not store a relationship graph, so it has no native reverse index. It does provide a
**Query Planner** (`PlanResources`): given a principal + action, it returns a structured condition
(an AST) the caller compiles into a data-store query (SQL/ORM filter) to fetch the accessible
resources — analogous to OPA partial evaluation. So "which resources?" is answered by the caller's
data store, guided by Cerbos.

### API / SDKs incl. .NET support

gRPC + REST APIs with official SDKs across languages, **including an official `Cerbos.Sdk` .NET
package**. The PDP is a sidecar/service you call; there is no in-process embedding of the engine.

### Hosting

Self-hosted **stateless PDP** (sidecar or central service) — plus **Cerbos Hub** (managed SaaS) for
policy CI/CD, testing, versioned distribution, and audit. The PDP stays yours; Hub manages the
policy lifecycle around it.

### Licensing & maturity

**Cerbos PDP core: Apache-2.0.** Cerbos Hub is a commercial managed offering (free tier + paid
plans). Growing adoption; well-regarded developer experience and testing story (`cerbos compile`,
policy unit tests).

### Strengths / weaknesses / when to use

- **Strengths:** simple YAML+CEL policies (gentle learning curve), stateless and easy to operate,
  strong policy-testing tooling, Query Planner for data filtering, official .NET SDK, optional
  managed Hub without giving up your PDP.
- **Weaknesses:** always out-of-process (a sidecar/service to run); no relationship store, so
  reverse queries need Query-Planner plumbing; YAML+CEL is less expressive than full Rego/Polar for
  the most complex logic.
- **When to use:** you want an externalized, testable, low-learning-curve PDP as a sidecar with an
  optional managed control plane, and your rules are RBAC-with-ABAC-conditions shaped.

**Sources**

- <https://www.cerbos.dev/>
- <https://github.com/cerbos/cerbos> (Apache-2.0)
- <https://docs.cerbos.dev/cerbos/latest/api/query_planner/>
- <https://github.com/cerbos/cerbos-sdk-dotnet>
- <https://www.cerbos.dev/product-cerbos-hub>

---

## Permit.io

### Overview / origin

Permit.io is a commercial **managed authorization platform** that puts a UI, SDKs, APIs, and a
policy-management/ops layer *on top of* established open-source engines. Rather than inventing a new
policy language, it composes existing engines — notably **OPA** (Rego), **OpenFGA** (ReBAC), and
Cedar — behind a unified product experience.

### Policy language / model

Because it is built on multiple engines, Permit.io covers **RBAC**, **ABAC**, and **ReBAC** in one
platform: RBAC/ABAC map to OPA/Rego (and Cedar), while ReBAC/relationship graphs map to an
OpenFGA-style backend. Policy is authored through Permit's UI/SDKs and compiled down to the
underlying engines, so the "language" is largely abstracted from the app developer.

### Decision / PDP model

Permit.io runs a **PDP sidecar** (a container you deploy next to your app, embedding the underlying
engine(s) and syncing policy/data from Permit's control plane) that your app queries locally for
low-latency `check` decisions. A cloud PDP option also exists. This is a managed-control-plane +
local-enforcement architecture: authoring/management is centralized SaaS; enforcement is a local
sidecar call.

### Reverse-index / query

Via its OpenFGA-style ReBAC backend and data sync, Permit.io supports relationship data and
list/filter ("which resources can this user access?") style queries that the stateless
policy-only engines lack — one of the benefits of composing a relationship store under the hood.

### API / SDKs incl. .NET support

REST/API plus official SDKs across languages **including .NET/C#** (`check`, role/relationship
management). The app talks to the local PDP sidecar; the sidecar talks to Permit's cloud control
plane.

### Hosting

**Managed SaaS control plane** (policy authoring, versioning, audit) + a **self-deployed PDP
sidecar** (or cloud PDP) for enforcement. You run the sidecar; Permit runs the management plane.

### Licensing & maturity

Commercial platform with a **free tier** and paid plans; parts of the stack are open source and it
leans on Apache-2.0 engines (OPA, OpenFGA, Cedar) underneath. As a product it is younger than the
raw engines it wraps; treat exact tier/feature/licensing specifics as version-dependent and verify
against Permit's site.

### Strengths / weaknesses / when to use

- **Strengths:** one platform spanning RBAC/ABAC/ReBAC without hand-rolling engine integration; UI +
  audit + policy lifecycle out of the box; local-sidecar enforcement latency; .NET SDK; reverse/list
  queries via the ReBAC backend.
- **Weaknesses:** a managed-vendor dependency and its pricing; abstraction over multiple engines can
  obscure the underlying policy semantics; you still operate the PDP sidecar; less control than
  running OPA/OpenFGA directly.
- **When to use:** you want managed authorization spanning multiple models with minimal
  engine-integration effort and value a UI/audit/ops layer over building on raw OPA/OpenFGA/Cedar.

**Sources**

- <https://www.permit.io/>
- <https://docs.permit.io/>
- <https://github.com/permitio>

---

## How these compare

- **Full-decision vs role-gate-only.** OPA, Cedar, Cerbos, Oso, and Permit.io can each own the
  **whole** `subject / action / resource / context` decision — the integration style this repo uses
  for OPA and Cedar against its [PDP contract](../../authz/pdp-contract.md). Casbin, as used here, is
  a **role-gate-only** engine: it decides role eligibility and the shared harness composes the rest.
- **Embedded vs out-of-process vs managed.** Cedar and Casbin can run **in-process** (zero infra,
  deterministic, offline) — the repo's "lite" profile. OPA and Cerbos are **out-of-process** PDPs
  (a sidecar/server to operate). AVP, Oso Cloud, Cerbos Hub, and Permit.io add a **managed** control
  plane (versioned central policy store + audit) at the cost of a service dependency — the same
  lite-vs-managed trade-off this repo draws for [Cedar vs AVP](../../authz/cedar-adapter.md).
- **Policy language expressiveness.** Rego (general-purpose) and Polar (logic language) are the most
  expressive; Cedar is authorization-specific and *analysable*; Cerbos YAML+CEL and Casbin
  model/matchers are the most approachable but least expressive for complex, ordered rule sets.
- **Reverse queries are the dividing line with ReBAC.** None of these policy engines maintains a
  relationship graph, so "*which* resources can X access?" is not a native decision-API answer.
  OPA (partial evaluation) and Cerbos (Query Planner) approximate it by emitting a residual/AST the
  caller's data store executes; Oso Cloud and Permit.io get closer to native list queries precisely
  because they add a stored fact/relationship backend. For true relationship-first reverse indexing,
  see the [ReBAC / Zanzibar survey](relationship-based-zanzibar.md).
- **.NET support.** Casbin (official `Casbin.NET`), Cedar (native bindings + `AWSSDK.VerifiedPermissions`
  for AVP), Cerbos (official `Cerbos.Sdk`), Oso Cloud, and Permit.io all have first-class or official
  .NET SDKs; OPA's .NET clients are community-maintained REST wrappers.

For the scored, side-by-side view across models, consistency, latency, reverse-index, testability,
auditability, ops burden, .NET support, AuthZEN alignment, licensing/maturity, and hosting, see the
[comparison matrix](../comparison-matrix.md). For the full landscape (ReBAC, entitlements, feature
flags, AuthZEN), see the [market survey index](../market-survey.md).
