# Topaz / Aserto adapter

> **Scope:** how the `topaz` engine adapter plugs an out-of-process
> [Topaz](https://www.topaz.sh/) (Aserto) authorizer into the unified PDP. It documents the shipped
> adapter in [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp): the decision
> contract the adapter speaks to Topaz over the Aserto authorizer gRPC API, how Topaz's OPA decision
> maps back onto `AccessDecision`, provider selection, the deliberate **parity boundary** (Topaz's
> directory / ReBAC path is *not* used for the decision), and the fail-closed posture. Read the
> [PDP contract](pdp-contract.md) first — this adapter answers the same
> `subject / action / resource / context` → `permit/deny + reasons + obligations` shape as the
> reference engine.

## What the Topaz adapter is (and why)

[Topaz](https://www.topaz.sh/) is Aserto's open-source authorizer. It has two halves: an embedded
**OPA runtime** that evaluates a Rego policy bundle, and a built-in **Zanzibar-style relationship
directory** (an edge BoltDB of subject→relation→object tuples) that Rego can query for ReBAC checks.
The adapter
([`TopazDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/TopazDecisionProvider.cs))
is an `IAuthorizationDecisionProvider` named `topaz`: it forwards each `AccessRequest` to Topaz's
authorizer and maps the reply back onto the shared
[`AccessDecision`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs).

Because Topaz *is* OPA under the hood, this adapter drives it as a **full-decision** engine over the
**same Rego** the [OPA adapter](opa-adapter.md) uses — the bundle owned under
[`infra/opa/policy`](../../infra/opa/policy). It queries Topaz's authorizer for `data.authz.bank.decision`
with the `AccessRequest` as `input`, exactly the decision rule the standalone OPA server answers, and
maps the returned Rego decision object back onto `AccessDecision`. So the same maker-checker,
segregation-of-duties, tenant-isolation, and approval-threshold rules the in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
encodes in C# are evaluated by the OPA runtime **inside Topaz**.

This makes the `topaz` engine a direct **head-to-head with the standalone `opa` engine**: the *same*
policy, the *same* decision rule, the *same* 22-scenario parity bar — the only difference is the host
(a bare OPA REST server vs. the OPA runtime embedded in Topaz's authorizer, reached over the Aserto
gRPC API). It answers "does OPA-inside-Topaz decide identically to OPA-standalone?" — and it must.

### Parity boundary — the directory / ReBAC path is deliberately NOT used

Topaz's headline feature is its **Zanzibar relationship directory**. This adapter deliberately does
**not** use it for the decision. The whole fintech verdict is computed from the OPA bundle plus the
request `input`; the request carries an **anonymous** identity context
(`IDENTITY_TYPE_NONE`) and the directory is left **empty** (no manifest, no relationships). The
authorizer's `reader`/`model` services exist in the config only because the `authorizer` service
declares `needs: [reader]` in Topaz's service model — the decision path never consults them.

This is the same style of documented boundary the [SpiceDB adapter](spicedb-adapter.md) draws around
its ReBAC path: exercising Topaz's directory-backed ReBAC authorization (writing relationship tuples
and letting Rego call `ds.check`) is a **documented follow-on**, not part of this adapter. The point of
this clickstop is the OPA-standalone-vs-OPA-inside-Topaz comparison, for which the directory is
irrelevant.

## The decision contract

The adapter forwards each request through the narrow
[`ITopazCheckClient`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/ITopazCheckClient.cs)
seam, implemented for real by
[`TopazCheckService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/TopazCheckService.cs)
over the Aserto authorizer's **Query** API (gRPC on `:8282`, with a REST gateway on `:8383`):

- **Query:** `x = data.authz.bank.decision` — bind the shared decision rule to a result variable.
- **Input:** the `AccessRequest` serialized in camelCase (`subject.*`, `action.name`, `resource.*`,
  `context.scopes`) — the same wire vocabulary from the
  [PDP contract](pdp-contract.md#well-known-vocabulary) that the OPA adapter posts under `input`.
- **Identity:** an explicit anonymous `IDENTITY_TYPE_NONE` context (the authorizer rejects an unset /
  unknown identity type; the decision itself ignores identity — see the parity boundary above).

The authorizer returns the OPA query result as a protobuf `Struct` shaped
`{ "result": [ { "bindings": { "x": <decision object> }, "expressions": [...] } ] }`. The service
navigates to the bound decision object and reads its raw fields — the **same** decision object the OPA
adapter maps, because it is the **same** Rego rule:

```json
{ "decision": "Permit", "reason": "Permit", "rule": "transaction.create.Permit", "obligations": ["require_approval"] }
```

Any structural deviation (no `result` list, an empty result, a missing/wrong-typed binding) is surfaced
as a sentinel "no decision" outcome, which the provider **fails closed** on — it never fabricates a
decision from a malformed response.

### Reason codes

The decision object's `reason` is one of the stable
[`ReasonCodes`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs) the shared Rego emits — the
same bounded set the reference and OPA engines emit: `Permit`, `MissingScope`, `TenantMismatch`,
`RoleNotAuthorized`, `SubjectNotMaker`, `MakerEqualsChecker`, `NotPending`, `SodConflict`,
`UnknownAction`. (`ReasonCodes` also declares `BranchNotInTenant`, reserved for branch-level ABAC and
not emitted by the bank policy.) The adapter validates the returned code against this bounded
vocabulary and **fails closed** on any code outside it, so an out-of-process engine cannot surface an
unbounded reason to callers or inflate audit/metric (`pdp.reason`) cardinality.

## How Topaz's OPA decision maps to `AccessDecision`

| Topaz decision object | `AccessDecision` |
|---|---|
| `decision "Permit"` + `reason "Permit"` | `AccessDecision.Permit(reason)` |
| `… + obligations ["require_approval"]` | `Permit` + `ObligationIds.RequireApproval` |
| `… + obligations ["post_immediately"]` | `Permit` + `ObligationIds.PostImmediately` |
| `… + obligations [<unknown>]` | **fail closed** (`Deny` / `ProviderUnavailable`) |
| `decision "Deny"` + `reason <code>` | `AccessDecision.Deny(new Reason("<code>", …))` (no obligations) |
| absent / empty query result (`None`) | **fail closed** — policy undefined for the input, or malformed response |
| missing `reason`, unknown `reason`, unknown `decision` | **fail closed** |
| `Permit` with a non-`Permit` reason, or `Deny` with `Permit` | **fail closed** (decision/reason inconsistency) |

Obligations are only attached on a `Permit`. **Unlike the standalone OPA adapter — which *drops* an
unrecognized obligation string — the Topaz adapter *fails closed* on one** (matching the
[Cerbos adapter](cerbos-adapter.md)). A malformed obligation token (e.g. a typo like `requre_approval`)
must never permit while silently dropping the maker-checker approval requirement — that would be a
fail-**open** on the 10,000 threshold. An absent/empty obligation list is a legitimate no-obligation
permit (a read, or a below-threshold transaction). The obligation ids mirror those the reference engine
attaches to a permitted `bank.transaction.create`
([`ObligationIds`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs)): `require_approval`
at/above the 10,000 approval threshold, `post_immediately` below it.

> **Deliberate divergence from OPA (documented decision).** The two adapters run the *same* Rego, so a
> conformant bundle never emits an unknown obligation and the two behave identically on the parity
> catalog. They differ only on a *malformed* response: OPA drops the unknown token; Topaz denies. The
> stricter Topaz posture is the intended fail-closed default for this clickstop.

Every well-formed decision carries a CS16
[`DecisionExplanation`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanation.cs) with
`Engine = "topaz"`, the normalized determining rule for the reason, and **OPA-native** policy
references — mirroring the OPA adapter exactly so the two line up in the playground: the policy's
determining-rule id (`<action-short>.<Reason>`, e.g. `transaction.create.Permit`) surfaced first as a
`rego-rule` when present, plus the stable package-path reference `data.authz.bank.decision`. A bundle
that predates the additive `rule` field degrades the explanation to the package-path reference only —
it never fails the decision.

## Provider selection

The adapter is registered alongside the reference engine in
[`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)
(the `TopazOptions` + the lazy `TopazCheckService` client seam, also exposed as `ITopazCheckClient` from
the same singleton, + the provider). Selection stays config-driven via `Pdp:Provider`, matched
case-insensitively by
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs):

```jsonc
// appsettings.json / environment
"Pdp": { "Provider": "topaz" }   // or the default "reference"
```

The default stays **`reference`** so `dotnet build`, `dotnet test`, and `aspire run` never require a
live Topaz. Configure the authorizer endpoint under the `Pdp:Topaz` section
([`TopazOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/TopazOptions.cs)):

```json
"Pdp": {
  "Topaz": {
    "Endpoint": "https://localhost:8282"
  }
}
```

`ApiKey` and `TenantId` are also bindable but empty by default — the lab Topaz config
([`infra/topaz/config.yaml`](../../infra/topaz/config.yaml)) enables anonymous access and disables the
API key. Under `aspire run` the AppHost injects `Pdp__Topaz__Endpoint` automatically once the opt-in
`topaz` container is started.

### TLS (and cleartext h2c)

Topaz's authorizer serves gRPC over **TLS** with a **self-signed dev certificate** it auto-generates on
boot, so the endpoint is an `https://` address and the adapter connects in **insecure** mode for a
**loopback** endpoint (localhost / `127.0.0.1` / `::1`) — it accepts the self-signed cert, a lab posture
**bounded to loopback**, never a deployment. For a **non-loopback** `https://` host the adapter uses
**normal CA validation** (so a misconfigured remote endpoint can never silently accept a forged/MITM
certificate); a real TLS Topaz would present a CA-trusted cert. Unlike the
[Cerbos adapter](cerbos-adapter.md), an `https://` endpoint is therefore **valid** here. A plain
`http://` endpoint selects Topaz's cleartext **h2c** transport instead; `TopazCheckService` enables
`AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` **lazily, only
when it builds an `http://` channel** — never at type load and never on the TLS path — so that path works
too.

## Bundle delivery — local, no registry push

Topaz normally pulls its OPA policy bundle from an **OCI registry** (e.g. `ghcr.io`). This adapter
deliberately does **not** use a registry — neither a pull nor a push. Instead
[`infra/topaz/config.yaml`](../../infra/topaz/config.yaml) points Topaz's OPA runtime at a **local
bundle** built from the bind-mounted policy directory:

```yaml
opa:
  local_bundles:
    paths:
      - /policy            # infra/opa/policy, bind-mounted read-only
    skip_verification: true
```

The stock OCI `opa.config.services` / `bundles` blocks from the Topaz template are removed, so the
authorizer never reaches out to a registry: it **boots and decides fully offline**. This reuses the
**exact same** `infra/opa/policy` Rego the OPA adapter evaluates (read-only — the Topaz deliverable does
not modify the shared policy), which is what makes the head-to-head honest. No registry credentials, no
`policy push`, no network egress.

## Fail-closed posture

The contract requires `Deny = 0` to fail closed. Because Topaz is out of process, the adapter can fail
for reasons the reference engine cannot — so it maps **any** failure to obtain a well-formed
`Permit`/`Deny` to a `Deny` with a provider-local `ProviderUnavailable` reason code, never a permit.
This happens on:

- a not-configured endpoint (blank), or a malformed / non-`http(s)://` endpoint,
- an unreachable authorizer or any gRPC transport error,
- an absent / empty / structurally-malformed query result (the `None` sentinel),
- a missing reason, or an unrecognized reason code (outside the bounded `ReasonCodes` vocabulary),
- an unrecognized `decision` string,
- a decision/reason inconsistency (a permit carrying a non-`Permit` reason, or a deny carrying
  `Permit`),
- a permit carrying an **unknown obligation** (so a malformed obligation can never permit while
  silently dropping the maker-checker approval requirement).

The authorizer client is built **lazily** on first use and the endpoint is validated (well-formed
absolute `http(s)://` URI) before any network call, so a misconfiguration fails closed with an
actionable message rather than a cryptic gRPC/Uri error. The gRPC channel is **owned** by the service
(not the Aserto wrapper) so it is disposed on a failed build and at shutdown. The specific cause is
logged (sanitized — CWE-117); the `AccessDecision` returned to anonymous `/api/authz/evaluate` callers
carries only a stable, non-sensitive message, so internal URLs / network / config detail never leak.
`ProviderUnavailable` is **provider-local**: it is deliberately not part of the shared `ReasonCodes` and
never appears in the parity catalog (Topaz is reachable there and the bundle is total), so it maps to no
Bank.Api rule. It exists only so a real outage is a legible, machine-stable `Deny`.

### Boundary — delegation / on-behalf-of / break-glass (enforced at the factory seam)

The shared bank Rego models the **base** fintech decision (coarse scopes, role eligibility, tenant
isolation, maker-checker, SoD, and the threshold obligation). The CS19/CS21 **on-behalf-of**
(`Subject.Actor`), **manager→delegate delegation** (`Context.Delegation`), and **break-glass**
(`Context.BreakGlass`) constraints are **not** encoded in it, so a delegation-aware request evaluated
against this delegation-*unaware* engine could **fail open**.

That guard is **not** in this adapter. As of CS45 it lives once, authoritatively, at the
[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
seam: the factory wraps every engine that does not declare
[`ISupportsExtendedAuthorizationContext`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/ISupportsExtendedAuthorizationContext.cs)
— `topaz` included — in the fail-closed
[`ExtendedContextGuardProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ExtendedContextGuardProvider.cs),
which **denies** any request carrying `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass` with
the distinct reason `ExtendedContextUnsupported` **before** it reaches this adapter. So this adapter
deliberately does **not** implement the capability marker. See
[the PDP contract's extended-authorization boundary](pdp-contract.md#extended-authorization-fail-closed-boundary).

## Parity and testing

The shared Rego running inside Topaz targets the same **22-scenario parity bar** the reference engine
satisfies
([`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)) —
Topaz and the reference engine must agree scenario-for-scenario on both the `Decision` and the primary
reason code, and (because it is the same policy) Topaz must agree with the standalone `opa` engine too.

The adapter's mapping and fail-closed layer are unit-tested fully **offline** (no live Topaz) via a fake
`ITopazCheckClient` seam in
[`TopazDecisionProviderTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/TopazDecisionProviderTests.cs),
and the endpoint-validation / lazy bootstrap in
[`TopazCheckServiceConfigTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/TopazCheckServiceConfigTests.cs).
Because the OPA-bundle path is only exercised by a running authorizer, full **policy** parity is proven
by the env-gated
[`TopazIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/TopazIntegrationTests.cs), which
soft-skips unless `TOPAZ_TEST_ENDPOINT` is set — so the default `dotnet test` stays green with no
container.

### Running end to end

1. Start Topaz with the shared bundle — either the Topaz Aspire container (start the `topaz` resource,
   which is registered with explicit start, then `aspire run`) or directly with Docker:

   ```bash
   docker run --rm -p 8282:8282 -p 8383:8383 \
     -e TOPAZ_DB_DIR=/db -e TOPAZ_CERTS_DIR=/certs \
     -v "$(pwd)/infra/topaz:/config:ro" \
     -v "$(pwd)/infra/opa/policy:/policy:ro" \
     ghcr.io/aserto-dev/topaz:0.33.14 run -c /config/config.yaml
   ```

2. Point the PDP at the adapter (and, outside `aspire run`, give it the endpoint):

   ```bash
   Pdp__Provider=topaz
   Pdp__Topaz__Endpoint=https://localhost:8282
   ```

3. Run the scenario self-check and expect every scenario to pass:

   ```bash
   POST /api/authz/scenarios/verify   # expect AllPassed
   ```

   Or run the env-gated integration suite directly:

   ```bash
   TOPAZ_TEST_ENDPOINT=https://localhost:8282 dotnet test \
     tests/AuthzEntitlements.Authz.Pdp.Tests \
     --filter FullyQualifiedName~TopazIntegrationTests
   ```
