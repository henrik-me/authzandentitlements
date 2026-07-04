# Cedar adapter

> **Scope:** how the `cedar` engine adapter plugs an in-process
> [Cedar](https://www.cedarpolicy.com/) policy / ABAC engine into the unified PDP. It documents the
> shipped adapter in
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): what Cedar is, the policies
> it evaluates, how a Cedar decision maps back onto `AccessDecision`, provider
> selection, and the fail-closed posture. Read the [PDP contract](pdp-contract.md) first — like every
> engine, Cedar answers the same `subject / action / resource / context` →
> `permit/deny + reasons + obligations` shape as the reference engine, and it is verified against the
> same 22-scenario [`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs).
> See [ARCHITECTURE.md](../../ARCHITECTURE.md) and the
> [coarse- vs. fine-grained boundary](../architecture/coarse-vs-fine-boundary.md).

## What the Cedar adapter is (and why)

Cedar is a purpose-built authorization policy language and engine. Policies are `permit` / `forbid`
rules over a fixed request quad — `principal`, `action`, `resource`, `context` — evaluated against a
set of **entities**. The adapter (`CedarDecisionProvider`, an
`IAuthorizationDecisionProvider` named `cedar`) runs Cedar **in process** via the
[`MonoCloud.Cedar`](https://www.nuget.org/packages/MonoCloud.Cedar) NuGet package — native .NET 10
bindings over Cedar (a fork of the upstream [`cedar-policy/cedar-java`](https://github.com/cedar-policy/cedar-java)
engine). No external server, no container, no policy files on disk: the fintech shape is encoded in
the embedded **policies** and the **entities** the adapter constructs — there is no separate Cedar
`Schema` artifact — and the policies are parsed once at construction.

This is the **in-process, container-free ("lite")** profile, the same one the
[`aspnet` and `casbin` adapters](adapters-aspnet-casbin.md) use — and the deliberate contrast to the
[`opa` adapter](opa-adapter.md), which forwards each request to an **out-of-process** OPA REST
server. Cedar gives the expressiveness of an externalized, declarative policy language *without* the
network hop or a second process, so it is the natural head-to-head counterpart to OPA: the same
fintech rules, expressed once in Cedar and once in Rego, evaluated by two very different engines and
required to agree scenario-for-scenario.

### Amazon Verified Permissions (the managed / cloud option)

The same Cedar policies can run **outside** the process in
[Amazon Verified Permissions (AVP)](https://aws.amazon.com/verified-permissions/) — AWS's fully
managed Cedar authorization service. Instead of embedding the policies in the PDP host, they live in
an AVP **policy store**, and the adapter would call AVP's `IsAuthorized` API per request. The policy
language and decision semantics are identical (AVP additionally supports an optional Cedar schema
that would mirror the entity shape this adapter constructs); only *where* evaluation happens changes.

| | In-process embedded Cedar (this adapter) | Amazon Verified Permissions (managed) |
|---|---|---|
| Where policy lives | Embedded string constants, compiled at startup | Central AVP policy store |
| Infrastructure | None — zero infra, deterministic, offline | AWS-managed service, network call per decision |
| Policy lifecycle | Redeploy the host to change policy | Versioned store, edit without redeploying callers |
| Audit / governance | Local decision events (see the [PDP contract](pdp-contract.md)) | Centralized store history, versioning, cross-service reuse |

The trade-off mirrors the lite-vs-managed split elsewhere in this project: embedded Cedar (and
Casbin) are zero-infra and deterministic; AVP (like an OPA server) centralizes the policy store,
versioning, and audit at the cost of a managed dependency and a per-request call. The shipped
`cedar` provider is the in-process embedded path; AVP is the documented managed alternative and is
not wired here.

## The Cedar model

The adapter encodes the fintech domain as embedded Cedar **policies** plus the **entities** it
constructs per request — there is no separate Cedar schema object; the shape below is what the
policies reference and what the adapter builds:

- **`User`** — the principal. Carries the caller's `roles` (e.g. `Teller`, `BranchManager`,
  `ComplianceOfficer`) and `tenant`.
- **`Account`** / **`Transaction`** — the resources. A transaction carries `tenant`, `makerId`,
  `status` (e.g. `Pending`), and `amount`; an account carries `tenant`.
- **Actions** — the bank action vocabulary: `bank.account.read`, `bank.account.create`,
  `bank.transaction.create`, `bank.transaction.approve`, and `bank.transaction.reject`.
- **`context.scopes`** — the coarse OAuth scopes re-checked as defence in depth (`bank.read`,
  `bank.transactions.write`, `bank.approvals.write`), matching the well-known vocabulary in the
  [PDP contract](pdp-contract.md#well-known-vocabulary).

Each incoming `AccessRequest` is projected onto this shape — a `User` principal, an action, the
target `Account`/`Transaction` entity, and a `context` carrying the request scopes — and handed to
the Cedar engine.

## How Cedar owns the full decision

Unlike the RBAC-only `aspnet` and `casbin` adapters — which decide *only* the role gate through
[`IEngineRoleAuthorizer`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/IEngineRoleAuthorizer.cs)
and defer every ABAC rule to the shared `FintechRuleEvaluator` — Cedar expresses the **whole**
fintech decision natively. Coarse-scope re-check, tenant isolation, role eligibility, subject-is-maker,
pending status, and segregation of duties are all Cedar policy conditions, not C# pipeline steps. This
is the CS09 head-to-head-with-OPA goal: a richer policy engine should carry the complete decision
itself, exactly as OPA's Rego does, rather than being reduced to a role lookup.

## Policy strategy for exact reason codes

The PDP contract requires more than the right allow/deny bit — each scenario has an expected
**primary reason code** (see the [reference rules](pdp-contract.md) and
[`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs)). Cedar's combining
semantics (a request is permitted only if some `permit` matches and no `forbid` matches) map cleanly
onto this if the policies are structured deliberately:

