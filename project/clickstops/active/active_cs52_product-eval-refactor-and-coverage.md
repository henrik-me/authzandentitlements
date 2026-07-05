# CS52 — Product-wide implementation evaluation: refactoring catalog + path to 95% test coverage

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs52/content
**Started:** 2026-07-05
**Closed:** —
**Filed by:** yoga-ae-c3 on 2026-07-05 — user request: "run through each area of the product, evaluate the implementation and suggest refactorings, create a CS with the suggestions ... include evaluation on how to get to 95% test coverage." Evaluation performed by four parallel read-only sub-agents (Claude Sonnet 4.6, high reasoning), one per area-cluster, over `main` @ 2fe2bb8.
**Depends on:** none (evaluation/plan only; implementation waves sequence at claim time)
**Hold:** ⛔ HELD — do NOT continue CS52's remaining work (Waves 0b/A/B/C) or file/claim any CS52 wave sub-CS without explicit maintainer confirmation. Wave 0 (coverage measurement infrastructure) is done + merged (PRs #177/#179); everything after it is paused pending an explicit "go". See the **Hold / claim gate** section below.

## Hold / claim gate

⛔ **This CS is HELD. Wave 0 (coverage measurement infrastructure) is complete and merged (PRs #177 + #179), but all remaining work is paused: do NOT continue Wave 0b / A / B / C, and do NOT file or claim a CS52 wave sub-CS, until a maintainer explicitly lifts this hold with a "go".** (A default dry-run `harness claim` preflight/harvest scan is harmless.)

**Preconditions — all must be true before continuing:**

1. **Explicit maintainer go.** A maintainer has explicitly confirmed the next CS52 wave is in scope and lifted this hold (record who + when here when lifting).
2. **Not time-reclaimable.** This hold overrides the default 7-day WORKBOARD reclaimable threshold — CS52 must NOT be picked up by another orchestrator on staleness alone; only an explicit maintainer "go" lifts it.

**Guard / enforcement (layered):** (1) this `## Hold / claim gate` is the always-on contract — continuing or claiming CS52 requires reading this file, so the hold is unavoidable; (2) the `WORKBOARD.md` Active Work row is set to `⏸ Paused` with a HELD reason; (3) `LEARNINGS.md` **LRN-085** (`status: open`, `claim_area: cs52`) is a before-claim/harvest backstop. **To lift:** record the maintainer confirmation above, flip LRN-085 (`status` + a `**Disposition:**`), restore the WORKBOARD row to `🟢 Active`, and remove this ⛔ block.

## Goal

Deliver a single, reviewed catalog of concrete, evidence-based refactorings for **every** area of the product, each with a detailed rationale, and a phased, enforceable plan for reaching **95% line coverage (90% branch)** on the business-logic assemblies. This CS **files the plan**; implementation is deliberately deferred and sequenced into reviewable waves at claim time (see Decision #1). No production code changes land under this CS.

## Background

- **Scope of the product (11 projects, ~20.9k src LOC / ~17.1k test LOC, 1679 tests, `main` green @ 2fe2bb8).** Measured src/test size and current test counts per project:

  | Area | src LOC | tests | test/src LOC | Current posture |
  |---|---|---|---|---|
  | Authz.Pdp | 6675 | 922 | 1.27 | Heavily tested; strong fail-closed posture |
  | Governance.Service | 3640 | 174 | 0.55 | Good domain tests; endpoint layer thin |
  | Bank.Api | 2955 | 96 | 0.37 | Domain tested; **no HTTP-level tests** |
  | Bank.Web | 1887 | 172 | 1.22 | ViewModels/clients tested; **no Razor page tests** |
  | Entitlements.Service | 1567 | 43 | **0.20** | **Most under-tested**: only pure domain |
  | Compliance | 1233 | 64 | 0.53 | Reporters partially tested |
  | Audit.Service | 1137 | 72 | 0.63 | Hash-chain well tested; endpoint thin |
  | Benchmarks | 862 | 52 | 0.67 | Runner tested; `EngineCatalog` uncovered |
  | Edge.Gateway | 602 | 82 | 1.55 | Well covered |
  | AppHost | 280 | 2 | smoke | App-model smoke only |
  | ServiceDefaults | 117 | **0** | **0** | **Zero tests** on a cross-cutting kingpin |

- **No coverage tooling exists today.** `Directory.Packages.props` pins `Microsoft.NET.Test.Sdk 18.7.0` + `xunit 2.9.3` with **zero** `coverlet`/`ReportGenerator`/`.runsettings` references, and `dotnet-ci.yml` runs `dotnet test ... --no-build` with **no `--collect`**. There is therefore **no way to measure coverage** — 95% cannot be targeted before measurement infrastructure exists (Decision #4).
- **`dotnet-ci.yml` is project-owned (seeded), not harness-managed** — a coverage gate can be added to it under this arc without a template CS.
- **Evaluation method.** Four parallel read-only sub-agents evaluated disjoint clusters — (1) Authz.Pdp; (2) Bank.Api/Bank.Web/Entitlements.Service; (3) Governance/Audit/Compliance; (4) Edge.Gateway/AppHost/ServiceDefaults/Benchmarks — each producing an implementation assessment, refactorings with rationale, and coverage gaps. The orchestrator evaluated cross-cutting build/CI/coverage config directly. The full per-item catalog is in `## Deliverables`.
- **A central finding: coverage and refactoring are coupled.** ~40% of the missing coverage is *unreachable* without testability seams — endpoint handlers take `DbContext` directly, `DateTimeOffset.UtcNow` is stamped inline, feature checks use the global `Api.Instance` singleton, git SHA capture uses `Process.Start`, and live probes open real `TcpClient`s. Tests for those paths cannot be written until the seam exists, so refactor + test ship together (Decision #7).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Delivery shape | This CS files the **plan/catalog**; implementation is sequenced into reviewable **waves** (claimed as sub-CSs `CS52a…`/task rows), not one PR | ~80 refactorings + a coverage program across 11 projects is far too large for one reviewable PR; a catalog + waves keeps each PR scoped and keeps `main` green. |
| 2 | Change safety | **Behavior-preserving refactors first**; every item tagged `behavior-change` inline is individually gated behind its own review — the complete set is: input validation→400 (R-BANKAPI-1, R-ENT-4, R-ENT-6, R-AUDIT-2), options `ValidateOnStart` (R-PDP-6), obligation-aware `AllAgree` (R-PDP-8), new/added API surface (R-BANKWEB-3, R-EDGE-3), wire-contract/schema change (R-ENT-8, R-BENCH-5), role-filter hardening (R-BANKWEB-5), proxy timeout (R-EDGE-8), offset cap (R-AUDIT-5), duration cap (R-GOV-2), quota compensation/saga (R-BANKAPI-8), markdown escaping (R-COMP-2), fail-closed on blank principal (R-COMP-5), evidence-case addition (R-COMP-1) | Separates mechanical cleanups (safe, batchable) from semantic changes (need targeted review + possible consumer coordination). |
| 3 | Audit hash-chain integrity | **No refactor may change the hashed field set** of `AuditHashChain.ComputeRowHash` without a chain-format-version migration; the audit refactors that touch hash-chain code — R-AUDIT-1 (span-hash inside `ComputeRowHash`), R-AUDIT-3 (`TimeProvider` on the **non-hashed** `ReceivedAtUtc`), R-AUDIT-7 (hex validator in `AuditCheckpoint.TryParse`, **not** `ComputeRowHash`) — are verified non-hashed-set-changing | Tamper-evidence guarantee; a silent hash-input change invalidates every stored chain (LRN audit-hash-chain). |
| 4 | Coverage tool | `coverlet.collector` via `dotnet test --collect:"XPlat Code Coverage"` + a `coverage.runsettings` + **ReportGenerator** merge + a threshold check | Zero-config, CPM-friendly, emits standard Cobertura, **no new runtime deps** (test-only, honors the zero-runtime-dep rule); ReportGenerator gives per-assembly floors + HTML. |
| 5 | 95% scope & metric | Target **95% line / 90% branch** on business-logic assemblies (Authz.Pdp, Bank.Api, Bank.Web, Entitlements.Service, Governance.Service, Audit.Service, Compliance, Edge.Gateway, ServiceDefaults). **Exclude** EF `Migrations/*` (generated), `AppHost` (topology — asserted by app-model smoke tests, not line coverage), `Benchmarks` (perf harness), and `[GeneratedCode]`/`[ExcludeFromCodeCoverage]` | A blanket 95% over generated/orchestration code is a false target that drives low-value tests; scoping to logic assemblies makes the number meaningful. |
| 6 | Enforcement path | **Ratchet, not big-bang**: capture the real per-assembly baseline, set the CI floor at baseline (non-regression), then raise floors per wave to 95%; ship the gate report-only for one PR, then flip to blocking | Avoids an impossible single jump; makes progress enforceable and monotonic (floors never lowered). |
| 7 | Refactor/test coupling | Testability seams (`TimeProvider`, repository interfaces, `IFeatureGate`/`IGitShaProvider`/`ISocketProber`, extract-page-handlers) ship **with** the tests they unblock | ~40% of gaps are unreachable without a seam; pairing them prevents "refactor now, test later" debt. |
| 8 | New test project | Add `tests/AuthzEntitlements.ServiceDefaults.Tests` (CPM-pinned xunit, `FrameworkReference Microsoft.AspNetCore.App`) | ServiceDefaults has **0 tests** yet gates OTLP export + health mapping for every service; a regression there is silent and product-wide. |

## Deliverables

Deliverable groups. Each per-area refactoring carries an ID `R-<AREA>-<n>`, an effort tag (S/M/L), a `preserving`/`behavior-change` tag, its rationale (problem + benefit), and file citations. "Coverage gaps" per area name the untested code and the concrete tests to add.

### D0 — Coverage measurement infrastructure (Phase 0; prerequisite)

- Add `coverlet.collector` as a CPM `PackageVersion` and reference it from every test project (via a new `tests/Directory.Build.props` to avoid per-project drift).
- Add `coverage.runsettings` (XPlat Code Coverage data collector) with exclusions: `ExcludeByFile=**/Migrations/*.cs`; `ExcludeByAttribute=Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverage`; exclude assemblies `[*.AppHost]*`, `[*.Benchmarks]*`, `[*]*.Migrations.*`.
- Add a `ReportGenerator` invocation (dotnet local tool, `dotnet-tools.json`) that merges per-project Cobertura into one summary + HTML.
- Capture and record the **baseline** per-assembly line/branch % (the number that does not exist today).
- **Validate `coverlet.collector` support on the .NET 10 RC SDK** (`10.0.100-rc.1`) as the first Phase-0 step; do not flip the CI gate to blocking until the collector emits correct Cobertura on this SDK.
- Wire a coverage step into `dotnet-ci.yml`: collect + merge + per-assembly threshold check. **Report-only** first, then flip to **blocking** (Decision #6).

### D1 — `docs/testing/coverage.md`

How to run coverage locally, the scoped assemblies + exclusions (Decision #5), the ratchet policy + current floors, and the sub-CS wave plan.

### D2 — `tests/AuthzEntitlements.ServiceDefaults.Tests` (new project, Decision #8)

### D3 — Per-area refactoring + test catalog

#### Authz.Pdp — strong fail-closed adapter seam; duplication + a few audit-explanation/lifetime gaps
- **R-PDP-1 — Extract a shared fail-closed adapter helper/base** _(S · preserving)_ — the `ProviderUnavailable` const + ~10-line `FailClosed(...)` body is copy-pasted verbatim across `Opa/Cerbos/Cedar` adapters; a change to fail-closed posture (e.g. a metric tag) needs 3 synchronized edits and diverges silently in an authz-critical path. `Providers/Adapters/Opa/OpaDecisionProvider.cs`, `Providers/Adapters/Cerbos/CerbosDecisionProvider.cs`, `Providers/Adapters/Cedar/CedarDecisionProvider.cs`.
- **R-PDP-2 — Hoist `ApprovalThreshold = 10_000m` to one shared domain constant** _(S · preserving)_ — the threshold is triplicated (`ReferenceDecisionProvider`, `FintechRuleEvaluator`, `CedarDecisionProvider`), each "mirroring" the others; a business change needs 3 edits and a miss yields parity divergence only the catalog runner catches.
- **R-PDP-3 — Map `ExtendedContextUnsupported` in `DecisionExplanations.RuleForReason` + attach an explanation in the guard** _(S · preserving)_ — today it falls through to `engine-unavailable`, so a deliberate policy-boundary deny audits as an outage, misleading operators. `Contracts/DecisionExplanations.cs`, `Providers/ExtendedContextGuardProvider.cs`.
- **R-PDP-4 — Implement `IDisposable` on `OpenFgaRebacService` + dispose the client on bootstrap failure** _(S · preserving)_ — it holds a `SemaphoreSlim` + `OpenFgaClient` that are never disposed and leaks a client on each failed bootstrap; `SpiceDbCheckService`/`CerbosCheckService` do this correctly — this is the outlier. Risk: socket exhaustion under repeated OpenFGA outages.
- **R-PDP-5 — Remove the dead `IOptions<AuditForwardingOptions>` injection from `AuditForwardingWorker`** _(S · preserving)_ — the parameter is immediately discarded (`_ = options;`); it inflates the ctor and must be supplied by every hand-built test. `Audit/AuditForwardingWorker.cs`.
- **R-PDP-6 — Consolidate `ChannelCapacity`/`HttpTimeoutSeconds` defaults + add `ValidateOnStart`** _(M · behavior-change)_ — `ChannelCapacity: 0` silently falls back to 2048 with no diagnostic, and adapter option URL/positive-int constraints only fail at first live call; promote to a clean startup error. `Audit/PdpAuditSinkServiceCollectionExtensions.cs` + the adapter/audit `Options` types (`OpaOptions`/`CerbosOptions`/`SpiceDbOptions`/`OpenFgaOptions`/`AuditForwardingOptions`).
- **R-PDP-7 — Add an async seam `EvaluateAsync` to `IAuthorizationDecisionProvider` (default bridges to sync)** _(L · preserving)_ — `OpenFgaProvider`/`SpiceDbProvider` block a thread-pool thread via `.GetAwaiter().GetResult()` on genuinely async SDK calls, a latency multiplier under load that also blocks `CancellationToken` flow. Migrate adapters one at a time. `Contracts/IAuthorizationDecisionProvider.cs`.
- **R-PDP-8 — Make `PlaygroundFanoutService.AllAgree` compare obligations, not just Permit/Deny** _(S · behavior-change)_ — it reports "all agree" when engines return `Permit` with *different* obligations (e.g. `RequireApproval` vs `PostImmediately`), giving false confidence in engine-migration comparisons; `ShadowRunner.Diff` already compares all three dimensions. `Playground/PlaygroundFanoutService.cs`.
- **R-PDP-9 — Replace the `"unavailable"` substring outage heuristic with a typed marker** _(M · preserving)_ — outage classification is a string-contains on the reason code, an unenforced naming convention that a future code containing "unavailable" would corrupt. `Playground/PlaygroundFanoutService.cs`.
- **R-PDP-10 — Cache `CasbinDecisionProvider.RolePolicies()` as a `static readonly` lookup** _(S · preserving)_ — it re-enumerates the fixed policy set and re-runs `_enforcer.Enforce` on the hot `DescribeRoleRule` path. `Providers/Adapters/Casbin/CasbinDecisionProvider.cs`.
- **Coverage gaps (tests to add):** `PdpDecisionService` break-glass audit flags + zero-reason fallback + `pdp.evaluate.duration` histogram; `AuditForwardingWorker` graceful-cancellation exit; `HttpForwardingPdpDecisionAuditSink` `dropped % 1000` second-log boundary; `CerbosCheckService.ExtractOutputToken` multi-output→fail-closed; `CedarDecisionProvider` amount-overflow fail-closed; `ChannelCapacity=0` fallback; `DecisionExplanations.Baseline` zero-reason. **Testability:** inject client factories into `CerbosCheckService`/`OpenFgaRebacService`; make `PdpTelemetry` meter/activity injectable (`IMeterFactory`); inject a trace-id provider into the playground.

#### Bank.Api — clean domain model; endpoint layer untested + input-validation/EF gaps
- **R-BANKAPI-1 — Add `[Required]`/`[StringLength]` on request DTOs + model-state validation** _(S · behavior-change)_ — empty/over-length `AccountNumber`/`Currency` currently pass the endpoint and 500 at the DB constraint instead of a clean 400. `Contracts/Dtos.cs`, `Data/BankDbContext.cs`.
- **R-BANKAPI-2 — Eliminate the double tenant DB lookup in POST `/api/transactions`** _(S · preserving)_ — the tenant row is fetched by Code then re-fetched by Id to recover its Code; one `AsNoTracking` fetch serves both. `Endpoints/TransactionEndpoints.cs`.
- **R-BANKAPI-3 — Introduce a scoped `CallerContext` caching the resolved tenant id** _(M · preserving)_ — every endpoint independently re-issues the same `WHERE code=@p` lookup; caching in the DI scope removes duplicate round-trips and makes the pattern visible. `Auth/TenantScope.cs`.
- **R-BANKAPI-4 — `AsNoTracking()` on the checker load in `DecideAsync`** _(S · preserving)_ — the checker graph is read-only but tracked, inconsistent with every other read. `Endpoints/TransactionEndpoints.cs`.
- **R-BANKAPI-5 — Retire/deprecate the dead `TransactionStatus.Approved` enum value** _(S · preserving)_ — never assigned (`Approval.Decide` uses `Posted`); misleads readers of the state machine (guard with a migration if any rows store it). `Domain/Enums.cs`.
- **R-BANKAPI-6 — Fix `EntitlementsEnforcer.Unavailable` discarding its own `Deny(...)` return** _(S · preserving)_ — it logs via `Deny`, drops the result, then rebuilds a near-identical record — divergence bait. `Entitlements/EntitlementsEnforcer.cs`.
- **R-BANKAPI-7 — Use server-side LINQ projections instead of `Select(x=>x.ToDto())`** _(M · preserving)_ — the terminal method-call `Select` materializes full entities then projects client-side. `Endpoints/AccountEndpoints.cs`, `Endpoints/TransactionEndpoints.cs`.
- **R-BANKAPI-8 — Quota-compensation on transaction-create failure** _(L · behavior-change)_ — quota is consumed before `SaveChangesAsync`; a save failure permanently burns a quota unit (documented gap). Needs a compensate endpoint/outbox. `Entitlements/EntitlementsEnforcer.cs`.
- **Coverage gaps:** **no HTTP-level tests** for `Account/Transaction/Reference` endpoints — add `WebApplicationFactory` tests for cross-tenant 403, missing-account 400, subject-mismatch 403, feature-gate 403, quota 429, service-unavailable 503, already-decided 409, xmin-race 409; `TenantScope.ResolveCallerTenantIdAsync` (null/unknown/known); `EntitlementsClient` transport for `ConsumeQuota/GetSeats` + cancellation passthrough. **Testability:** extract `DecideAsync` into a handler class; inject `ITenantRepository`.

#### Bank.Web — clean layering; a DTO monolith, transport duplication, and untested Razor pages
- **R-BANKWEB-1 — Split the 415-line `Clients/Dtos.cs` into per-service files** _(S · preserving)_ — one unstructured monolith mixing 8 services' contracts; per-file makes diffs/ownership clear.
- **R-BANKWEB-2 — Extract shared HTTP transport into a base/extension** _(M · preserving)_ — `GetListAsync/GetOrNullAsync/PostAsync` are copy-pasted between `BankApiClient` and `GovernanceClient` (~90 lines); a fix must be applied twice.
- **R-BANKWEB-3 — Add `GET /api/users/me` (Bank.Api) + use it in `CurrentUser`** _(M · behavior-change: new additive endpoint)_ — `ResolveBankUserIdAsync` fetches *all* tenant users then client-side `FirstOrDefault`; O(N) over the wire per page that needs the id. `Clients/CurrentUser.cs`.
- **R-BANKWEB-4 — Share the domain enums instead of redefining them** _(M · preserving)_ — `AccountType/TransactionStatus/…` are duplicated in `Clients/Dtos.cs` mirroring the Bank.Api `Domain/Enums.cs`; a new value deserializes to `0` silently. Extract a shared contracts project.
- **R-BANKWEB-5 — Filter blank role values in `CurrentUser.Roles`** _(S · behavior-change: security hardening)_ — `IsInRole("")` returning true is a privilege-escalation risk if an IdP emits empty multi-value claims. `Clients/CurrentUser.cs`.
- **R-BANKWEB-6 — Extract Razor page submit logic into injected handler services** _(M · preserving)_ — `OnValidSubmit`/`OnPost` in `NewTransaction.razor`/`Approvals.razor` orchestrate `CurrentUser` + bank client with **zero** tests; a handler seam makes them unit-testable.
- **R-BANKWEB-7 — Clarify the `TaskCanceledException` timeout discriminator** _(S · preserving)_ — `when (!ct.IsCancellationRequested)` works by accident for HttpClient timeouts (`InnerException is TimeoutException`); make intent explicit. `Clients/BankApiClient.cs`, `Clients/GovernanceClient.cs`.
- **Coverage gaps:** all Razor page handlers; `AuditClient`/`PdpClient` transport; `BankApiClient` transaction GETs + timeout branch; `CurrentUser` null-`HttpContext`; `GovernanceClient` write methods. **Testability:** inject `IBankApiClient` into extracted page handlers.

#### Entitlements.Service — pure domain is clean; the entire endpoint/persistence/metering/feature layer is untested (highest-value coverage target)
- **R-ENT-1 — Replace `FirstAsync` with `FirstOrDefaultAsync` + graceful 500** _(S · preserving)_ — a subscription pointing at a missing plan row throws `InvalidOperationException`→unhandled 500 w/ stack trace. `Endpoints/EntitlementsEndpoints.cs`.
- **R-ENT-2 — Add endpoint-level integration tests for all seven handlers** _(L · preserving)_ — the quota retry loop, advisory-lock seat path, feature gate, and 503 exhausted-retry path are entirely untested; a concurrency bug is invisible until load. `Endpoints/EntitlementsEndpoints.cs`.
- **R-ENT-3 — Exponential backoff + jitter in the quota-consume retry loop** _(S · preserving)_ — 8 immediate retries on `DbUpdateException` create a thundering herd under contention. `Endpoints/EntitlementsEndpoints.cs`.
- **R-ENT-4 — Validate route params (length/non-empty)** _(S · behavior-change)_ — 200-char `tenantCode` hits a `VARCHAR(50)` column; inconsistent with the constraint.
- **R-ENT-5 — Pre-compute lowercase metric/audit tag strings** _(S · preserving)_ — `ToString().ToLowerInvariant()` allocates per decision on the hottest service path. `Endpoints/EntitlementsEndpoints.cs`, `Metering/EntitlementsMetrics.cs`.
- **R-ENT-6 — Return 400 for non-positive `ConsumeQuotaRequest.Amount`** _(S · behavior-change)_ — silently normalizing `0`/negative to `1` hides caller bugs.
- **R-ENT-7 — Load `Plan` inside the advisory-lock transaction for seat endpoints** _(S · preserving)_ — the seat-limit is read before the lock; a concurrent limit change would decide on a stale value.
- **R-ENT-8 — Disambiguate `SeatAssignmentResponse.Assigned` (rename or split types)** _(M · behavior-change)_ — release returns `Assigned=false` for success; the field name misleads callers. Wire-contract change → version.
- **Coverage gaps (largest deficit):** `UsageCounter.CurrentPeriod` month/UTC boundaries; the **full endpoint matrix** (plan/module/feature/quota/seats — ~30 named cases incl. unknown-tenant, at-limit, unlimited, idempotent assign, 503-after-max-retries); `EntitlementsMetrics`/`LoggingEntitlementAuditSink` via `MeterListener`/`FakeLogger`; `OpenFeatureGate` round-trip + `FeatureProviderFactory` Unleash-missing-URL throw. **Testability:** inject `IUsageCounterRepository` (stub `DbUpdateException`) and a `FakeFeatureGate` instead of the global `Api.Instance`.

#### Governance.Service — layered gates are correct; an 814-line god handler + a risky `default:` fallthrough + time/duration gaps
- **R-GOV-1 — Fix the `default:` fallthrough in `ApproveRequestAsync`** _(S · preserving)_ — `case Approved: default:` silently issues a grant (and null-derefs `outcome.Grant!`) for any future `ApprovalDisposition` value; make `default:` throw. `Endpoints/GovernanceEndpoints.cs`.
- **R-GOV-2 — Cap `RequestedDurationMinutes`** _(S · behavior-change)_ — `AddMinutes(Int32.MaxValue)` overflows `DateTimeOffset` → unhandled 500 in `ComputeExpiry`; clamp + 400. `Domain/GovernanceRules.cs`, `Domain/AccessGrantFactory.cs`.
- **R-GOV-3 — Unify approve/reject checker-validation** _(M · preserving)_ — the reject path uses a local `ValidateChecker` tuple while approve delegates to `AccessApprovalService`; a rule change needs both. `Endpoints/GovernanceEndpoints.cs`, `Sod/AccessApprovalService.cs`.
- **R-GOV-4 — Inject `TimeProvider` across handlers** _(M · preserving)_ — multiple `DateTimeOffset.UtcNow` captures per request skew timestamps and block deterministic time assertions. `Endpoints/GovernanceEndpoints.cs`.
- **R-GOV-5 — Split `Endpoints/GovernanceEndpoints.cs` (814 LOC) into Request/Grant/Review modules** _(M · preserving)_ — the size makes change-impact assessment hard; aligns with the existing `BreakGlassDelegationEndpoints` split. `Endpoints/GovernanceEndpoints.cs`.
- **R-GOV-6 — Add an xmin concurrency token to `AccessReviewCampaign`** _(M · preserving)_ — concurrent last-item decisions both write `Completed` (last-writer-wins); a token removes the redundant write. `Data/GovernanceDbContext.cs`.
- **R-GOV-7 — Document the deliberate tenant-scope omission on grant endpoints** _(S · preserving)_ — `RevokeGrantAsync` fetches by grant id with no tenant guard (intentional intra-cluster), asymmetric with CS29-scoped request endpoints; mark it so a future endpoint doesn't copy the wrong pattern. `Endpoints/GovernanceEndpoints.cs`.
- **R-GOV-8 — Replace the primitive tuple return of `ValidateChecker` with a named record** _(S · preserving)_ — positional `(GovernanceOutcome,string,string)?` is unreadable and untestable in isolation.
- **Coverage gaps:** endpoint-level approve→grant flow (`WebApplicationFactory` + fake `IPdpSodClient`); the `default:` fallthrough guard; `EffectiveDurationMinutes(Int32.MaxValue)`; `ReviewCampaignPlanner.BuildItems` empty input; `GetTenant` missing/blank claim; `DecideReviewItemAsync` already-decided 409; delegation-revoke-when-expired. **Testability:** `IGovernancePersistence`/in-memory DB fixture; `TimeProvider`.

#### Audit.Service — tamper-evident chain is well-built; small hot-path + validation + testability polish (chain-format frozen, Decision #3)
- **R-AUDIT-1 — Span-based `SHA256.HashData` instead of `buffer.ToArray()`** _(S · preserving)_ — the `MemoryStream.ToArray()` heap copy runs on every ingest; hash the `GetBuffer()` span in place. **Does not change the hashed bytes** — same input, different delivery. `Domain/AuditHashChain.cs`.
- **R-AUDIT-2 — Validate `IngestDecisionAsync` inputs** _(S · behavior-change)_ — over-length `TraceId` (max 64) 500s at `SaveChanges`; empty required strings persist silently; return 400. `Endpoints/AuditEndpoints.cs`.
- **R-AUDIT-3 — Inject `TimeProvider` into `AuditChainWriter.CreateEntry`** _(S · preserving)_ — `ReceivedAtUtc = DateTimeOffset.UtcNow` is non-deterministic; `ReceivedAtUtc` is **non-hashed**, so no chain-format impact. `Services/AuditChainWriter.cs`.
- **R-AUDIT-4 — `init` setters on `AuditEntry` content fields** _(M · preserving)_ — public setters on a tamper-evident entity give no compile-time append-only protection. `Data/AuditEntry.cs`.
- **R-AUDIT-5 — Cap `offset` in `QueryEntriesAsync`** _(S · behavior-change)_ — unbounded `OFFSET N` allows a full sequential scan via `?offset=10000000`; cap + 400 (cursor paging is the eventual design). `Endpoints/AuditEndpoints.cs`.
- **R-AUDIT-6 — Add `VerifyAsync`-with-checkpoint tests** _(S · preserving)_ — only the sync `Verify` path has checkpoint tests though both share `Verifier`. `Domain/AuditHashChain.cs`.
- **R-AUDIT-7 — `char.IsAsciiHexDigit` instead of `Uri.IsHexDigit`** _(S · preserving)_ — semantically explicit in a security context; identical behavior, no chain-format impact. `Domain/AuditHashChain.cs`.
- **Coverage gaps:** ingest validation (empty/over-length); query limit/offset clamping branches; `VerifyAsync` w/ checkpoint (tail-truncation, suffix-rewrite, mid-chain anchor); `AppendAsync` cancellation on the semaphore + DB-failure recovery; `RequestSnapshotOptions` max=1 floor; multi-filter AND. **Testability:** `IAuditChainRepository` seam over `IServiceScopeFactory`.

#### Compliance — clean two-tier error model; document-rendering + bounds + provenance gaps
- **R-COMP-1 — Add the "row-hash rewritten in place" tamper scenario to `AuditIntegrityReporter`** _(S · behavior-change: adds an evidence case)_ — the evidence pack omits the exact `UPDATE … SET row_hash` case the chain can detect; auditors can't confirm it today (bump the case count assertion). `AuditIntegrityReporter.cs`.
- **R-COMP-2 — Escape `|`/newlines in `MarkdownRenderer` table cells** _(S · behavior-change)_ — a campaign name/reason containing `|` breaks the GFM table and corrupts the evidence document. `MarkdownRenderer.cs`.
- **R-COMP-3 — Count `Pending` explicitly instead of `total - certified - revoked`** _(S · preserving)_ — an unrecognized server decision (new `"Abstain"`) yields a negative `Pending`. `CertificationReporter.cs`.
- **R-COMP-4 — Guard `decision.Reasons[0]` with a fallback** _(S · preserving)_ — unguarded index throws on an empty `Reasons` list from a stub/future provider. `SodEvidenceReporter.cs`.
- **R-COMP-5 — Fail-closed on blank `PrincipalId` instead of substituting the probed principal** _(S · behavior-change)_ — silent substitution fabricates a false evidence claim. `LeastPrivilegeReporter.cs`.
- **R-COMP-6 — Fix `CaptureGitSha` output-read ordering** _(S · preserving)_ — `ReadToEnd()` after `WaitForExit(int)` can miss trailing bytes per .NET docs. `ComplianceReportStore.cs`.
- **R-COMP-7 — Give `BuildDeterministic` a genuinely synchronous core** _(S · preserving)_ — removes `.GetAwaiter().GetResult()` sync-over-async on the deterministic path. `ComplianceReportBuilder.cs`.
- **Coverage gaps:** row-hash-rewrite scenario; `ToSummary` unrecognized-decision; markdown `|`-escape; `CaptureGitSha` (available/unavailable/timeout — needs seam); `Parse` schemaVersion=0; blank-`PrincipalId`; zero-campaigns render branch; `EscapeDataString` special-char principal ids; `ComplianceDataException` propagation. **Testability:** `IGitShaProvider` seam.

#### Edge.Gateway — correct, tightened security posture; dead telemetry, a route-verb gap, and a missing proxy timeout
- **R-EDGE-1 — Remove or activate the dead `GatewayTelemetry.ActivitySource`** _(S · preserving)_ — a public static `ActivitySource` that never starts a span; dead code that also leaks a process-wide listener into tests. `Telemetry/GatewayTelemetry.cs`.
- **R-EDGE-2 — Wire `GatewayActorClaims` into audit or mark it a reserved seam** _(M · preserving)_ — the OBO/delegation claim reader is tested but unreferenced in production, so delegation calls audit without the effective human actor. `Auth/GatewayActorClaims.cs`.
- **R-EDGE-3 — Add PUT/DELETE/PATCH routes (or an explicit documented gap)** _(S · behavior-change)_ — `read-catch-all` captures only `GET`; any other verb to `/api/**` returns a gateway 404 and reaches no policy, silently making mutable Bank.Api endpoints unreachable through the sole external entry point. `appsettings.json`.
- **R-EDGE-4 — Structured logging instead of `Console.Error.WriteLine`** _(S · preserving)_ — the non-Development auth-misconfig warning bypasses OTel/Loki and is invisible exactly when it matters. `Auth/GatewayAuthenticationSetup.cs`.
- **R-EDGE-5 — Inject `TimeProvider` into `GatewayAuditMiddleware`** _(S · preserving)_ — hard `DateTimeOffset.UtcNow` blocks timestamp assertions. `Audit/GatewayAuditMiddleware.cs`.
- **R-EDGE-6 — Declare explicit YARP `Transforms`** _(S · preserving)_ — the implicit "forward all headers incl. `Authorization`" is undocumented; explicit transforms are the idiomatic control point. `appsettings.json`.
- **R-EDGE-7 — Name the magic `5099` default destination** _(S · preserving)_ — an untraceable standalone default that silently targets the wrong endpoint. `appsettings.json`.
- **R-EDGE-8 — Configure a proxy request-timeout** _(S · behavior-change)_ — YARP's forwarder uses its own `HttpMessageInvoker`, outside ServiceDefaults resilience; a hung Bank.Api holds a request open indefinitely. `Program.cs`.
- **Coverage gaps:** `GatewayMetrics.RecordDecision` via `MeterListener`; `Activity.Current` tag enrichment; non-`/api` skip; edge-allow/routed path; `MapPolicyToScope` unknown-policy branch; `ResolveAuthority` trailing-slash. **Testability:** injectable `ActivitySource`, `TimeProvider`, `ILogger`.

#### AppHost — disciplined opt-in wiring; magic strings + smoke-test assertions to add
- **R-APPHOST-1 — Extract repeated audience/token/env-key strings to constants** _(S · preserving)_ — `"bank-api"` audience ×3+, the Unleash client token ×2 (the only glue between Unleash and the entitlements service — a mismatch fails silently at runtime), and repeated `"Keycloak__Authority"`. `AppHost.cs`.
- **R-APPHOST-2 — Extract the `WithEnvironment(OTLP)+WaitFor(observability)` pair into a helper** _(S · preserving)_ — 7 copy-pasted pairs; missing either on a new service loses its first telemetry batch. `AppHost.cs`.
- **R-APPHOST-3 — Assert explicit-start + core-service + OTLP wiring in the app-model smoke test** _(M · preserving)_ — current smoke tests only check build + name uniqueness; a removed `.WithExplicitStart()` would silently pull Docker on every `dotnet test`, breaking Docker-free CI. `tests/AuthzEntitlements.AppHost.Tests/AppHostApplicationModelSmokeTests.cs`.
- **R-APPHOST-4 — Move `"bank-web-secret"` to the existing user-secrets / a marked constant** _(S · preserving)_ — a hardcoded client secret cargo-cults into forks; the csproj already has a `UserSecretsId`. `AppHost.cs`.
- **R-APPHOST-5 — Factor the Unleash Postgres-env block into a helper** _(M · preserving)_ — the 4-part DSN assembly duplicates the OpenFGA pattern. `AppHost.cs`.
- **R-APPHOST-6 — Comment the deliberate no-`WaitFor(auditService)` on the PDP→audit chain** _(S · preserving)_ — prevents a well-intentioned but incorrect hard dependency being added. `AppHost.cs`.
- **R-APPHOST-7 — Resolve `WaitFor(observability)` on an HTTP health-check** _(M · preserving)_ — a slow LGTM image pull blocks all seven services; a `/metrics` health gate resolves earlier. `AppHost.cs`.
- **Coverage gaps:** app-model assertions for explicit-start on each opt-in engine, core-service presence, OTLP-on-each-service, external-endpoint-only on edge/web, `openfga-migrate` WaitForCompletion. **Testability:** none needed — the app model is inspectable data.

#### ServiceDefaults — 0 tests today on a cross-cutting kingpin (Decision #8)
- **R-SVCDEF-1 — Create `tests/AuthzEntitlements.ServiceDefaults.Tests`** _(M · preserving)_ — the OTLP gate is the behavioral kingpin for all seven services; a one-char env-key typo silently disables all telemetry with no test to catch it. `Extensions.cs`.
- **R-SVCDEF-2 — Enable/annotate gRPC client instrumentation** _(S · preserving)_ — SpiceDB/Cerbos adapters use gRPC; those spans are invisible in Tempo without it (the comment implies "optional"). `Extensions.cs`.
- **R-SVCDEF-3 — Move `Extensions` out of `namespace Microsoft.Extensions.Hosting`** _(M · preserving)_ — framework-namespace squatting confuses IntelliSense attribution and is inconsistent with the correctly-namespaced `LogSanitizer`. `Extensions.cs`.
- **R-SVCDEF-4 — Comment that resilience doesn't cover YARP's forwarder** _(S · preserving)_ — every reader assumes `AddStandardResilienceHandler` covers all HTTP; it doesn't (ties to R-EDGE-8). `Extensions.cs`.
- **R-SVCDEF-5 — Document/test the non-Development health-endpoint behavior** _(S · preserving)_ — `MapDefaultEndpoints` only maps `/health` in Development, so an Aspire `WaitFor` probe 404s under Staging/Production. `Extensions.cs`.
- **Coverage gaps (all new):** OTLP gate on/off/whitespace; health mapped in Dev / 404 in Prod; resilience + service-discovery registered; `LogSanitizer.Clean` CRLF/null/empty; logging flags; health trace filter. ~12 tests. **Testability:** none — the generic `IHostApplicationBuilder` signature already accepts a test `WebApplicationBuilder`.

#### Benchmarks — solid runner; `EngineCatalog` uncovered + entry-point/printer testability
- **R-BENCH-1 — `IReadOnlyList<string>` for `InProcessEngineNames`/`LiveEngineNames`** _(S · preserving)_ — mutable `string[]` is an encapsulation hole on the canonical engine list. `EngineCatalog.cs`.
- **R-BENCH-2 — Make `ProbeLiveReachable` async with a sync console boundary** _(S · preserving)_ — `.GetAwaiter().GetResult()` on a TCP connect. `EngineCatalog.cs`.
- **R-BENCH-3 — Extract `PrintSummary`/`PrintRegressionReport` to a `BenchmarkPrinter(TextWriter)`** _(S · preserving)_ — local functions writing to `Console.Out` are untestable. `Program.cs`.
- **R-BENCH-4 — Cache `DefaultBaselinePath` as a `static readonly` field** _(S · preserving)_ — `Path.Combine(AppContext.BaseDirectory,…)` recomputed per call. `BenchmarkOptions.cs`.
- **R-BENCH-5 — Add `P90Ms` to `LatencyStats` (schema v2)** _(M · behavior-change)_ — P50→P95 is too coarse for skewed distributions (breaks v1 baseline intentionally). `BenchmarkModels.cs`.
- **R-BENCH-6 — Document single-call Stopwatch limits for sub-100ns engines** _(S · preserving)_ — timer quantization dominates for cedar/reference; set expectations. `BenchmarkRunner.cs`.
- **R-BENCH-7 — Add `EngineCatalogTests`** _(S · preserving)_ — the only production file with no direct coverage. `EngineCatalog.cs`.
- **Coverage gaps:** `EngineCatalog` (`IsInProcess/IsLive/IsKnown`, `CreateInProcessProvider` incl. unknown, `LiveEndpointDescription`); `ResultStore.CaptureGitSha`/`SanitizeForFileName`; `Program` exit codes (needs `EntryPoint.Run(args,stdout,stderr):int`); `warmup=0`; `RegressionDetector.DeltaPercent` zero-baseline; `BenchmarkJson.Options` immutability; `LatencyStatistics.Compute` null. **Testability:** entry-point extraction; `Func<>` seams for git process + socket prober.

### D4 — Coverage-to-95% program (evaluation summary; full narrative in `## Test coverage → 95%`)

- Phase 0 measurement infra (D0) → Phase 1 scope/exclusions (Decision #5) → Phase 2 CI ratchet gate (Decision #6) → Phase 3 gap-closure waves A/B/C → Phase 4 `docs/testing/coverage.md` (D1).
- **Wave sequencing** (by deficit × risk): **A** = ServiceDefaults (new project), Entitlements.Service endpoint/metering/feature, Bank.Api HTTP-level; **B** = Governance endpoint + Bank.Web page-handlers + Edge/Audit branch gaps; **C** = Compliance, Benchmarks, AppHost app-model assertions, residual PDP branches. Each wave pairs testability seams (Decision #7) with the tests they unblock.

## User-approval gates

- **None billable** — evaluation/plan only; no cloud resources, no `azd`. Filing this CS does not claim or implement it.
- At implementation time: (a) flipping the CI coverage gate from report-only to **blocking** is a separate reviewed step; (b) each **behavior-change**-tagged refactor (and every wire-contract change: R-ENT-8, R-BENCH-5, R-EDGE-3) gets its own targeted review and, where a contract changes, consumer coordination.

## Exit criteria

- This planned CS is filed with: the complete per-area refactoring catalog (each item: rationale + effort + behavior-preserving flag + citations), the coverage-to-95% program (tooling + scope + ratchet + waves), an independent GPT-5.5 plan review with a pinned attestation hash whose verdict is `Go`/`Go-with-amendments`, and `harness lint` green (clickstop + plan-review + text-encoding). Implementation is explicitly out of scope for the filing CS.

## Risks + open questions

- **Catalog scope is large (~80 items).** Mitigation: Decision #1 sequences implementation into scoped waves/sub-CSs; the filing CS itself carries zero implementation risk.
- **Coverage baseline is unknown until measured.** Reaching 95% may be a substantial test-writing effort; the ratchet (Decision #6) makes it incremental and enforceable rather than a cliff.
- **coverlet on the .NET 10 RC SDK** (`10.0.100-rc.1`) — the collector's net10 compatibility must be verified in Phase 0 before the gate is trusted.
- **Behavior-changing refactors can alter API/wire contracts** (R-ENT-8 seat response, R-EDGE-3 new routes, R-BENCH-5 schema v2). Gated per Decision #2 + the user-approval note.
- **Audit hash-chain is high-risk.** Decision #3 forbids any hashed-set change without a format-version migration; R-AUDIT-1/-3/-7 are verified non-hashed-set-changing.
- **Open question:** should the shared Bank enums (R-BANKWEB-4) live in a new `AuthzEntitlements.Bank.Contracts` project referenced by both Api and Web, or be duplicated with a contract test asserting parity? Resolve at Wave-B claim.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 9bd9d60aea5b | 2026-07-05T17:49:00Z | Needs-Fix | Wrong citation dirs (Compliance/Audit/PDP/Governance); Decision #3 overstated `ComputeRowHash`; 4 behavior-change items mistagged. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | b39e71fd74d3 | 2026-07-05T18:00:42Z | Go | All R1 findings fixed; iterated 2 more GPT-5.5 passes (R-GOV-5 path, R-EDGE-2 range tag; Decision #2 = exact 18-item set); citations/tags verified. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Wave 0 (D0/D1) — coverage measurement infrastructure: `coverlet.collector` via CPM + `tests/Directory.Build.props`, `coverage.runsettings`, ReportGenerator dotnet-tool, per-assembly baseline capture, `docs/testing/coverage.md` | done | yoga-ae-c4 | Delivered on `cs52/content`. Non-behavior-changing (Decisions #4/#5/#6); test/config/docs only. coverlet 6.0.4 + ReportGenerator 5.4.7 validated on the .NET 10 RC SDK; baseline overall 69.8% line / 71.0% branch; build 0/0, 1742 tests pass. |
| Wave 0b (D0) — report-only coverage gate in `.github/workflows/dotnet-ci.yml` | pending | — | Deferred, coordination-gated: workflow files are coordinated by yoga-ae-c5 (WORKBOARD note); land after sign-off, then flip to blocking per Decision #6. |
| Wave A (D3/D4) — ServiceDefaults.Tests (Decision #8) + Entitlements.Service endpoint/metering/feature + Bank.Api HTTP-level tests & seams | pending | — | Deferred to a follow-up wave/sub-CS per Decision #1 (highest deficit × risk). |
| Wave B (D3/D4) — Governance endpoint + Bank.Web page-handlers + Edge/Audit branch gaps | pending | — | Deferred to a follow-up wave/sub-CS per Decision #1. |
| Wave C (D3/D4) — Compliance, Benchmarks, AppHost app-model assertions, residual PDP branches | pending | — | Deferred to a follow-up wave/sub-CS per Decision #1. |
| Close-out: docs + restart state | pending | yoga-ae-c4 | Update `WORKBOARD.md` + `CONTEXT.md` so a fresh agent can restart from the actual state (coverage tooling present, baseline recorded, waves pending). |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | File learnings in `LEARNINGS.md` and planned follow-up CSs for Waves A/B/C. |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

- **Plan-review provenance:** 4 GPT-5.5 rubber-duck passes (R1 Needs-Fix → 3 fix iterations → Go). Fixes spanned citation directories (Compliance/Audit/PDP adapters/Governance `Sod`), the Decision #3 hash-chain wording, effort/behavior tag standardization (S/M/L × preserving/behavior-change), and Decision #2 behavior-change gate-list completeness (now the exact 18-item set). Table rows R1/R2 pin the initial and final attested Decisions+Deliverables hashes.
- Evaluation was produced by four parallel read-only sub-agents (Claude Sonnet 4.6, high reasoning) over `main` @ 2fe2bb8; the orchestrator handled cross-cutting build/CI/coverage analysis.

### CS52 Wave 0 — coverage measurement infrastructure (2026-07-05, yoga-ae-c4)

- **Sub-agent ledger:** agent-id `cs52-wave0-coverage-infra` | model `claude-opus-4.8` | role impl-coverage-infra | report-status complete. Delivered `Directory.Packages.props` (coverlet.collector 6.0.4), `tests/Directory.Build.props` (imports root props + version-less coverlet), `coverage.runsettings` (Cobertura + Decision #5 exclusions), `.config/dotnet-tools.json` (ReportGenerator 5.4.7), `docs/testing/coverage.md`, `.gitignore` (coverage artifacts). Sub-agent created no commit (orchestrator committed).
- **Local review:** GPT-5.5 rubber-duck @ content HEAD `4af1b5d` → **Go-with-amendments**. Dispositioned: (a) baseline %/count reconciliation — clarified in `coverage.md` that ReportGenerator truncates (5322/7614 is reported as 69.8%, not rounded to 69.9%); (b) `Obsolete` in `ExcludeByAttribute` — **kept**, matches the plan's explicit D0 spec (`ExcludeByAttribute=Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverage`).
- **Learning candidates (to file in `LEARNINGS.md` at close-out):**
  - `tooling`: MSBuild/XML comments cannot contain `--`; embedding CLI flags (`--collect`/`--settings`) in a `<!-- -->` comment in `Directory.Packages.props`/`.runsettings` makes the file unparseable and silently disables Central Package Management repo-wide (NU1015 on every package) with no pointer to the comment. Reword to avoid `--`.
  - `env`: single-project `dotnet restore <test>.csproj` fails NU1015 under CPM in this repo; only solution-level `dotnet restore AuthzEntitlements.sln` resolves.
  - `build-hygiene`: `TestResults/` + `coverage-report/` were not gitignored — added in this change.

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
