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

**CS28 (.NET build/test CI gate) complete** (PR #68, squash-merged 2026-07-04 as `e5c8366`). `.github/workflows/dotnet-ci.yml`
now builds + tests the whole solution on `pull_request`→`main` and `push`→`main` (SHA-pinned `actions/checkout` v6.0.2 +
`actions/setup-dotnet` v5.4.0 from `global.json`; `dotnet build` then `dotnet test --no-build --no-restore`; no Docker — the
OPA/OpenFGA live tests self-skip), closing the **LRN-035 / CS13×CS16** gap where neither CI nor `harness startup` built .NET, so
a cross-CS *logical* merge conflict (two PRs each green vs their own base) could land a non-compiling `main` uncaught.
**Advisory** on this private free-tier repo (branch-protection API → HTTP 403): the check is not required-to-merge, and fully
*preventing* the merge-order class needs require-up-to-date / a merge queue — a follow-up for when the repo goes public/Pro;
the `push`→`main` run detects such breaks reactively. Verified **live**: `build-test` green (~1m30s) on PR #68. GPT-5.5 plan
review (Go-with-amendments) + content rubber-duck (Go, 2 Copilot rounds) + plan-vs-impl GO. LRN-035 updated (core gap
addressed; enforcement residual kept open).

**CS18 (security hardening + threat model) complete** (PR #69, squash-merged 2026-07-04 as `8fb4106`). A STRIDE threat
model for the authorization system itself: `docs/security/threat-model.md` maps 10 trust boundaries and every STRIDE
category to its **shipped, `file:line`-cited** control, residual risk, and tracked follow-on — covering the four
called-out threats (token replay/forgery, confused-deputy, tuple/policy tampering, fail-closed-on-PDP-outage). Recon
confirmed the posture was **already strong** (issuer/audience/signature/lifetime token validation, `MapInboundClaims=false`,
HTTPS-metadata-outside-Dev; maker/checker/tenant bound to the token or the trusted resource row, never caller input;
fail-closed-on-outage across every PDP engine + entitlements + governance SoD — all built+tested in CS03–CS11), so CS18 is
predominantly **threat-model + verification + targeted hardening**. Hardening: both JWT setups (`AuthenticationSetup.cs`,
`GatewayAuthenticationSetup.cs`) gain an explicit tightened `ClockSkew` (30s, from the 5-min default) + `RequireExpirationTime`
+ `RequireSignedTokens` + `ValidateIssuerSigningKey`, with new offline token-security tests (config assertions + functional
expired/tampered/wrong-aud/wrong-iss/missing-exp rejection) in both test projects. `docs/security/secrets-and-least-privilege.md`
inventories the dev-only committed secrets (all confirmed dev-only; the Postgres password is an Aspire parameter), a
least-privilege review, and a production hardening checklist. Engine tuple/policy **cryptographic signing** is a tracked
follow-on (R1 plan-review amendment — drift detection ≠ signature). `dotnet build` 0/0; full solution `dotnet test`
**784/784**; `build-test` CI green. GPT-5.5 rubber-duck R1+R2 (Go) + Copilot (4 comments → explicit `ValidateIssuerSigningKey`
+ test hygiene, all addressed) + plan-vs-impl GO. New learnings LRN-042..043.

**CS20 (migration & portability — extensibility) complete** (PR #71, squash-merged 2026-07-04 as `a57475e`). Demonstrates
engine extensibility on the existing seams as a **library + tests + docs** change (no new HTTP surface): (D1) a config-driven
engine swap needs **no app-code change** — one unchanged `DecideVia` call site routes to `reference`/`casbin`/`cedar`/`aspnet`
purely by `Pdp:Provider` (CS05 `AuthorizationDecisionProviderFactory`); (D2) a new `AuthzEntitlements.Authz.Pdp.Migration`
**RBAC→ReBAC translator** mechanically converts an RBAC policy (`RbacPolicy`/`FintechRbacPolicy`, grant matrix mirroring
`ReferenceDecisionProvider`'s role eligibility) into an OpenFGA schema-1.1 "roles as usersets" model + `RebacTuple`s
(`RbacToRebacTranslator` → `TranslatedRebacGraph`), with an **in-process `Check` parity resolver** proving translated-ReBAC ==
RBAC across the full user×permission grid — **no live OpenFGA server**; the relation-name sanitizer is **fail-closed to the
OpenFGA identifier rule** `^[a-z][a-z0-9_]{0,62}$`; and (D3) a **dual-run/shadow parity gate** (CS17 `ShadowRunner.RunCatalog`)
proves `reference` vs `casbin`/`cedar`/`aspnet` = zero divergences (plus a non-vacuous divergence-caught test). Docs at
`docs/authz/migration-and-portability.md` + `docs/authz/adding-an-engine-adapter.md`. Only the pure role→permission dimension
translates; contextual/ABAC gates stay with the ABAC engines (documented). `dotnet build` 0/0, PDP `dotnet test` 512→**544**,
`build-test` CI green, `harness lint` 22/0. GPT-5.5 rubber-duck R1–R4 (Go) + 3 Copilot rounds (OpenFGA relation-rule
correctness + nits, all resolved) + plan-vs-impl GO. New learnings LRN-044..045.

**CS24 (performance benchmark + tracking) complete** (PR #75, squash-merged 2026-07-04 as `b8c2720`). A new zero-dependency `AuthzEntitlements.Benchmarks` console + test project measures PDP authorization latency by running the shared `FintechScenarioCatalog` through each engine and timing every `Evaluate` allocation-free (`Stopwatch.GetTimestamp`/`GetElapsedTime`): in-process `reference`/`aspnet`/`casbin`/`cedar` are measured; live `opa`/`openfga` **probe-and-self-skip** offline (cancellation-bounded TCP probe — no Docker on the default path). `LatencyStatistics` computes cold + warm **p50/p95/p99** + throughput (nearest-rank); `ResultStore` persists runs as camelCase JSON and is **fail-closed** (malformed/empty/null/unsupported-`schemaVersion`; timeout-bounded git-sha capture); `RegressionDetector` flags warm-p95 regressions vs a committed baseline (25% relative **and** 0.10 ms absolute floor) and `--check` exits non-zero to alert. For live trend tracking, an **append-only `pdp.evaluate.duration` histogram** was added to `PdpTelemetry` + recorded in `PdpDecisionService` (behavior preserved), visualized by a new Grafana `infra/observability/grafana/dashboards/pdp-performance.json` (p50/p95/p99 via `histogram_quantile` + decision throughput). Doc at `docs/eval/performance-benchmarks.md`. `dotnet build` 0/0; full solution `dotnet test` **868/868** (Benchmarks.Tests 52). GPT-5.5 rubber-duck (full-diff Go) + **6 Copilot rounds** — each a real fail-closed/robustness hardening (subprocess hang, dup-engine/dup-baseline, `schemaVersion` validation, probe-cancel, frozen `JsonSerializerOptions` + non-negative tolerance) all resolved — + plan-vs-impl GO. Plan D2 "caching" was scoped to the cold/warm split (not explicit cache hit/miss — documented divergence; test-coverage follow-ups noted). New learnings LRN-046..047.

**CS14 (Blazor fintech product UI) complete** (PR #76, merged 2026-07-04). `AuthzEntitlements.Bank.Web` becomes a **Blazor Web App (Interactive Server)** demonstrating one fintech back-office workflow through all four authorization layers with **visible outcomes** — AuthN (Keycloak OIDC; preserves the CS03 cookie+OIDC contract + adds `bank.transactions.write`/`bank.approvals.write` scopes) → coarse edge gateway → fine Bank.Api maker-checker/SoD → commercial entitlements. Token-forwarding pages render **static SSR** (so `IHttpContextAccessor`/`GetTokenAsync` work) with `MapStaticAssets` for hydration; one **Interactive Server island** (`EntitlementChecker`) does a live feature check reading tenant from the cascaded auth state. Fail-closed typed clients (`IBankApiClient` via the gateway with an `AccessTokenHandler`; anonymous Entitlements/Governance/Pdp) capture coarse/fine/entitlement/decide-once/unavailable denials into `ApiResult`; `CurrentUser` maps the OIDC identity → Bank user GUID (== token `sub`), tenant, and governance principal. Pages: Home, Claims, Accounts/AccountDetail (read slice), NewTransaction (maker), Approvals (checker), Entitlements (+ island), AccessRequests (JIT + SoD, **tenant-scoped decide guard**). `AppHost` wires bank-web → edge-gateway/entitlements/governance/authz-pdp. Doc at `docs/product/bank-web.md`. `dotnet build` 0/0; full solution `dotnet test` **959/959** (Bank.Web.Tests 91). 9-round GPT-5.5 rubber-duck (R1 Needs-Fix `MapStaticAssets` → Go; R7 Needs-Fix cross-tenant decide-guard → Go; R9 main-merge Go) + **6 Copilot rounds** (each a real fail-closed/robustness/accuracy fix — cf. LRN-047) + plan-vs-impl GO. New learnings LRN-048..049.

**CS22 (compliance mapping — SOX/PCI-DSS/GDPR) complete** (PR #83, squash-merged 2026-07-04 as `6220a13`). Maps the lab's shipped controls to regulatory frameworks and surfaces **runnable** evidence. (1) `docs/compliance/control-mapping.md` — one table per framework (SOX ITGC / PCI-DSS v4.0 Req 7/8/10 / GDPR Art 5/25/30/32) → shipped control with verified `file:line` citations + evidence surfaces + an honest "known gaps and residuals" section (illustrative, not a certification). (2) A new zero-dependency `AuthzEntitlements.Compliance` console+library evidence report generator (CS24-Benchmarks pattern): a **deterministic/DB-free SoD report** (drives `GovernanceSodPolicy.FindConflict` + the in-process `ReferenceDecisionProvider` on `governance.access.request` over all 5 incompatible pairs → Deny+`SodConflict` vs Permit) and a **deterministic audit-integrity report** (folds a chain with the pure `AuditHashChain` and proves detection of content mutation / tail-truncation-with-trusted-checkpoint / sequence gap / broken prev-hash); plus **access-certification** and **least-privilege** reports that live-probe Governance.Service and **self-skip offline** while **failing closed** on a reached-but-erroring response (transport/timeout → self-skip; reached non-2xx or malformed → exit 1; caller-cancel propagates). JSON (frozen options, fail-closed reader) + Markdown output; `requireValue`-guarded CLI. (3) `infra/observability/grafana/dashboards/compliance.json` — a Grafana **Compliance** dashboard (SoD denials, governance decisions/grants/reviews, PDP outcomes, entitlements, gateway) grounded in the emitted meters. `dotnet build` 0/0; full solution `dotnet test` **1023/1023** (Compliance.Tests 64). GPT-5.5 rubber-duck R1 Needs-Fix (fail-open live probe + 2 doc citations) → R2 Go → R3 Go + Copilot (2 findings: caller-cancellation semantics + GDPR Article 32 wording, both fixed) + plan-vs-impl GO. New learnings LRN-050..052 (fail-closed live-probe three-way classification; inferred non-SoD dashboard metric names pending a live scrape; CI evidence-gate zero-step flake → local `harness pr-evidence` + admin-merge).

**CS29 (governance tenant-scoping + fail-closed hardening) complete** (PR #89, squash-merged 2026-07-04 as `d25aa77`). Closes the LRN-049 confused-deputy / cross-tenant gap in `Governance.Service` **server-side** and hardens `RbacPolicy.Create` (LRN-044). The five access-request endpoints (create/list/get/approve/reject) now `RequireAuthorization()` (JwtBearer mirroring Bank.Api — `MapInboundClaims=false`, audience `bank-api`, **no realm change** — the forwarded Bank.Web token already carries `aud: bank-api` via the `bank-claims` scope) and are **tenant-scoped in-query**: the caller's tenant is read from the validated token `tenant` claim (missing → 403), the list is filtered to it, and a cross-tenant get/approve/reject returns **404 before any status check** (no existence oracle); `create` binds tenant from the token, never the body. Every other governance endpoint stays **anonymous** so the Compliance service + intra-cluster reads keep working. `Bank.Web` forwards the user token to the governance client; `AppHost` injects Keycloak authority/audience + `WaitFor(keycloak)`. `RbacPolicy.Create` now fails closed on empty/duplicate roles/permissions. Doc `docs/governance/tenant-scoping.md`. `dotnet build` 0/0; full solution `dotnet test` **1063/1063** (Governance +32, Pdp +6, Bank.Web +2). GPT-5.5 rubber-duck R1 Go → R2 Go (query-level tenant filter per Copilot) + Copilot (5 findings resolved) + plan-vs-impl GO. LRN-049 + LRN-044 flipped to **applied**; a handler-integration regression test (needs an EF-InMemory/TestHost harness) is a documented follow-up.

**CS15 (AuthZ playground + audit explorer) complete** (PR #84, squash-merged 2026-07-04 as `db058f2`). Two Bank.Web surfaces make the fine-grained authz layer inspectable, plus their backing PDP/audit APIs. **PDP fan-out** (`POST /api/authz/playground/fanout`, `PlaygroundFanoutService`): runs ONE `AccessRequest` across every registered engine (in-process `reference/aspnet/casbin/cedar`; opt-in `opa/openfga` fail closed to an *Unavailable* row) returning per-engine `{decision, reasons, obligations, explanation, latencyMs, traceId, available}` + cross-engine `allAgree` — a **non-audited what-if** (direct factory→provider, never `PdpDecisionService`, mirrors CS17); blank engine tokens are ignored (all-blank ⇒ all engines) and a provider throw is logged server-side + returns a **sanitized** message (anonymous-endpoint info-leak). **Audit query**: a `?sequence=` filter on `GET /api/audit/entries` (closes the ingest `Location`-header gap; tamper-evident chain untouched). **Bank.Web UI** (Blazor Interactive Server, `[Authorize]`): `/playground` (form + presets + engine selection + comparison table incl. obligations + replay pre-fill) and `/audit` (filters + live hash-chain verify badge + "Replay in Playground"); new fail-closed `IAuditClient` + `PdpClient.FanoutAsync`; `bank-web` now `.WithReference(audit-service)`. **Replay = "open in Playground"** pre-filled with the captured audit fields + recorded decision (the CS13 row omits ABAC inputs incl. a distinct resource tenant/branch, so a naïve auto-replay would mislead) — **faithful 1:1 replay deferred** (LRN-053). Doc `docs/product/authz-playground-and-audit-explorer.md`. `dotnet build` 0/0; full solution `dotnet test` **1132/1132** on the merged state (CS15 added ~66 tests). GPT-5.5 rubber-duck **R2–R5 all Go** + **3 Copilot rounds** (obligation-mirror + replay-doc scope → blank-engine-name fallback → sanitize errors + drop unused inject; final Copilot pass 0 threads) + plan-vs-impl GO. Merged via a verified local **trial-merge** against the concurrently-advanced main (CS22/CS29) to clear the LRN-035 semantic-merge risk. New learnings LRN-053..057. **Repo was made public** this session to restore GitHub Actions capacity (private free-tier billing block); re-evaluate the private-tier constraint record.

**CS19 (agent + non-human access + on-behalf-of delegation) complete** (PR #85, owner-admin-squash-merged 2026-07-04 as `4704143`). Authorizes AI agents / MCP tools / workload identities alongside humans with **constrained delegation** (intersection, not impersonation). The PDP `Subject` gains an optional `Actor(Type,Id,Scopes)` (null ⇒ direct human/service path, **byte-identical**); `ReferenceDecisionProvider` permits an OBO call only when the effective user is permitted **AND** the agent holds the required delegated `agent.bank.*` scope, else Deny `DelegationScopeMissing` (rule `delegation-scope`) — a base user-Deny passes through, so **an agent can never exceed the user**. The audit event gains additive `SubjectType`/`ActorId`/`ActorType` (Audit.Service hash-chain untouched); the logging sink **CR/LF-sanitizes every rendered field** (CWE-117 log-forging, surfaced by CodeQL when the repo went public — incl. OpenFGA policy-reference tuples). Keycloak adds a `bank-agent` client-credentials client (`agent-claims` ⇒ `subject_type=agent`; delegated `agent.bank.*` — default `read`, write/approvals **optional/per-token** = scoped/time-boxed) and a `service-claims` scope marking `bank-workload` `subject_type=service` (non-human acting as itself). Fail-closed OBO claim helpers `ActorClaims` (Bank.Api) + `GatewayActorClaims` (edge) read `subject_type`/`on_behalf_of`/`sub` and resolve delegation **only** for the ordinal `{agent,service}` allow-list — the OBO seam **CS21 reuses** (no reverse dep; RFC 8693 `act` is the documented production/exchanged-token form). Bank.Web `AgentAccess` page renders human-direct vs agent side-by-side against the live PDP; `AgentAccessScenarioCatalog` covers OBO/non-human cases. Doc `docs/authz/agent-and-nonhuman-access.md`. `dotnet build` 0/0; full solution `dotnet test` **1064/1064** pre-merge. GPT-5.5 rubber-duck R1 NF (OBO gate accepted any non-user `subject_type`) → R2 Go → R3 Go → R4 Go → R5 NF (`PolicyReferences` log-forging path) → R6 Go + **3 Copilot rounds** + **CodeQL** (log-forging **fixed, not dismissed**) + plan-vs-impl GO. Merged via owner **admin-merge** (all ruleset reqs met; stale duplicate check-runs left a spurious `BLOCKED`). New learnings LRN-058..060.

**CS30 (supply-chain: NuGet audit suppression & transitive-pin re-evaluation) complete** (PR #95, squash-merged 2026-07-04 as `23e4036`). Re-evaluated the repo's 15 `NuGetAuditSuppress` entries + the MSBuild transitive pin (fixes **LRN-003** + **LRN-005**), **remediating rather than suppressing**: OpenTelemetry bumped 1.14.0→**1.16.0** (Instrumentation.Runtime 1.15.1; clears the 4 OTel advisories, first-patched 1.15.2/1.15.3) and a CPM **transitive pin `MessagePack 2.5.302`** (clears the 11 MessagePack advisories on transitive 2.5.192 via `Aspire.AppHost.Sdk`) let **all 15 suppressions be dropped** from `Directory.Build.props`; the `Microsoft.Build.*` **17.14.28** pin is **retained** (dropping it reverts EF Core Design rc.1 to vulnerable 17.14.8 / GHSA-w3q9-fxm7-j8fq) with a dated re-confirmation. `dotnet list --vulnerable --include-transitive` is **clean across all 20 projects** and `dotnet build` is 0/0 under `TreatWarningsAsErrors`; `dotnet test` **1063/1063**. New durable doc `docs/security/nuget-audit-reeval-2026-07-04.md` records the disposition + the drop-triggers (EF Core RC1→GA; stable Aspire with patched MessagePack). GPT-5.5 rubber-duck **R1–R3 all Go** + **3 Copilot rounds** (durable-doc clickstop-link durability + OTel first-patched wording, both fixed) + plan-vs-impl GO. LRN-003 + LRN-005 flipped to **applied**.

**CS31 (engine-adapter test seams + degenerate-input parity) complete** (PR #100, squash-merged 2026-07-04, on main as `66fbc7d`). Fixes **LRN-038** + **LRN-033** + **LRN-031** (all flipped to **applied**). (1) Extracted `IOpenFgaCheckClient` — the one-member forward-`Check` seam `OpenFgaProvider` depends on (DI resolves the same `OpenFgaRebacService` singleton, one bootstrap) — so the ReBAC permit/deny `DecisionExplanation` (engine/DeterminingRule/tuple ref) is unit-testable **offline** via a `FakeOpenFgaCheckClient` (LRN-038). (2) Added degenerate-input (null/empty/whitespace) fail-closed **parity tests** asserting equivalence to the `ReferenceDecisionProvider` oracle (Decision + `Reasons[0].Code`) for every in-process engine (reference/aspnet/casbin/cedar); OPA/OpenFGA boundaries covered at the mapper level, OPA ABAC degenerate parity stays in the Rego suite; the shared 22-scenario `FintechScenarioCatalog` is intentionally unchanged (LRN-033). (3) Added an `OpenFgaOptions.AuthorizationModelId` **pin** (pin-when-configured else write-then-pin — avoids per-boot model-version growth) + targeted per-tuple existence reconciliation replacing read-all (LRN-031). Doc `docs/authz/adapter-test-seams-and-degenerate-parity.md`. `dotnet build` 0/0; PDP `dotnet test` **726/726** on the merged state. GPT-5.5 rubber-duck R1 Go + 2 narrow re-attests (Copilot nits + rebase onto CS15/CS19/CS30) + Copilot (3 nits resolved) + plan-vs-impl GO.

**CS32 (observability & audit-event enrichment; aspire-run 500 triage) complete** (PR #112, squash-merged 2026-07-04, on main as `6f596a2`). Fixes **LRN-013** and triages **LRN-014** (both flipped to **applied**). (1) **Edge-denial enrichment:** `GatewayAuditMiddleware.ResolveRouteMetadata` falls back from `IReverseProxyFeature` (unset on a short-circuit 401/403 deny) to the matched endpoint's YARP `RouteModel` metadata, so deny events carry RouteId/RequiredScope; the allow/routed path is unchanged (feature still primary). (2) **Uniform non-authz skip:** both `GatewayAuditMiddleware` and `BankAuthorizationAuditMiddleware.ShouldAudit` skip unmatched-404 (null endpoint) + method-mismatch-405 (routing non-decisions); a matched-endpoint business 404/409 stays audited as a genuine allow. (3) **aspire-run 500 (LRN-014):** an offline probe proved the OTLP exporter is **request-path isolated** (a dead OTLP endpoint leaves `/alive`+`/api/*` at 200) and all 7 exporting services already `WaitFor(observability)`, so no missing-`WaitFor` fix exists — documented as a non-reproducible early-RC/environmental interaction with a clean-machine confirmation runbook; **`AppHost.cs` unchanged**. No authz decision or Audit.Service hash-chain change. Docs `docs/observability/aspire-run-500-triage.md` + `docs/authz/audit-enrichment-and-skip-contract.md`. `dotnet build` 0/0; full solution `dotnet test` **1347/1347**. GPT-5.5 rubber-duck R1 Go + Copilot (1 pre-existing-link nit → doc-hygiene follow-up) + plan-vs-impl GO.

**CS34 (log-forging / CWE-117 sanitization) complete** (PR #113, squash-merged 2026-07-04, on main as `c40e1a7`). Neutralizes the 4 CodeQL `cs/log-forging` alerts that the `code_scanning` ruleset rule (`alerts_threshold: errors`) was blocking every merge on. A shared `public AuthzEntitlements.ServiceDefaults.LogSanitizer.Clean` (CR/LF→space, null-safe — the CWE-117 barrier already proven in `LoggingPdpDecisionAuditSink`, which CodeQL does not flag) now wraps every rendered string arg at the three flagged `ILogger` sites (`OpenFgaProvider` fail-closed `LogWarning`; `GatewayAuditMiddleware` + `BankAuthorizationAuditMiddleware` audit `LogInformation`); `StatusCode`/`TimestampUtc` stay unwrapped; the sink's private `Clean` now delegates to the shared helper. 7 unit + 3 real-path behavior tests. `dotnet build` 0/0; full solution `dotnet test` **1357/1357**. Rebased onto CS32 (#112) — CS32's `ShouldAudit` skip/enrichment preserved; the Bank behavior test uses `SetEndpoint` to satisfy CS32's endpoint guard. GPT-5.5 rubber-duck impl-review Go + rebase-delta Go + plan-vs-impl GO. Landed via one justified admin-merge (the fix clears the very alerts blocking it — chicken-and-egg); once main's CodeQL re-runs and closes them, PRs merge without the `code_scanning` block.

**CS33 (consolidate durable learnings into project-local doc blocks) complete** (PR #119, squash-merged 2026-07-04, on main as `8c71a23`). **Completes the CS28h harvest arc.** Propagated the 37 durable how-to learnings into the project-local doc blocks (the "knowledge lives in the repo" doctrine): **25** dotnet/Aspire/Keycloak/Blazor/EF/PDP-adapter/security conventions into `CONVENTIONS.md` `conventions.project` and **12** review/Copilot/merge/citation/CI-evidence gates into `REVIEWS.md` `reviews.project-gates` (each bullet LRN-cited), then flipped all 37 (LRN-001/002/004/007–012/015–030/032/034/036/037/039/041–043/045–048) from `open` to `applied` (disposition = doc block + PR #119 + `8c71a23`). `harness lint` 0/0; `harness sync --mode=check` **no drift** (only the allowlisted local blocks edited — no managed-core change). GPT-5.5 rubber-duck R1 Needs-Fix (LRN-002 citation overstatement) → R2 Go → narrow re-attest + Copilot (3 nits resolved) + plan-vs-impl R1 Needs-Fix (missing landing SHA) → R2 GO.

**CS23 (comparison matrix + market survey) complete** (content PR #111 squash-merged 2026-07-04 as `cf98afd`; matrix-dimensions follow-up PR #117 as `fcbbdc8`). The evaluation-lab documentation, produced by six parallel background sub-agents (Claude Opus 4.8; entitlements on the 4.7 fallback). (1) A grounded **comparison matrix** (`docs/eval/comparison-matrix.md`) scoring the six integrated PDP engines (`reference/aspnet/casbin/cedar` in-process + `opa/openfga` live) plus the two entitlements providers (OpenFeature InMemory + Unleash) across all **12** named dimensions — models, consistency, latency, reverse-index, policy language, testability, auditability, ops burden, .NET support, AuthZEN alignment, licensing/maturity, hosting — integrated cells grounded in shipped code/docs + the CS24 benchmark baseline, surveyed engines summarized with links. (2) A **market survey** index + four category deep-dives (`docs/eval/market-survey.md` + `docs/eval/survey/{relationship-based-zanzibar,policy-and-decision-engines,entitlements-and-flags,authzen}.md`) covering the Zanzibar/ReBAC family (OpenFGA/SpiceDB/Keto/Topaz/Permify/Warrant-WorkOS), policy engines (OPA/Cedar-AVP/Casbin/Oso/Cerbos/Permit.io), entitlements/flags (OpenMeter/Stigg/OpenFeature/Flagsmith/Unleash/Entra), and the OpenID **AuthZEN** standard — each item with strengths/weaknesses/when-to-use + primary-source citations. (3) Six **ADRs** (`docs/adr/README.md` + `0001..0006`) formalizing the shipped decisions (unified AuthZEN PDP, reference oracle, multi-engine adapters + config swap, OpenFGA ReBAC, OpenFeature entitlements, fail-closed + audit-first). `harness lint` 22/0; 157/157 relative links resolve. GPT-5.5 rubber-duck caught real fact-claim fixes (OpenFGA consistency overstatement; AuthZEN response field is `decision` not `allow`; obligations ride in AuthZEN `context`) + Copilot; the plan-vs-impl gate first flagged 2 missing matrix dimensions (added in #117) then **GO**. Documents the project ADR-format extension (`Alternatives considered` + `When to use`) in the CONVENTIONS project-local block. New learnings LRN-061..063.

**CS25 (managed-vs-self-host TCO + cloud move) complete** (PR #114, squash-merged 2026-07-04, on main as `d311d1a`; close-out PR #123). Docs-only Eval-lab deliverable. (1) `docs/eval/managed-vs-selfhost-tco.md` — a TCO/ops comparison of the five managed offerings (Auth0/Okta FGA, AuthZed Cloud, Oso Cloud, Permit.io, Amazon Verified Permissions) vs self-hosted OSS, grounded in the lab's real stack (in-process `reference`/`aspnet`/`casbin`/`cedar`, out-of-process `opa`/`openfga`, the six shared-Postgres logical DBs, Aspire, OTLP→Grafana), with a per-option cost/ops/lock-in table, an **Azure cloud-move** section feeding CS27 (ACA vs AKS, Azure Database for PostgreSQL Flexible Server, OTLP re-point, and the load-bearing **AVP-is-AWS-only** constraint), and a dated honesty caveat + Sources (pricing at the model/cost-driver level, not stale figures). (2) `docs/adr/0007-self-host-first-authz-with-managed-optionality.md` — the self-host-first-with-managed-optionality decision (Accepted; `Realized in` shipped-only, Azure/CS27 marked forward), bidirectionally cross-referenced with CS23's `comparison-matrix.md` + `market-survey.md`. Claimed while CS23 was in flight (cross-refs/ADR initially deferred); CS23 merged mid-CS (PR #111) so the ADR + cross-refs completed in the same content PR. Independent GPT-5.5 rubber-duck R1 Go → R2 Needs-Fix (unshipped CS27 in a shipped-only `Realized in`) → R3 Go → Copilot (same field, deeper) → R4 Go; plan-vs-impl **GO**. New learning **LRN-064** (author eval-lab TCO docs at the pricing-model level with a dated caveat + Sources). No code/tests.

**CS26 (expansion engines) complete** (SpiceDB content PR #134, Cerbos content PR #139; close-out PR #151). Two more PDP engines shipped behind the CS05 `IAuthorizationDecisionProvider` seam: **SpiceDB** (ReBAC, the OpenFGA head-to-head counterpart) and **Cerbos** (out-of-process full-decision over gRPC, the OPA counterpart). Both are opt-in **pinned** containers (`authzed/spicedb:v1.54.0`, `ghcr.io/cerbos/cerbos:0.53.0`) wired `WithExplicitStart` with **no hard `WaitFor`**, so the default `reference` path and the Docker-free build/test/`aspire run` loop are unchanged. Both register in `AddPdp` so `PlaygroundFanoutService` auto-includes them (dynamic enumeration over all registered providers) plus explicit `Playground.razor` opt-in entries; adapter docs (`docs/authz/spicedb-adapter.md`, `docs/authz/cerbos-adapter.md`) and the SpiceDB-vs-OpenFGA head-to-head (`docs/eval/spicedb-vs-openfga.md`) shipped with them. **Rescoped:** Keto+Topaz → **CS46**, Oso → **CS47** (de-scope — no pinnable in-process .NET path), and the cross-cutting OBO/delegation/break-glass fail-closed guard surfaced by the Cerbos review → **CS45** (all filed, PR #144). SpiceDB **759/759** offline; Cerbos **874/874** offline + a 22-scenario Decision+reason parity run against a real container. Independent GPT-5.5 reviews-of-record on both PRs. New learnings **LRN-072..076**.

**CS37 (weekly LRN harvest) complete** (content PR #135, on main as `c2bea79`): LRN-050..064 dispositioned — durable how-tos consolidated into the CONVENTIONS.md/REVIEWS.md project-local blocks and flipped applied; LRN-059/062 confirmed already-landed (CS34 / PR #111); LRN-057 filed as planned CS36 (audit request-snapshot); LRN-035/040 remain deferred (enforcement owned by yoga-ae-c5's CS40). Content by an Opus-4.8 sub-agent; independent GPT-5.5 diff review + plan-vs-impl review both GO.

**CS36 (audit request-snapshot for faithful decision replay, LRN-057) complete** (content PR #140, on main as `8fe3911`; close-out follows). The PDP persists the full `AccessRequest` as a **non-hashed** `RequestSnapshot` column on the CS13 tamper-evident audit store (`ComputeRowHash`/`AuditPayload` byte-identical — a regression test proves null-vs-populated → same `RowHash`), and the Audit Explorer replays a recorded decision **1:1** (roles/scopes/amount/maker/status/resource tenant+branch/OBO actor) via sequence-fetch, with an honest best-effort fallback + non-authoritative labeling; fail-open-to-null + a configurable 16 KB size guard clamped to the column width, both logged (sanitized). Independent GPT-5.5 **security review PASS** + review-of-record (R1/R2 NEEDS-FIX on replay fidelity/config-cap/banner → R3 GO; 2 Copilot-caught fixes → GO) + plan-vs-impl **GO**; rebased onto CS21+CS26 with a green whole-solution build+test (LRN-040). **LRN-057 flipped applied.** `dotnet build` 0/0, `dotnet test` all pass, `harness lint` 0.

**CS21 (break-glass, delegation & on-behalf-of) complete** (PR #120, squash-merged 2026-07-05 as `6b1f312`). Emergency break-glass access + manager→delegate delegation + OBO enforcement, reusing the CS19 OBO seam (no duplication). The PDP stays a pure, deterministic function: break-glass/delegation grants + an injected `Now` clock ride in `EvaluationContext` (additive, null-default ⇒ the human path is **byte-identical**). **Break-glass = bounded emergency elevation, never an integrity bypass:** a base Deny for a *missing capability* (`MissingScope`/`RoleNotAuthorized`) is raised to `Permit(BreakGlassInvoked)` + a `require_break_glass_review` obligation only under an active, matching, non-blank-`GrantId`/`Justification` grant — and an **independent `PassesHardInvariants` guard** re-checks tenant / maker-checker-SoD / subject-is-maker / pending so a missing-capability denial can never *mask* (and thus bypass) a co-occurring integrity violation. **Delegation** = the CS19 OBO intersection ∧ the manager's grant `Scopes` (the delegate exceeds neither its token nor the manager's grant) ∧ an active, matching delegation grant (`DelegationNotActive`). **Heightened audit** adds `BreakGlass`/`BreakGlassGrantId`/`DelegationId` (real elevations at Warning; CS13 hash-chain untouched). **Governance.Service** owns the grant lifecycle as in-memory, time-boxed, fail-closed stores (`IsActive(now)`; mandatory review is **post-expiry only**; bounded retention that **never evicts a grant still pending review**; defensive copies; trimmed inputs) via 9 anonymous endpoints; the shared `GovernanceDecisionEmitter` + `LoggingGovernanceAuditSink` **CR/LF-sanitize** every request-derived audit field (CWE-117, mirroring CS19/CS34). **Bank.Web** `/break-glass` + `/delegation` pages (offline-testable VMs) drive the live PDP + Governance. Runbook `docs/governance/break-glass-and-delegation-runbook.md`. `dotnet build` 0/0; full solution `dotnet test` **1484/1484**. **11 GPT-5.5 rubber-duck rounds** (R1 Needs-Fix caught the integrity-masking bypass → `PassesHardInvariants`; R2–R11 Go) + **8 Copilot rounds** (store encapsulation, post-review timing, blank-grant-id fail-closed, delegated-scope ceiling, retention-vs-mandatory-review interaction, CWE-117 governance log-forging — all fixed) + plan-vs-impl **GO**. New learnings LRN-065..068.

**CS49 (docs: refresh README + ARCHITECTURE) complete** (PR #150, squash-merged 2026-07-05 as `f01475a`). `README.md` and `ARCHITECTURE.md` — which still described the pre-implementation bootstrap state (README: "Nothing is implemented yet"; ARCHITECTURE frozen at the 2026-07-02 bootstrap) — were rewritten to reflect the **shipped** system: how to run/use the lab (`aspire run` from `src/AuthzEntitlements.AppHost`, default critical path vs opt-in engines, seeded-user login, the maker-checker walkthrough, engine comparison via the AuthZ Playground + `Pdp:Provider` + `scenarios/verify`, the Audit Explorer + `/api/audit/verify`, governance JIT/break-glass/delegation, Grafana), the current **system architecture** (11-project component map, an updated top-level mermaid, the four-layer + audit model, the 8-engine PDP adapter seam — 6 full-fintech + 2 ReBAC — data stores, observability, security posture, decision log, status/roadmap), and **four key data-flow sequence diagrams** (transaction POST end-to-end; standalone PDP evaluate + scenario parity; tamper-evident audit append + verify; governance JIT + SoD-via-PDP). Docs-only (no code/tests). Reviewed across **13 independent GPT-5.5 rounds** + a plan re-attestation + Copilot (fixes incl. ReBAC-vs-fintech parity, OpenFGA HTTP transport, Grafana anonymous-Editor kiosk, `/api/authz/rebac/*` OpenFGA-only, entitlements create-only before the `>=10,000` threshold, blank `Pdp:Provider` → default `reference`); the WORKBOARD Active-Work row was **descoped** to keep the docs PR immune to concurrent WORKBOARD churn (the CS record is the source of truth). `harness lint` 22/0; `harness sync --mode=check` clean.

**CS48 (local stack validation + demo/lab readiness) complete** (PR #160, squash-merged 2026-07-05 as `02e546e`; close-out PR pending). Executed the local validation that gates the held CS27/CS43/CS44: `dotnet build` **0/0** and `dotnet test` **1648/0/0** across the nine suites (incl. all PDP engine **adapters** via `Authz.Pdp.Tests`), plus an `aspire run` boot smoke that **found + fixed a demo-blocking defect** — the AppHost crashed on startup because two container resources (`unleash`, `openfga`) shared a name with the same-named shared-Postgres databases (Aspire names are case-insensitive + must be unique); latent because no CI runs the AppHost. Fixed in-band (Decision #8) by renaming the containers to `unleash-server`/`openfga-server` (`src/AuthzEntitlements.AppHost/AppHost.cs`); post-fix the AppHost boots, the dashboard serves, and `postgres`/`keycloak`/`observability` come up healthy. Deliverables `docs/validation/local-stack-validation.md` + `docs/demo/local-demo-runbook.md`. **Observability assessment:** current Grafana/OTLP + Aspire dashboard are sufficient for the demo → OpenMeter (CS43/CS44) **not warranted yet**. Live opt-in-engine + authenticated-UI drive-through explicitly **deferred** (documented close-out scope amendment) to the runbook + a follow-up (**LRN-078**: add an AppHost application-model CI smoke test). Independent GPT-5.5 review-of-record (Go) + Copilot + plan-vs-impl **GO**. **The CS27/CS43/CS44 hold precondition #1 (documented local validation) is now SATISFIED; the holds still require explicit user go-ahead + an observability-warrant decision to lift (Decision #7 — not lifted here).**

**Next claimable:** with **CS11, CS13, CS14, CS15, CS16, CS17, CS18, CS19, CS20, CS21, CS22, CS23, CS24, CS25, CS26, CS28, CS29, CS30, CS31, CS32, CS33, CS34, and CS48 DONE**, **CS27 was rescoped 2026-07-04** and split: app Azure deploy stays **CS27**, full OpenMeter (local) is **CS43** (CS10+CS12), Azure OpenMeter is **CS44** (CS25+CS27+CS43). **⛔ CS27, CS43, and CS44 remain HELD** — do NOT claim without explicit user confirmation: their precondition #1 (documented local validation) is now satisfied by CS48, but lifting still needs a maintainer go-ahead + observability-warrant decision (cloud/Azure deploy is out of scope at this time; CS48's assessment says OpenMeter not warranted yet). Each carries a `## Hold / claim gate` + an `open` LEARNINGS backstop (LRN-069/070/071, `claim_area: cs27/cs43/cs44`) and is registered HIGH-RISK in `harness.config.json`. Also ready: the CS26 follow-ups — **CS45** (OBO/delegation/break-glass adapter guard — claim first), **CS46** (Keto+Topaz adapters — depends on CS45), and **CS47** (Oso de-scope disposition). **LRN-078** (open) recommends an AppHost CI smoke test — candidate follow-up CS. Check each file's `**Depends on:**` and the WORKBOARD before claiming. `harness lint` is green and the remaining CSs carry an independent GPT-5.5 `## Plan review` attestation.

## Constraints

See `.harness-known-constraints.md` for repository tier and disposition (detected 2026-07-02T18:34:27.975Z).

## Architecture pointer

See [ARCHITECTURE.md](ARCHITECTURE.md).

_(Keep this section as a short pointer. All architecture detail lives in
`ARCHITECTURE.md`. Add a sentence or two summarising the top-level design only
if it helps orient a reader skimming this file.)_

## Blockers / open questions

- **Branch-protection ruleset: applied + hardened (CS40).** The repo is now **public** and the "push to main" ruleset (id `18513457`) is applied with required checks [`build-test`, `structural-gate`, `read-only-gates`, `copilot-review-attached`, `independence-invariant`] + `required_review_thread_resolution` + Admin-only break-glass bypass, so a compliant PR merges **bypass-free** once green (verified end-to-end via PR #143). The CS28 `.NET` build/test is now required-to-merge (closes the old "advisory CI only" gap). **Residual follow-up:** the committed `infra/main-protection-ruleset.json` spec predates the public cutover and was never applied; it drifts from the live ruleset and is harness-sync-managed, so reconciling it is a CS40 follow-up. Policy: `docs/ci/review-pr-hardening.md`.
- **Docker daemon must be running** for the container-based engines/infra (Docker Desktop is installed).
- **Plan review: complete** — all 27 CSs independently reviewed (GPT-5.5) and attested (Go / Go-with-amendments); 13 CSs were amended to fix dependency/scope gaps found in review. No plan-review blocker remains.
- **Open decision:** whether to promote SpiceDB from the Phase-7 expansion (CS26) into the Phase-2 core for a direct OpenFGA head-to-head. The SpiceDB-vs-OpenFGA head-to-head data now exists (`docs/eval/spicedb-vs-openfga.md`, shipped with CS26) to inform this decision; the decision itself stays open.

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
| CS27 | Azure deployment of the app (azd → ACA) ⛔ HELD | Expansion | CS10, CS12, CS14, CS15, CS25 |
| CS43 | Full OpenMeter metering (local) ⛔ HELD | Expansion | CS10, CS12 |
| CS44 | Azure deployment of OpenMeter ⛔ HELD | Expansion | CS25, CS27, CS43 |
| CS48 | Local stack validation + demo/lab readiness (gates CS27/CS43/CS44) | Validation | CS12, CS14, CS15, CS21, CS26 |

### Parallelization (waves)

- **Wave 1 (serial foundation):** CS01 -> CS02.
- **Wave 2 (parallel after CS02):** CS03 (Identity), CS05 (PDP hub), CS10 (Entitlements), CS12 (Observability).
- **Wave 3 (after CS05):** engine adapters CS06, CS07, CS08, CS09 (parallel); CS04 (after CS03); CS13 audit (after CS05); CS11 (after CS02 + CS08).
- **Wave 4 (after engines):** CS16, CS17, CS20 (need engine behavior); CS24 (engines + CS12); CS22 (CS11+CS12+CS13); CS14 product (CS03+CS04+CS06+CS10+CS11); CS15 playground (engines + CS13); CS18 (CS04+CS05).
- **Wave 5:** CS19 (CS13+CS14), CS23 (CS15+CS24), CS26 (CS05+CS15).
- **Wave 6:** CS21 (CS19+CS13+CS14), CS25 (CS23+CS24), CS27 (CS14+CS15+CS25).
- **Wave 7 (OpenMeter split, 2026-07-04):** CS43 (full OpenMeter local — CS10+CS12); CS44 (OpenMeter on Azure — CS25+CS27+CS43, after CS27+CS43). **⛔ All three (CS27/CS43/CS44) are HELD** pending detailed local validation + demo/lab observability justification + explicit user go-ahead — see each file's `## Hold / claim gate` (guarded by LRN-069/070/071 + `reviews.high_risk_clickstops`).

A fleet of orchestrators can each `harness claim` an independent, dependency-satisfied CS.
