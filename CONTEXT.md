# Project Context

> **Last updated:** _(set when consumer first edits)_

## Codebase state

Bootstrap + pre-flight complete (2026-07-02). Git initialized on `main`; remote `origin` points to
`github.com/henrik-me/authzandentitlements` (pushed to GitHub, private). **agent-harness
v0.12.0 adopted** (process docs, review gates, linters, CI workflow). The full **27-CS plan**
is authored in `project/clickstops/planned/` with explicit Phase / Lane / Depends-on.

**CS01 (Aspire foundations) complete** (PR #2, merged 2026-07-03). The .NET Aspire solution
skeleton is scaffolded: `AuthzEntitlements.AppHost` (orchestration) + `AuthzEntitlements.ServiceDefaults`
(OTel/health/resilience) on **.NET 10 / Aspire 13**, with a single PostgreSQL resource exposing the
five logical DBs (`bank`, `openfga`, `entitlements`, `governance`, `audit`), Central Package Management
(`Directory.Packages.props`), shared `Directory.Build.props`, and a `global.json` pinning the .NET 10 SDK.
`dotnet build` is 0/0 and `aspire run` brings up the dashboard + a healthy Postgres container. Known-vulnerable
preview packages are handled with per-advisory `NuGetAuditSuppress` (tracked in LRN-003 for CS18).

**CS02 (fintech domain skeleton) complete** (PR #5, merged 2026-07-03). `AuthzEntitlements.Bank.Api`
(ASP.NET Core minimal APIs, net10.0) implements the fintech back-office domain — `Tenant → Region → Branch`,
`User`/`Role`/`UserRole` (RBAC), `Account`, `Transaction`, `Approval` — on **EF Core 10 (RC1) + Npgsql**
against the `bank` DB, with an `InitialCreate` migration, an idempotent deterministic seeder
(teller/manager/compliance/auditor users + posted / pending / manager-approved transaction scenarios), and
minimal CRUD + approve/reject endpoints. **Maker-checker + segregation-of-duties** are enforced (threshold
→ `Pending` + `Approval`; `checker != maker`; checker-role eligibility; tenant-scoped checker); tenant/branch
attributes are derived-from-account + FK-enforced; approval decisions use an `xmin` optimistic-concurrency
token. `dotnet build` 0/0, `dotnet test` 7/7; runtime-verified against Postgres 17. A new transitive advisory
(CVE-2025-55247, MSBuild via EF Core Design) was **remediated via a patched-version CPM transitive pin**
(LRN-005; drop in CS18). New learnings LRN-004..007 filed.

**CS03 (AuthN via Keycloak OIDC) complete** (PR #12, merged 2026-07-03). A Keycloak Aspire container
(fixed host port, `WithRealmImport`, `KC_FEATURES_DISABLED=organization`) imports the `authz-bank` realm
whose roles/users/branches/tenants/claims/clients mirror the CS02 seed (Keycloak user id = Bank.Api
`User.Id`, so JWT `sub` correlates). `AuthzEntitlements.Bank.Api` validates Keycloak JWTs
(`MapInboundClaims=false`, env-gated HTTPS metadata) and enforces role/scope/tenant authorization as an
**outer gate** over the CS02 maker-checker/SoD/tenant rules: reads are tenant-scoped, writes are scope+role
gated, and maker/checker + tenant are **bound to the token** (never caller-supplied) and fail-closed.
`AuthzEntitlements.Bank.Web` is an OIDC (code+PKCE) login stub; `docs/identity/entra-id.md` maps the setup to
Microsoft Entra ID. `dotnet build` 0/0, `dotnet test` 28/28; runtime-verified end-to-end against real Keycloak
(token claims for all 5 users + authz matrix + full `aspire run`). New learnings LRN-008..012 filed.

**CS04 (coarse-grained edge gateway) complete** (PRs #17 + #19, merged 2026-07-03). A new `AuthzEntitlements.Edge.Gateway`
(ASP.NET Core + YARP `Yarp.ReverseProxy` 2.3.0) fronts `Bank.Api` and enforces **coarse-grained** authorization at the
edge before routing — valid JWT + audience `bank-api` + the scope a route class needs (`bank.read` /
`bank.transactions.write` / `bank.approvals.write`) + `tenant`-claim presence — mirroring the CS03 token contract
(`MapInboundClaims=false`, shared `ResolveAudience`). Four coarse policies + a `TenantPresenceRequirement`; YARP routes
map each Bank.Api route class to its policy; the edge-authorized marker lives in the YARP proxy pipeline so routed
requests audit as `allow/routed`, edge short-circuits as denies, and unmatched (404)/method-mismatch (405) requests are
not audited. **Both gates emit structured, audit-ready authorization-decision events** (edge: `GatewayAuditMiddleware`
+ `gateway.decisions` meter; fine: `Bank.Api` `BankAuthorizationAuditMiddleware`) plus OTel — CS13 ingests later.
Boundary doc at `docs/architecture/coarse-vs-fine-boundary.md`. Build 0/0; gateway + Bank.Api unit suites pass;
runtime-verified against live Keycloak (401 no-token; scope-differentiated allow/deny; 405 not audited). GPT-5.5
rubber-duck (edge R1–R6) + Copilot reviewed. New learnings LRN-013..014.

**CS10 (commercial / product entitlements) complete** (PR #18, merged 2026-07-03). A new `AuthzEntitlements.Entitlements.Service`
(ASP.NET Core minimal APIs, net10.0) owns the `entitlements` DB and models **commercial entitlements**: plan tiers
(Standard/Professional/Enterprise) with module licensing (`wire`/`fx`/`treasury`), seat limits, feature gates, and usage
quotas. Feature gates go through **OpenFeature 2.14.0** — an in-memory provider (seeded from a single `FeatureCatalog`) is the
deterministic default; a config-gated `Unleash.Client` 6.2.1 provider + an `unleash` container (`WithExplicitStart`, off the
critical path) are the managed-flag option. Quotas use a Postgres `UsageCounter` (`xmin` optimistic-concurrency consume) + an
OTel `Meter`; **seat assignment enforces capacity atomically via a per-subscription Postgres advisory transaction lock**
(`pg_advisory_xact_lock` — serializes rather than conflict-retries; 0-error / no over-allocation verified under 30-way
concurrency). `Bank.Api` gains a typed, service-discovered **fail-closed** `IEntitlementsClient` + `EntitlementsEnforcer`
gating `POST /api/transactions` (wire-module / high-value-transactions-feature / monthly-quota → 402/403/429; 503 when the
service is unreachable). Every decision emits an **audit-ready** structured event (lower-case fields; Audit.Service ingests in
CS13). `dotnet build` 0/0, full-solution `dotnet test` 140/140; runtime-verified against Postgres 17. GPT-5.5 rubber-duck
R1–R9 + 5 Copilot rounds (all resolved). New learnings LRN-015..017.

**CS05 (AuthZEN-aligned unified PDP abstraction) complete** (PR #24, merged 2026-07-03). A new
`AuthzEntitlements.Authz.Pdp` (ASP.NET Core minimal APIs, net10.0, **no DB**) defines the unified fine-grained PDP: an
**AuthZEN-aligned `IAuthorizationDecisionProvider`** (`AccessRequest` = subject/action/resource/context → `AccessDecision`
= permit/deny + reasons + obligations) answered by an in-process `ReferenceDecisionProvider` that mirrors the Bank.Api
rules in order (scope → role → subject-is-maker → tenant → pending → SoD; 10,000 maker-checker threshold obligation).
**Config-driven provider selection** (`Pdp:Provider`, default `reference`) via a **fail-closed**
`AuthorizationDecisionProviderFactory` (unknown/blank/duplicate/whitespace names rejected) is the seam CS06–CS09 adapters
plug into. A **22-scenario engine-agnostic `FintechScenarioCatalog`** + `ScenarioCatalogRunner` is the parity bar
(exposed at `POST /api/authz/scenarios/verify`); every decision funnels through `PdpDecisionService`, emitting one
**audit-ready** event (`IPdpDecisionAuditSink`) + an OTel `pdp.evaluate` span + `pdp.decisions.total` counter —
**hooks/contracts only** (live Audit.Service is CS13; observability stack is CS12; no Bank.Api integration). The evaluate
boundary fails closed (400) on malformed requests. Adapter-author contract at `docs/authz/pdp-contract.md`. `dotnet build`
0/0; PDP `dotnet test` 139/139 (full solution green). GPT-5.5 rubber-duck R1 (Block) → R2–R6 (Go) + 5 Copilot rounds
(all resolved). New learnings LRN-021..022.

**CS12 (persistent observability stack) complete** (PRs #27 + #32, merged 2026-07-03). A single
`grafana/otel-lgtm:0.28.0` container (`observability`) bundles the **OTel Collector + Prometheus + Tempo +
Loki + Grafana** as the persistent telemetry backend beyond the ephemeral dev-time Aspire dashboard; a
persistent container lifetime + a `/data` volume survive `aspire run` restarts. All **five**
ServiceDefaults-instrumented services (`bank-api`, `entitlements-service`, `edge-gateway`, `bank-web`,
`authz-pdp`) fan OTLP to it via an AppHost-injected `OTEL_EXPORTER_OTLP_ENDPOINT` (ServiceDefaults already
gates its exporter on that var — **no ServiceDefaults code change**). Two read-only **baseline Grafana
dashboards** (Service Health RED + Request Rates) are provisioned from `infra/observability/`. Grafana is
locked to an **anonymous-Editor kiosk** — anonymous role `Editor`, UI login form + HTTP Basic Auth both
disabled — so the image's default `admin/admin` cannot escalate; OTLP ingest (4317/4318) is modeled as
internal `tcp` so only the Grafana UI is external. Doc at `docs/observability/observability-stack.md`.
`dotnet build` 0/0, full-solution `dotnet test` 279/279; standalone `grafana/otel-lgtm:0.28.0` verified
(both dashboards provision on Grafana 13.0.1; Prometheus(default)/Loki/Tempo datasources; `admin/admin`
Basic Auth blocked). GPT-5.5 rubber-duck (7 rounds incl. security hardening) + Copilot (multi-round) +
plan-vs-impl GO. New learnings LRN-023..024; LRN-014 (OTLP-export 500) carried forward for a full-`aspire
run` triage.

**CS06 (engine adapters: ASP.NET Core + Casbin.NET) complete** (PR #34, merged 2026-07-03). The first two engine
adapters plug into the CS05 PDP seam: `AspNetCorePolicyProvider` (`Name="aspnet"`, ASP.NET Core
`RolesAuthorizationRequirement`) and `CasbinDecisionProvider` (`Name="casbin"`, embedded Casbin.NET 2.21.2 RBAC model +
programmatic policy) — both **container-free "lite" profile** (no files/containers). A shared `FintechRuleEvaluator`
encodes the full per-action ordered fintech pipeline + ABAC in **lock-step parity with the reference**; each adapter is a
thin `IEngineRoleAuthorizer` supplying only the **engine-owned role gate**, so both answer the 22-scenario
`FintechScenarioCatalog` identically while the engine owns just role eligibility ("same question, swappable engine").
Registered in `AddPdp` (default stays `reference`); selectable via `Pdp:Provider`. Doc at
`docs/authz/adapters-aspnet-casbin.md`. `dotnet build` 0/0, full-solution `dotnet test` 375/375 (PDP 139→235, +96, incl.
per-adapter catalog parity + threshold-obligation tests). GPT-5.5 rubber-duck R1 (Conditional Go) → R2/R3/R4 (Go) + 3
Copilot rounds (all resolved) + plan-vs-impl GO. New learnings LRN-025..026.

**CS08 (engine adapter: OPA / Rego) complete** (PR #38, merged 2026-07-04; follow-ups #40, #41). A third engine plugs into the
CS05 PDP seam: `OpaDecisionProvider` (`Name="opa"`) forwards each `AccessRequest` to an **out-of-process OPA** REST decision API
(`POST /v1/data/authz/bank/decision`, `{"input": …}`) and maps the reply onto `AccessDecision`. The `authz.bank` Rego policy
(`infra/opa/policy/authz.rego`) mirrors `ReferenceDecisionProvider` exactly (ordered checks, 10,000 threshold obligation,
NotPending-before-SoD, fail-closed `UnknownAction` default), clearing the 22-scenario parity bar; `opa test` covers it (45/45, incl. a
bounded amount/time/geo/risk/tier ABAC showcase). The adapter is **fail-closed**: any transport/timeout/non-success/absent-result/
parse error, an unknown reason code, or a decision/reason inconsistency Denies with a provider-local `ProviderUnavailable` + a
**stable, non-sensitive** message (detail logged), never a permit or a 500. The OPA container is an **opt-in** Aspire resource
(`openpolicyagent/opa:1.18.2-static`, `WithExplicitStart`, no `WaitFor`); the default provider stays `reference` so build/test/`aspire
run` never need Docker or OPA. Docs at `docs/authz/opa-adapter.md` + `infra/opa/README.md` (incl. the WASM in-process alternative).
`dotnet build` 0/0, full-solution `dotnet test` 404/404 (PDP 264, +30 OPA adapter tests); `opa test` 45/45; live `POST
/api/authz/scenarios/verify` 22/22 with `Pdp:Provider=opa`. GPT-5.5 rubber-duck R1–R5 (Go) + Copilot (3 rounds: fail-closed
info-leak, sync-contract comment, untrusted reason-code — all resolved) + plan-vs-impl GO. New learnings LRN-027..029.

**CS07 (engine adapter: OpenFGA / ReBAC — Zanzibar) complete** (PR #35, squash-merged 2026-07-04 as `99d4abe`). A fourth engine
plugs into the CS05 PDP seam — the first **relationship-based (ReBAC)** one: `OpenFgaProvider` (`Name="openfga"`) maps an
`AccessRequest` to a single OpenFGA **forward `Check`** (subject→relation→object) via the sync seam (bridged sync-over-async), and
`OpenFgaRebacService` adds the **reverse-index** queries `ListUsers` ("who can view account X") / `ListObjects` ("what can user Y
access"), surfaced at `/api/authz/rebac/{verify,who-can-access,what-can-user-access}`. The schema-1.1 `RebacModel` encodes all four
relationship types — account ownership, relationship-manager→customer (tuple-to-userset indirection), branch/region hierarchy, and
delegation — with a consistent `RebacSeedTuples` graph and a CS07-specific `RebacScenarioCatalog` (ReBAC ≠ the RBAC
`FintechScenarioCatalog`, by design). The adapter is **fail-closed**: a not-configured (blank `Pdp:OpenFga:ApiUrl`), unreachable, or
erroring engine Denies (`EngineUnavailable`, stable non-sensitive message, cause logged) rather than throwing; the reverse-index
endpoints return 400 on bad input and 503 on engine unavailability. OpenFGA is an **opt-in** Aspire resource (pinned
`openfga/openfga:v1.18.1`, `migrate` + `run`, shared `openfga` postgres db, `WithExplicitStart`); the default provider stays
`reference` so build/test/`aspire run` never need Docker. `dotnet build` 0/0, full-solution `dotnet test` 456/456 (PDP 316, +51
OpenFGA/ReBAC tests incl. a fail-closed `Evaluate` unit test + self-skipping live-server integration); reviewed across 18 GPT-5.5
rubber-duck rounds + Copilot (13 rounds, all addressed) + plan-vs-impl GO. New learnings LRN-030..031.

**CS09 (engine adapter: Cedar) complete** (PR #48, squash-merged 2026-07-04 as `1b90ae9`). The fifth engine — the second
**policy/ABAC** one, head-to-head with OPA — plugs into the CS05 PDP seam: `CedarDecisionProvider` (`Name="cedar"`) runs Cedar
**in process** via `MonoCloud.Cedar` 0.1.0 (native .NET 10 bindings; a fork of `cedar-policy/cedar-java`), so unlike the RBAC-only
Casbin/ASP.NET adapters it **natively owns the full fintech decision** (per LRN-026), not the role-gate-only split. The embedded
`CedarPolicyModel` uses a broad `permit` per action + one annotated `forbid` per deny reason, built from explicit `Policy(source, id)`
objects so the authorization-response determining set carries stable, mappable ids; the adapter maps that set to the reference's
**first-failing** reason via per-action precedence (LRN-021), computes the threshold obligation adapter-side, guards unknown actions
adapter-side, and **fails closed** (provider-local `ProviderUnavailable`, never throws/permits, exception logged) mirroring the OPA
adapter. Tenant match is **fail-closed** (null/whitespace normalized) to match the reference — a gap the 22-scenario catalog missed,
caught by review (LRN-033). Container-free "lite" profile; default provider stays `reference`. Doc at `docs/authz/cedar-adapter.md`
(+ **Amazon Verified Permissions** as the managed/cloud option) + a `pdp-contract.md` pointer. `dotnet build` 0/0, full-solution
`dotnet test` 358/358 (PDP; +42 Cedar tests incl. full-catalog parity + per-scenario + obligations + combined-failure ordering +
blank-tenant reference-oracle parity + fail-closed + selection). GPT-5.5 rubber-duck R1 (Block: tenant fail-open) → R2
(Go-with-amendments) → R3/R5 (Go) + Copilot (2 comments resolved) + plan-vs-impl GO. New learnings LRN-032..033.

**CS17 (policy lifecycle + validation/testing) complete** (PR #55, squash-merged 2026-07-04 as `9f2df6f`). The
**policy-as-code lifecycle** layer on the CS05 PDP: a **shadow / dual-run** harness (`ShadowRunner`) compares one request —
or the whole 22-scenario catalog — across a primary + shadow engines, reporting decision/reason/obligation divergences
(`POST /api/authz/shadow{,/catalog}`; the default in-process RBAC family is `reference`/`aspnet`/`casbin`/`cedar`, excluding
OpenFGA[ReBAC, different model] and OPA[needs a live server]). **What-if simulation** (`WhatIfEvaluator`,
`POST /api/authz/whatif`) previews a chosen/active engine's decision **without enforcement** (bypasses `PdpDecisionService`,
so no audit event). A **golden-decision snapshot** (`GoldenDecisionSnapshot`) is an **independent** committed baseline
(decision + reason + obligations per scenario) with a SHA-256 **policy-version** hash + `Compute`/`Diff` **drift detection**
(`GET /api/authz/policy/version`). An **AuthZEN Access Evaluation** conformance surface (`AuthZenMapper` +
`POST /api/authz/authzen/evaluation`) carries the fintech attributes in AuthZEN property bags and **fails closed** via
`AuthZenRequestValidation` — a present-but-unparseable `amount`, or an omitted `maker_id`/`status` on transaction actions, is a
400, never a silent $0/SoD bypass (LRN-034). The factory gains name-based `GetProvider`/`TryGetProvider`/`ProviderNames`
(fail-closed). Validated by a **golden / negative / property-based** suite (cross-engine parity over a deterministic generated
request space, determinism, fail-closed totality, threshold obligations, full-catalog AuthZEN round-trip). Default provider
stays `reference`; no external dependency; the deterministic default run is unchanged. `dotnet build` 0/0; PDP `dotnet test`
358 → **417 (+59)**; full solution 544/544. GPT-5.5 rubber-duck R1 (Needs-Fix: AuthZEN boundary fail-open) → R2–R5 (Go) +
Copilot (3 rounds: whitespace-normalization, nullable `out`, nullable param — all resolved) + plan-vs-impl GO.
**CI-posture decision (escalation):** the exit criterion "policy changes are gated by CI tests" was delivered as the runnable
policy **test suite** + a documented opt-in GitHub Actions snippet rather than a new .NET CI workflow, respecting the repo's
process-gates-only posture (maintainer decision pending; see PR #55 Notes + LRN-035). Doc at
`docs/authz/policy-lifecycle.md`. New learnings LRN-034..035.

**CS16 (explainability: why allowed / why denied) complete** (PR #56, squash-merged 2026-07-04 as `6f17c05`). Every PDP decision —
permit or deny, from every engine — now carries a first-class, engine-agnostic **`DecisionExplanation`** (`Engine`, normalized
`DeterminingRule`, engine-native `PolicyReferences`, `Narrative`) on `AccessDecision`. `PdpDecisionService` **guarantees** it (a
baseline derived from the reason code when a provider attaches none) and threads it into the audit event (`PdpDecisionAuditEvent`
+ the default `LoggingPdpDecisionAuditSink`) and the `/api/authz/{evaluate,scenarios/verify}` responses. Per-engine extraction:
the reference engine + the shared `FintechRuleEvaluator` (Casbin/ASP.NET) attach normalized `rule` ids + engine-native role refs
(`IEngineRoleAuthorizer` gained `EngineName` + `DescribeRoleRule`); OPA's Rego emits an additive `rule` id surfaced as a `rego-rule`
ref; Cedar surfaces the determining forbid/permit **policy ids** (first-failing-first); OpenFGA surfaces the checked **relationship
tuple**. **Additive only** — reason codes, obligations, decision outcomes, and the 22-scenario parity are unchanged. Docs at
`docs/authz/explainability.md` (per-engine explanation-quality comparison) + a `pdp-contract.md` section; **UI rendering is CS15,
live audit ingestion is CS13** (plan-review R1 amendment). `dotnet build` 0/0, full-solution `dotnet test` 546/546 (PDP 406;
~50 explanation tests), `opa test` 51/51, runtime `/evaluate` smoke returns the explanation on permit + deny. 6 background
sub-agents (foundation → 4 parallel engines → docs); GPT-5.5 rubber-duck R1 (1 amendment: audit-sink emission) → R2/R3 (Go) +
Copilot (4 nits resolved) + plan-vs-impl GO. New learnings LRN-036..039.

**CS11 (access-governance entitlements) complete** (PR #54, squash-merged 2026-07-04 as `2ddc857`). A new
`AuthzEntitlements.Governance.Service` (ASP.NET Core minimal APIs, net10.0) owns the `governance` DB and models the **Entra-style
access-governance** lifecycle: **access packages** (e.g. `quarter-end-close`), **JIT elevation** gated by a two-stage **maker-checker +
segregation-of-duties** approval (the approver must be a known checker-eligible principal ≠ the requester), **time-bound grants** whose
expiry is enforced at **read time** via `IsActive(now)` (no background sweeper), and **access-review / recertification campaigns**
(materialise one item per active grant; certify/revoke). **SoD runs through the PDP** (plan-review R1 amendment — not direct OPA coupling):
a new engine-agnostic **`governance.access.request`** action (5 incompatible role-pairs) is answered by `PdpSodClient` → `POST
/api/authz/evaluate`, **fail-closed** (only `Deny/SodConflict` is a business denial; a PDP/OPA outage → retryable 503, request stays
`Pending`), and mirrored **identically** in the reference provider (`GovernanceSodPolicy`) and the OPA/Rego policy so the verdict is
engine-swappable. `SodConflict` is a first-class `ReasonCode` (surfaced through the OPA adapter + CS16 explanations as
`segregation-of-duties`). Every decision emits an **audit-ready** event + an OTel meter; decide-once via `xmin` + unique indexes.
`dotnet build` 0/0, full-solution `dotnet test` **762/762** (Governance.Service 64), `opa test` 64/64. GPT-5.5 rubber-duck **R1–R10** + **7
Copilot rounds** (all resolved) + plan-vs-impl GO. Doc at `docs/governance/access-governance.md`. New learning LRN-040. **Bank.Api
enforcement of governance grants, break-glass/CS21, and live audit ingestion (CS13) are deferred.**

**CS13 (tamper-evident audit log pipeline) complete** (content PR #57 + build-unbreak hotfix PR #60, squash-merged 2026-07-04). A new `AuthzEntitlements.Audit.Service` (ASP.NET Core minimal APIs, EF Core 10 + Npgsql over the `audit` DB) is a **tamper-evident, append-only, hash-chained** audit store: `AuditHashChain` (pure, DB-free) computes a SHA-256 **row hash** binding the sequence + previous row hash + every content field (UTC/µs-normalized timestamp so the hash survives the `timestamptz` round-trip; genesis prev-hash = 64 zeros), and `AuditChainWriter` serializes appends (single-writer `SemaphoreSlim` + transaction; `Sequence` PK + unique `RowHash` index as the DB backstop). `AuditEndpoints` exposes `POST /api/audit/decisions` (server-stamps `Producer="pdp"`), `GET /api/audit/verify` (streaming `VerifyAsync` + an optional **trusted checkpoint** `?expectedSequence=&expectedRowHash=` that catches tail-truncation / full-suffix rewrite a bare chain can't self-detect; malformed checkpoint → 400 fail-closed), and `GET /api/audit/entries` (filtered/paged). **PDP ingestion first:** `PdpDecisionService` already emits one `PdpDecisionAuditEvent` per decision; a **config-gated** HTTP-forwarding sink (`Audit:Sink=http`, default stays the offline logging sink) ships it to the store via a non-blocking bounded channel + resilient `BackgroundService`, off the decision hot path. `AppHost.cs` runs `audit-service` on the default run path and injects `Audit__Sink=http` + the service URL into `authz-pdp` (no hard `WaitFor`). Doc at `docs/authz/audit-pipeline.md`. `dotnet build` 0/0; full solution `dotnet test` **667/667** (Audit.Service 46 pure hash-chain/verify/checkpoint + PDP forwarding-sink tests). 5-round GPT-5.5 rubber-duck (R1 No-go: no trusted anchor → R2–R5 Go) + Copilot (2 rounds, resolved) + plan-vs-impl GO. The CS13×CS16 semantic merge conflict (both touched `PdpDecisionAuditEvent`) that red-greened `main`, fixed by hotfix #60, is documented in LRN-040; new channel learning LRN-041.

**Next claimable:** with **CS11, CS13, CS16, and CS17 DONE**,
the ready-and-unowned queue includes **CS18** (security hardening; needs CS04+CS05),
**CS20** (migration & portability; needs CS05–CS08), and **CS24** (perf benchmark; needs engines + CS12). **All five engine adapters
plus the unified explanation model are DONE**; CS11 completion advances **CS14** (Blazor product UI; needs CS03/CS04/CS06/CS10/CS11) and
**CS22** (compliance mapping; needs CS11+CS12+CS13). `harness lint` is green; the remaining CSs carry an independent GPT-5.5 `## Plan review` attestation.

## Constraints

See `.harness-known-constraints.md` for repository tier and disposition (detected 2026-07-02T18:34:27.975Z).

## Architecture pointer

See [ARCHITECTURE.md](ARCHITECTURE.md).

_(Keep this section as a short pointer. All architecture detail lives in
`ARCHITECTURE.md`. Add a sentence or two summarising the top-level design only
if it helps orient a reader skimming this file.)_

## Blockers / open questions

- **Branch-protection ruleset not applied.** `infra/main-protection-ruleset.json` requires a public repo or GitHub Pro (private repo returns HTTP 403); by decision the repo stays **private**, so the review-gate workflows run on PRs as **CI only (not required-to-merge)**. Apply the ruleset later if the repo goes public or the plan upgrades.
- **Docker daemon must be running** for the container-based engines/infra (Docker Desktop is installed).
- **Plan review: complete** — all 27 CSs independently reviewed (GPT-5.5) and attested (Go / Go-with-amendments); 13 CSs were amended to fix dependency/scope gaps found in review. No plan-review blocker remains.
- **Open decision:** whether to promote SpiceDB from the Phase-7 expansion (CS26) into the Phase-2 core for a direct OpenFGA head-to-head.

## CS plan

The full clickstop queue lives in `project/clickstops/planned/` (**27 CSs**). Each CS file
carries **Phase**, **Lane**, and **Depends on** fields. A claim-time `## Plan review`
(independent reviewer) is required before a CS moves to `active/`.

### Dependency + lane map

| CS | Title | Lane | Depends on |
|----|-------|------|-----------|
| CS01 | Aspire solution foundations | Foundation | None |
| CS02 | Fintech domain skeleton | Foundation | CS01 |
| CS03 | AuthN via Keycloak OIDC | Identity | CS02 |
| CS04 | Coarse-grained edge gateway | Identity | CS03 |
| CS05 | AuthZEN-aligned PDP abstraction | PDP-core (hub) | CS02 |
| CS06 | Adapters: ASP.NET + Casbin | Engines | CS05 |
| CS07 | Adapter: OpenFGA (ReBAC) | Engines | CS05 |
| CS08 | Adapter: OPA / Rego | Engines | CS05 |
| CS09 | Adapter: Cedar | Engines | CS05 |
| CS10 | Commercial entitlements | Entitlements | CS02 |
| CS11 | Governance entitlements | Entitlements | CS02, CS08 |
| CS12 | Observability stack | Observability | CS02 |
| CS13 | Audit log pipeline | Observability | CS05 |
| CS14 | Blazor product UI | Product | CS03, CS04, CS06, CS10, CS11 |
| CS15 | AuthZ playground + audit explorer | Product | CS06, CS07, CS08, CS09, CS13 |
| CS16 | Explainability (why allowed/denied) | Cross-cutting | CS05, CS06, CS07, CS08, CS09 |
| CS17 | Policy lifecycle + testing | Cross-cutting | CS05, CS06, CS07, CS08, CS09 |
| CS18 | Security hardening + threat model | Cross-cutting | CS04, CS05 |
| CS19 | Agent + non-agent access | Cross-cutting | CS03, CS05, CS13, CS14 |
| CS20 | Migration & portability | Cross-cutting | CS05, CS06, CS07, CS08 |
| CS21 | Break-glass, delegation & OBO | Cross-cutting | CS05, CS11, CS13, CS14, CS19 |
| CS22 | Compliance mapping | Cross-cutting | CS11, CS12, CS13 |
| CS23 | Comparison matrix + survey | Eval | CS15, CS24 |
| CS24 | Performance benchmark + tracking | Eval | CS06, CS07, CS08, CS09, CS12 |
| CS25 | Managed-vs-self-host TCO | Eval | CS23, CS24 |
| CS26 | Expansion engines | Expansion | CS05, CS15 |
| CS27 | Full OpenMeter + Azure deploy | Expansion | CS10, CS12, CS14, CS15, CS25 |

### Parallelization (waves)

- **Wave 1 (serial foundation):** CS01 -> CS02.
- **Wave 2 (parallel after CS02):** CS03 (Identity), CS05 (PDP hub), CS10 (Entitlements), CS12 (Observability).
- **Wave 3 (after CS05):** engine adapters CS06, CS07, CS08, CS09 (parallel); CS04 (after CS03); CS13 audit (after CS05); CS11 (after CS02 + CS08).
- **Wave 4 (after engines):** CS16, CS17, CS20 (need engine behavior); CS24 (engines + CS12); CS22 (CS11+CS12+CS13); CS14 product (CS03+CS04+CS06+CS10+CS11); CS15 playground (engines + CS13); CS18 (CS04+CS05).
- **Wave 5:** CS19 (CS13+CS14), CS23 (CS15+CS24), CS26 (CS05+CS15).
- **Wave 6:** CS21 (CS19+CS13+CS14), CS25 (CS23+CS24), CS27 (CS14+CS15+CS25).

A fleet of orchestrators can each `harness claim` an independent, dependency-satisfied CS.