- One broad **`permit`** per action — the happy path for that action.
- One **`forbid`** per deny reason, each built as a `Policy(source, id)` with a **stable, explicit
  policy id** naming the [`ReasonCode`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs) it
  represents (e.g. `read.TenantMismatch`, `transaction.create.MissingScope`, `approval.NotPending`).

The adapter then maps the **determining** `forbid`'s id (echoed in the authorization response's reason
set) to the shared reason code. Explicit `Policy(source, id)` objects are used rather than `@id`
annotations because `PolicySet.ParsePolicies` ignores `@id` and assigns its own sequential ids; only
ids set on the `Policy` object are echoed back as the determining set. Because an
input can trip several `forbid`s at once, the adapter selects the **highest-precedence** reason in the
reference engine's per-action order, so the primary reason matches the reference and OPA for every
scenario. The per-action orders it mirrors (from
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)):

| Action | Reason precedence (first failing wins) |
|---|---|
| `bank.account.read` | scope → tenant |
| `bank.account.create` | role → tenant |
| `bank.transaction.create` | scope → role → subject-is-maker → tenant → **permit** (+ threshold obligation) |
| `bank.transaction.approve` / `bank.transaction.reject` | scope → role → tenant → pending → segregation-of-duties |

An action outside the vocabulary is denied by an **adapter-side guard** (a `KnownActions` check)
*before* Cedar is consulted — surfaced as the `UnknownAction` reason, fail-closed, mirroring the
reference engine's default arm rather than relying on Cedar's implicit deny.

## Obligations

On a permitted `bank.transaction.create`, the **adapter** attaches the maker-checker threshold
obligation (Cedar itself does not emit obligations), mirroring the reference engine's
[`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs): the adapter computes
it from the decimal amount — at or above the **$10,000** approval threshold obliges `require_approval`;
below it, `post_immediately`. Obligations are attached only on a permit.

## Fail-closed posture

The contract requires `Deny = 0` to fail closed, and the adapter honours it exactly as the
[`opa` adapter](opa-adapter.md#fail-closed-posture) does. Any Cedar evaluation error, an
unmappable result, or a determining `forbid` whose id is not in the bounded reason vocabulary
maps to a **`Deny`** with a stable, provider-local reason — never a permit, and the provider never
throws through to the caller. The specific cause (exception detail, an unexpected engine result) is
**logged** for operators; the `AccessDecision` returned to callers carries only a non-sensitive
message, so internal detail is never leaked through the anonymous evaluate endpoint. This provider-local
reason is deliberately not part of the shared `ReasonCodes` and never appears in the parity catalog —
it exists only so a real engine fault is a legible, machine-stable deny.

## Provider selection

Selection stays the config-driven CS05 seam, unchanged. The adapter registers alongside the reference
engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs),
and [`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
matches `Pdp:Provider` against the engine name case-insensitively:

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "cedar" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require
anything external — and because Cedar is in-process, selecting `cedar` still needs no container,
server, or network. An unknown provider name fails closed at the factory (it throws, naming the
unknown provider and listing the registered ones) rather than silently defaulting to an engine.

## Parity and testing

Cedar is held to the same **22-scenario parity bar** as the reference and OPA engines. Each scenario
in the shared [`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
is run through [`ScenarioCatalogRunner`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/ScenarioCatalogRunner.cs);
a scenario passes only when Cedar's decision **and** primary reason code match the catalog's
expectation. Because OPA is verified against the very same catalog, passing it is a genuine
head-to-head: the two engines answer every scenario — tenant isolation, role eligibility, the
maker-checker threshold and its boundary, segregation of duties, subject-is-maker, missing scopes,
and the fail-closed unknown-action path — identically to each other and to the reference.
