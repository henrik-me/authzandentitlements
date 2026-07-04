# Learnings

Learnings filed during the project. See [`RETROSPECTIVES.md`](RETROSPECTIVES.md) for harvest procedure and entry format.

---

> **ID sequencing:** Use sequential IDs starting from LRN-001. The linter emits
> warnings for gaps in the sequence but treats them as non-fatal; gaps do not
> cause exit code 1.

---

## Open

### LRN-001

```yaml
id: LRN-001
date: 2026-07-03
category: tooling
source_cs: CS01
status: open
tags: [aspire, dotnet, scaffolding, windows]
```

**Problem:** Scaffolding the Aspire foundations required generating the AppHost/ServiceDefaults projects and adding the Postgres hosting integration from a non-interactive agent shell.

**Finding:** Aspire CLI 13.1.0 `aspire add <integration>` aborts with exit 5 ("Interactive input is not supported in this environment") even with `--non-interactive --version` — use `dotnet add package Aspire.Hosting.<X> --version <apphost-sdk-band>` instead. The `aspire-apphost` template emits `AppHost.cs` (not `Program.cs`) and a single-SDK csproj (`Sdk="Aspire.AppHost.Sdk/13.1.0"`, no explicit `Aspire.Hosting.AppHost` PackageReference). `dotnet new` on Windows emits CRLF, so authored files must be normalized to LF/no-BOM for the text-encoding gate.

**Evidence:** PR #2 (commit `3919a03`); sub-agent `cs01-aspire-scaffold` report; two failed `aspire add postgres` invocations (exit 5).

**Implications carried forward:**
- CS02+ scaffolding should use `dotnet add package` for Aspire integrations and normalize generated files to LF.

### LRN-002

```yaml
id: LRN-002
date: 2026-07-03
category: tooling
source_cs: CS01
status: open
tags: [nuget, cpm, build, aspire]
```

**Problem:** With `TreatWarningsAsErrors=true` and Central Package Management, a clean `dotnet build` on preview / .NET 10 RC packages was impossible because NuGet audit advisories are promoted to build errors at restore time.

**Finding:** Suppress the SPECIFIC advisory IDs via `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-..." />` items in `Directory.Build.props` — NOT a blanket `NoWarn=NU1902;NU1903`, which would also mask future advisories in later projects. Per-advisory suppression keeps auditing active so any new advisory still fails the build.

**Evidence:** PR #2; `Directory.Build.props`; GPT-5.5 review (sub-agent `cs01-review`) finding #1; `dotnet list package --vulnerable --include-transitive`.

**Implications carried forward:**
- CS02+ adding preview packages will hit the same and can reuse the per-advisory pattern.

### LRN-003

```yaml
id: LRN-003
date: 2026-07-03
category: architectural
source_cs: CS01
status: open
tags: [security, nuget, aspire, opentelemetry]
claim_area: security-hardening
```

**Problem:** CS01 ships with known-vulnerable preview packages whose advisories are suppressed to achieve a clean build; this defers real supply-chain risk rather than resolving it.

**Finding:** 15 NuGet audit advisories across 3 packages are suppressed: MessagePack 2.5.192 (2 High + 9 Moderate, transitive via `Aspire.AppHost.Sdk` dashboard/DCP — not directly controllable) and OpenTelemetry 1.14.0 exporter + Api (4 Moderate, direct via the `aspire-servicedefaults` template). They are localhost-only dev-loop packages in CS01 with no untrusted-input path.

**Evidence:** PR #2; `Directory.Build.props` NuGetAuditSuppress list; `done/done_cs01_aspire-foundations.md` Notes.

**Implications carried forward:**
- CS18 (security hardening) must revisit: drop suppression entries as non-vulnerable stable Aspire 13 / OTel packages ship, or pin patched versions via CPM.

### LRN-004

```yaml
id: LRN-004
date: 2026-07-03
category: tooling
source_cs: CS02
status: open
tags: [efcore, npgsql, concurrency, dotnet]
```

**Problem:** CS02 needed Postgres optimistic concurrency on the `Approval` row to close a maker-checker double-decide race, but the standard Npgsql helper was unavailable on the pinned RC stack.

**Finding:** `UseXminAsConcurrencyToken()` was **removed** in `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0-rc.1 (verified absent from the assembly). The replacement is a shadow row-version mapped to the hidden system column: `entity.Property<uint>("xmin").IsRowVersion();`. Npgsql maps it to the Postgres system `xmin` column, so the generated SQL (`dotnet ef migrations script`) has **no physical `xmin` column** even though the migration C# shows `xmin = table.Column<uint>(type:"xid", rowVersion:true)`. A Copilot review flagged the migration C# as creating a physical column — a false positive (empirical `CREATE TABLE "Approvals"` SQL has no such column, and the migration applies cleanly).

**Evidence:** PR #5 (fix commit `9420dc4`); `src/AuthzEntitlements.Bank.Api/Data/BankDbContext.cs:161`; `dotnet ef migrations script` output.

**Implications carried forward:**
- Later EF Core optimistic-concurrency work on Postgres should use `Property<uint>("xmin").IsRowVersion()` and verify the generated SQL (not the migration C#) to confirm the system-column mapping.

### LRN-005

```yaml
id: LRN-005
date: 2026-07-03
category: tooling
source_cs: CS02
status: open
tags: [nuget, cpm, security, efcore, msbuild]
claim_area: security-hardening
```

**Problem:** Under `TreatWarningsAsErrors` + CPM, a clean build broke when EF Core Design 10.0.0-rc.1 dragged in a newly-advisory'd transitive MSBuild package.

**Finding:** `Microsoft.EntityFrameworkCore.Design` 10.0.0-rc.1 pulls `Microsoft.Build.Tasks.Core`/`Microsoft.Build.Utilities.Core` 17.14.8, carrying **CVE-2025-55247 / GHSA-w3q9-fxm7-j8fq** (High; Linux-only, design-time MSBuild temp-dir DoS). Remediate — do NOT suppress — by pinning the patched `17.14.28` on the same minor line via CPM **transitive pinning** (`CentralPackageTransitivePinningEnabled=true` + `PackageVersion` entries). This removes the advisory from `dotnet list package --vulnerable` (a real fix), unlike `NuGetAuditSuppress`.

**Evidence:** PR #5; `Directory.Packages.props` (transitive pins + `CentralPackageTransitivePinningEnabled`); `dotnet list package --vulnerable --include-transitive` (CVE absent post-pin).

**Implications carried forward:**
- CS18 (security hardening): drop the MSBuild pin once EF Core Design ships against patched MSBuild.
- Prefer patched-version CPM transitive pinning over suppression for any new transitive advisory (extends LRN-002/003).

### LRN-006

```yaml
id: LRN-006
date: 2026-07-03
category: tooling
source_cs: CS02
status: obsolete
tags: [harness, review, cli, escalation]
```

**Problem:** The `harness review <pr>` verb could not be used for the CS02 content review.

**Finding:** `harness review <pr>` (agent-harness v0.12.0) non-dry-run path aborted with "Could not find clickstop file for CS02 under project/clickstops/{active,planned,done}" even though `active_cs02_fintech-domain-skeleton.md` existed on both the PR branch and `main`; the `--dry-run` variant succeeded. Worked around by dispatching the GPT-5.5 reviewer sub-agent directly with the canonical reviewer preamble (OPERATIONS.md § Reviewer dispatch) and recording the verdict manually in the PR Review log. This was a harness (`lib/`) bug — out of scope to fix in-band (Hard Rule §3); escalated to the harness maintainer (filed upstream as `henrik-me/agent-harness#407`).

**Evidence:** this session; `harness review 5 --rubber-duck-only --no-poll` → exit 2 with the lookup error; `--dry-run` variant → exit 0; file present via `git ls-files`.

**Disposition:** obsolete — fixed upstream in **agent-harness v0.13.0** (CS93; `henrik-me/agent-harness#407` closed COMPLETED — `findClickstopFile` in `lib/review.mjs` now normalizes the padded/zero-stripped CS id on both sides and resolves directory-form + done-stage clickstops). This repo bumped its pin to v0.13.0 and removed the temporary `.github/copilot-instructions.md` workaround note; `harness review 5` verified working (exit 0, resolves `done_cs02_…`).

### LRN-007

```yaml
id: LRN-007
date: 2026-07-03
category: process
source_cs: CS02
status: open
tags: [review, verification, efcore]
```

**Problem:** CS02 drew findings from two independent reviewers of very different quality; acting on all blindly — or dismissing all blindly — would both have been wrong.

**Finding:** The independent GPT-5.5 review (R1) caught two REAL blocking bugs (in-memory-only double-decide race; caller-controlled, unconstrained tenant/branch). Copilot's 6 comments were ALL false positives, disproven empirically: (a) `.Select(x => x.ToDto()).ToListAsync()` on an EF `IQueryable` does NOT throw — EF Core client-evaluates the **top-level projection** (all list endpoints returned 200 with data); (b) the `xmin` row-version maps to the Postgres system column (no physical column; see LRN-004). Lesson: **verify review comments against the running system** (build/test/curl) before either fixing or dismissing — a confident "will throw at runtime" claim was falsified by one `GET`.

**Evidence:** PR #5 Copilot review (6 comments, all resolved with an evidence comment); live `GET /api/{tenants,branches,accounts,transactions}` → 200; `dotnet ef migrations script` (no physical `xmin` column).

**Implications carried forward:**
- Keep the independent-model rubber-duck as the review-of-record; treat automated-reviewer comments as leads to verify, not directives.

### LRN-008

```yaml
id: LRN-008
date: 2026-07-03
category: tooling
source_cs: CS03
status: open
tags: [keycloak, aspire, realm-import, docker]
```

**Problem:** The `authz-bank` realm import crashed Keycloak on startup (standalone and under Aspire).

**Finding:** Keycloak 26's default-on `organization` feature throws `IllegalArgumentException: Session not bound to a realm` in `setupClientServiceAccountsAndAuthorizationOnImport` when a realm export includes a client with `serviceAccountsEnabled` (here `bank-workload`). Disable it with `KC_FEATURES_DISABLED=organization` (container env / Aspire `.WithEnvironment`). Separately, Aspire's `WithRealmImport` enforces Keycloak's `<realm>-realm.json` filename convention (a bare directory `--import-realm` is lenient), so the file must be `authz-bank-realm.json` (not `authz-realm.json`).

**Evidence:** PR #12; container stack trace (`InfinispanOrganizationProvider.getRealm`); "File name / realm name mismatch" import error; `AppHost.cs`, `infra/keycloak/authz-bank-realm.json`.

**Implications carried forward:**
- Any Keycloak-26 realm import with a service account needs `KC_FEATURES_DISABLED=organization`; name realm files `<realm>-realm.json`.

### LRN-009

```yaml
id: LRN-009
date: 2026-07-03
category: architectural
source_cs: CS03
status: open
tags: [keycloak, aspire, oidc, issuer]
```

**Problem:** bank-api rejected valid Keycloak tokens under Aspire with "The issuer ... is invalid".

**Finding:** A dynamic/proxied Aspire Keycloak endpoint makes Keycloak stamp a different `iss` per access path (browser vs in-process service vs direct container port), so JWT issuer validation fails. Fix: pin Keycloak to a fixed host port (`AddKeycloak(name, port)`) and inject ONE explicit `Keycloak:Authority` (`http://localhost:<port>/realms/<realm>`) shared by every service and the browser, so the issuer is identical everywhere.

**Evidence:** PR #12; runtime `WWW-Authenticate: error_description="The issuer 'http://localhost:62370/realms/authz-bank' is invalid"`; `AppHost.cs` fixed-port + explicit authority.

**Implications carried forward:**
- CS04 (edge gateway) and any Aspire+Keycloak OIDC reuse the fixed-port + explicit-stable-authority pattern.

### LRN-010

```yaml
id: LRN-010
date: 2026-07-03
category: process
source_cs: CS03
status: open
tags: [jwt, dotnet, authz, testing]
claim_area: security-hardening
```

**Problem:** `RequireRole` returned 403 for tokens that plainly carried the role, yet the unit tests passed.

**Finding:** `JwtBearerOptions.MapInboundClaims` defaults to **true**, remapping Keycloak's top-level `roles` claim to the legacy `ClaimTypes.Role` URI while `RoleClaimType="roles"`, so `IsInRole`/`RequireRole` looked under "roles" and found nothing — a silent auth failure (also remaps `sub`→nameidentifier). Set `options.MapInboundClaims=false`. Synthetic-principal unit tests build the identity directly (`roleType="roles"`) and BYPASS the JWT handler, so they never catch this — reinforces LRN-007 (verify against the running system) and requires an options-level regression test.

**Evidence:** PR #12; temporary `/debug/whoami` showed the role under `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`; `AuthenticationSetup.cs` (`MapInboundClaims=false`) + `AuthenticationSetupTests`.

**Implications carried forward:**
- Any JwtBearer/OIDC wiring with custom claim names needs `MapInboundClaims=false` (bank-web too); add an options-resolution regression test — synthetic-principal tests are insufficient.

### LRN-011

```yaml
id: LRN-011
date: 2026-07-03
category: architectural
source_cs: CS03
status: open
tags: [authz, security, multi-tenant]
claim_area: security-hardening
```

**Problem:** The first CS03 cut authorized the token but still trusted caller-supplied `MakerId`/`CheckerId`/`TenantId`, and reads weren't tenant-scoped (GPT-5.5 R1/R2 blocking).

**Finding:** An AuthN CS must BIND access to the authenticated token, not caller-supplied fields: transaction maker and approval checker = token `sub`; every read tenant-scoped and every write tenant-checked against the token `tenant` claim, fail-closed on a missing/unknown claim. This is defense-in-depth layered OVER the domain maker-checker/SoD rules (which the token binding must not weaken).

**Evidence:** PR #12 GPT-5.5 R1 (5 blocking) + R2 (1 blocking); `TransactionEndpoints.cs` (sub binding + fail-closed tenant), `Auth/TenantScope.cs`; runtime act-as-other + cross-tenant → 403/404.

**Implications carried forward:**
- CS04 (edge) and CS05 (PDP) inherit the token-bound-identity + fail-closed-tenant contract; the `branch` claim is carried but not yet enforced (later ABAC).

### LRN-012

```yaml
id: LRN-012
date: 2026-07-03
category: tooling
source_cs: CS03
status: open
tags: [keycloak, oidc, client-scopes, par]
```

**Problem:** Access tokens lacked `sub`/`preferred_username`/`email`, and the OIDC web login failed with "Invalid scopes".

**Finding:** A Keycloak realm export that supplies its own `clientScopes` array does NOT auto-seed the built-in `basic`/`profile`/`email`/`roles` scopes, so (a) `sub` (moved to the `basic` scope in KC 24+) and `preferred_username`/`email` are absent from tokens, and (b) requesting undefined `profile`/`email` scopes fails the PAR authorization with "Invalid scopes". Fix: add `sub`/`preferred_username`/`email` mappers to an applied custom default scope (`bank-claims`), request only defined client scopes, and use a Keycloak-accepted dev redirect (`*`).

**Evidence:** PR #12; token decode (missing `sub`) → `bank-claims` mappers; bank-web `/claims` 500 "Invalid scopes: openid profile email bank.read" → scope reduction; `infra/keycloak/authz-bank-realm.json`.

**Implications carried forward:**
- When hand-authoring a Keycloak realm export, either include the built-in scopes or carry the required OIDC claims via a custom default scope; only request client scopes the realm defines.

### LRN-013

```yaml
id: LRN-013
date: 2026-07-03
category: architectural
source_cs: CS04
status: open
tags: [audit, yarp, gateway, aspnetcore, dotnet]
claim_area: observability
```

**Problem:** The CS04 edge gateway must emit accurate, audit-ready coarse-authorization *decision* events, but an audit middleware that wraps the YARP proxy cannot infer the edge decision from the HTTP status code alone.

**Finding:** A proxy-wrapping audit middleware sees the FINAL status, which conflates edge decisions with downstream ones: a request the edge *allows and routes* can still be 403'd by the backend's fine-grained authz (→ misreported as an edge deny), and `context.GetEndpoint() != null` still matches ASP.NET's synthetic **405** endpoint (→ an unmatched method audited as a false `allow/routed`). The reliable "was actually forwarded" signal is the **YARP proxy pipeline** itself: set the edge-authorized marker inside `MapReverseProxy(proxyPipeline => { proxyPipeline.Use(...); proxyPipeline.UseSessionAffinity(); proxyPipeline.UseLoadBalancing(); proxyPipeline.UsePassiveHealthChecks(); })`, which runs only for a request that matched a real proxy route, cleared its coarse policy, and is about to be forwarded. Then audit only genuine decisions (routed, or an edge 401/403 short-circuit). Three GPT-5.5 rubber-duck rounds surfaced these three misclassifications in sequence.

**Evidence:** PR #17; GPT-5.5 rubber-duck R1 (downstream-403), R2 (any-request marker), R3 (405 synthetic endpoint) → R4 Go; runtime-verified (`DELETE /api/accounts` → 405 with zero audit events; `POST /api/accounts` coarse.authenticated → allow/routed). `src/AuthzEntitlements.Edge.Gateway/Program.cs`, `Audit/GatewayAuditMiddleware.cs`.

**Implications carried forward:**
- For any proxy/gateway that audits decisions, key the decision off the proxy pipeline (was-forwarded), not the final status; only audit genuine authz decisions. Fine-grained decisions belong to the terminal service (`Bank.Api` `BankAuthorizationAuditMiddleware`; later the PDP in CS05).
- Follow-up (non-blocking): enrich edge-denial events with RouteId/RequiredScope (unset via `IReverseProxyFeature` on short-circuits), and skip auditing non-authz-decision requests (unmatched 404 / method-mismatch 405) uniformly across both gates.

### LRN-014

```yaml
id: LRN-014
date: 2026-07-03
category: tooling
source_cs: CS04
status: open
tags: [aspire, otel, dotnet, runtime, windows]
claim_area: observability
```

**Problem:** During CS04 `aspire run` verification, `Bank.Api` returned an empty-body HTTP 500 on **every** request (including `/alive`), which blocked the edge gateway's `WaitFor(bank-api)` so the gateway never started.

**Finding:** `Bank.Api` is unchanged by CS04, Keycloak was reachable, and the edge gateway run **standalone** (without the Aspire-injected OTLP export env) served and enforced correctly — so the 500 is an environmental Aspire/OTLP-export interaction under `dotnet run` of the AppHost, orthogonal to CS04. Verifying the gateway standalone (Keycloak on its fixed port + `Bank.Api` as the YARP destination) is a reliable way to isolate service logic from the Aspire orchestration/OTLP layer.

**Evidence:** this session; `GET http://localhost:5000/alive` → 500 (len 0) under `aspire run`; the same gateway build served 200 + correct 401/403/routed decisions standalone. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- Triage the OTLP exporter / OpenTelemetry-instrumentation vs .NET 10 RC1 interaction (candidate for the CS12 observability stack); until then, isolate service-level runtime verification from the AppHost OTLP wiring when it misbehaves.
- **CS12 update (2026-07-03):** CS12 landed the real OTLP collector (`grafana/otel-lgtm`) and pointed every service's `OTEL_EXPORTER_OTLP_ENDPOINT` at it, but a full `aspire run` reproduction of the empty-body 500 was NOT performed (a parallel `aspire run` may be active; CS12 verified the stack standalone instead). Whether routing OTLP at a ready collector (with `WaitFor(observability)`) resolves the 500 remains open — reproduce on the next clean full `aspire run`.

### LRN-015

```yaml
id: LRN-015
date: 2026-07-03
category: architectural
source_cs: CS10
status: open
tags: [postgres, ef-core, concurrency, dotnet]
claim_area: entitlements
```

**Problem:** Enforcing a hard capacity cap (seat limits) atomically under concurrent writes in EF Core + Postgres.

**Finding:** A `Serializable`-isolation + bounded-retry loop THRASHES under contention — 12 concurrent seat-assigns against a 5-seat plan produced 10 HTTP 500s (retry exhaustion), and a commit-time serialization failure (40001) makes an explicit `tx.RollbackAsync()` throw "transaction has completed", defeating the retry. Replacing it with a **per-subscription Postgres advisory transaction lock** — `SELECT pg_advisory_xact_lock(hashtextextended(<id>, 0))` issued inside the EF transaction via `db.Database.ExecuteSqlInterpolatedAsync(...)` — serializes the count→capacity-check→insert deterministically (blocks rather than conflict-retries; auto-releases at commit). 12- and 30-way concurrent assigns then give exactly the cap, 0 errors, no over-allocation. Prefer pessimistic advisory locks over `Serializable`+retry for hard capacity caps.

**Evidence:** PR #18; `src/AuthzEntitlements.Entitlements.Service/Endpoints/EntitlementsEndpoints.cs` `AssignSeatAsync`; runtime concurrency test (30 concurrent → 5 assigned / 25 denied / 0 errors, seatsUsed=5).

### LRN-016

```yaml
id: LRN-016
date: 2026-07-03
category: process
source_cs: CS10
status: open
tags: [aspire, dotnet, sub-agent-dispatch]
```

**Problem:** A sub-agent dispatched to add an Aspire service to `AppHost.cs` was scoped to own only `AppHost.cs`, but could not complete without touching a file outside its declared ownership.

**Finding:** Adding an Aspire service reference requires editing BOTH `AppHost.cs` AND `AppHost.csproj` — the Aspire AppHost SDK source-generates the `Projects.<ProjectName>` type from the `<ProjectReference>` in the `.csproj`, so `AppHost.cs` cannot compile without the csproj reference. A sub-agent briefing that adds a new Aspire service must grant write-ownership of `AppHost.csproj` (and, for a whole new project, the `.sln` + `Directory.Packages.props`) alongside `AppHost.cs`.

**Evidence:** PR #18; sub-agent `cs10-entitlements-service` escalation; `AppHost.cs` uses `Projects.AuthzEntitlements_Entitlements_Service`, generated from the `AppHost.csproj` `ProjectReference`.

### LRN-017

```yaml
id: LRN-017
date: 2026-07-03
category: architectural
source_cs: CS10
status: open
tags: [dotnet, http, fail-closed, entitlements]
```

**Problem:** An intra-cluster entitlements-decision endpoint conflated a TRANSIENT store failure with a BUSINESS "deny", and a fail-closed client's sentinel fields were wire-deserializable.

**Finding:** When the quota-consume optimistic-retry loop exhausts, returning a `200 {allowed:false}` deny made the `Bank.Api` enforcer mislabel a transient failure as business "quota exceeded" (429). Return a graceful **503** for transient/infrastructure failure (kept distinct from the `200` allow/deny business contract) so a fail-closed client maps it to its `Unavailable` sentinel → 503, not a business status. Relatedly, client-side result records that carry an `IsUnavailable`/`Reason` sentinel "set only locally" must be annotated `[JsonIgnore]` so a wire payload can never inject them. General pattern: keep transient-failure signalling (5xx) distinct from business decisions (2xx allow/deny), and make local-only sentinel fields non-deserializable.

**Evidence:** PR #18; `src/AuthzEntitlements.Entitlements.Service/Endpoints/EntitlementsEndpoints.cs` `ConsumeQuotaAsync`; `src/AuthzEntitlements.Bank.Api/Entitlements/EntitlementsEnforcer.cs` + `EntitlementsContracts.cs`; Copilot PR review (rounds 1–4).

### LRN-018

```yaml
id: LRN-018
date: 2026-07-03
category: process
source_cs: CS10
status: open
tags: [git, ci, commit-trailers, windows]
```

**Problem:** Integrating `main` into a long-running CS content branch (to pick up a sibling CS) tripped the commit-trailer gates in two non-obvious ways.

**Finding:** (1) The B1 commit-trailer gate enforces the `Co-authored-by: Copilot` trailer on **every** commit in the PR range, INCLUDING the merge commit — `git merge`'s auto-generated "Merge branch …" message has no trailer, so B1 / `harness lint` fail on content PRs (B1 skips only for `workboard-only`-labelled PRs). Fix: `git commit --amend` the merge commit to add the trailer. (2) After a `git rebase --continue` that resolved a conflict, `.git/COMMIT_EDITMSG` keeps the trailer FOLLOWED BY `# Conflicts:` + rebase comment lines; the local `commit-trailers` linter reads `.git/COMMIT_EDITMSG` and treats only the *trailing* run of `Key: Value` lines as the trailer block, so it reports a false "Missing Co-authored-by" even though the committed message is correct. Fix: `git commit --amend --no-edit` to refresh `COMMIT_EDITMSG`.

**Evidence:** CS10 PR #18 merge commit `d6fb750` failed B1 (missing trailer) → amended to `ce39399`; close-out rebase left `# Conflicts:` in `COMMIT_EDITMSG` → local `commit-trailers` false-fail → `git commit --amend --no-edit` cleared it (`Total: 23 passed / 0 failed`).

### LRN-019

```yaml
id: LRN-019
date: 2026-07-03
category: process
source_cs: CS10
status: open
tags: [multi-agent, git, dotnet, aspire]
claim_area: orchestration
```

**Problem:** With multiple orchestrators running CSs in parallel, a CS branch that spans several hours reliably hits merge conflicts as sibling CSs land on `main`.

**Finding:** The recurring conflict surface is a small, predictable set of **shared integration files**: `AuthzEntitlements.sln`, `Directory.Packages.props`, `src/AuthzEntitlements.AppHost/AppHost.cs`, and `WORKBOARD.md` — CS10 conflicted with CS04 (all four) and then with the CS05 claim (`WORKBOARD.md`). Resolutions are almost always **additive** (keep both sides). Reusable techniques: for `.sln` conflicts, `git checkout --theirs -- AuthzEntitlements.sln` then `dotnet sln add <your new projects>` is safer than hand-merging Project GUIDs; for `AppHost.cs`, keep both service registrations and reconcile a single `var bankApi = …` when a sibling captured that resource into a variable; for `Directory.Packages.props`, keep both per-CS `<ItemGroup>`s. After resolving, expect a new HEAD → re-attest the latest review Go row against it (stale-diff A4) and re-engage Copilot (A16).

**Evidence:** CS10 PR #18 (merged CS04: `.sln` / `Directory.Packages.props` / `AppHost.cs` / `AppHost.csproj` / `Program.cs`) + close-out PR #21 (`WORKBOARD.md` vs the CS05 claim); `.sln` resolved via `checkout --theirs` + `dotnet sln add`.

### LRN-020

```yaml
id: LRN-020
date: 2026-07-03
category: process
source_cs: CS10
status: open
tags: [review, copilot]
```

**Problem:** Copilot PR review, re-engaged after each fix HEAD, kept re-raising already-fixed comments and surfaced a new nit almost every round — risking an unbounded fix → re-engage loop.

**Finding:** Copilot re-emits its FULL comment set on every re-review (it re-scans the whole diff), so previously-addressed items reappear as fresh threads even when the fix is in place; and each re-engage tends to find 1–2 additional (often cosmetic/edge-case) nits. Copilot `COMMENTED` (not `CHANGES_REQUESTED`) is non-blocking, and the A16 gate only needs a Copilot review on the current HEAD submitted after the latest local Go. Convergence tactic: fix the genuinely-substantive findings, then set a hard stop — do a final Copilot re-engage, RESOLVE all resulting threads (real + re-raised), and merge WITHOUT pushing further commits (each new commit resets the loop and re-triggers async review). Triage each thread as "false-positive re-raise of a fixed item" (resolve) vs "new substantive bug" (fix once, then re-enter the hard stop).

**Evidence:** CS10 PR #18 — 5 Copilot rounds; the quota-500 + audit-casing comments were re-raised ~4× after being fixed; the one genuinely-new substantive finding each round (release-lock, quota-remaining off-by-one, transient-503 mislabel) was fixed and the rest resolved; merged after the final re-engage + full thread resolution.

### LRN-021

```yaml
id: LRN-021
date: 2026-07-03
category: architectural
source_cs: CS05
status: open
tags: [pdp, authz, parity, adapters]
```

**Problem:** The CS05 reference PDP is the parity oracle the CS06–CS09 engine adapters are compared against via the shared scenario catalog. An adapter that faithfully mirrors the Bank.Api rules must agree with the reference provider on every scenario — including cases where *several* checks fail at once and only the *first-failing* reason is reported.

**Finding:** Mirroring the rule *set* is not enough — the rule *order* must match Bank.Api too. CS05 R1 review caught the reference provider checking segregation-of-duties (maker==checker) *before* pending-status, whereas Bank.Api `Approval.Decide` checks `Status != Pending` first. For a request that is both a self-approval AND already-decided, the two disagreed on the reason code (`MakerEqualsChecker` vs `NotPending`). The fix reordered the PDP to pending-before-SoD. CS06–CS09 adapters MUST replicate the reference provider's ordered checks (scope → role → subject-is-maker → tenant → pending → SoD), not just the predicates, or they will fail catalog parity on combined-failure scenarios.

**Evidence:** CS05 PR #24; `ReferenceDecisionProvider.EvaluateApprovalDecision`; `Approval.cs:26-35`; catalog scenario `manager-approve-own-txn-sod` (pending) vs `manager-approve-already-approved`; R1 Block → R2 Go.

**Implications carried forward:**
- CS06–CS09 adapter briefings must require matching the reference provider's *check order*, verified by `ScenarioCatalogRunner` (primary-reason-code parity), not just decision parity.

### LRN-022

```yaml
id: LRN-022
date: 2026-07-03
category: tooling
source_cs: CS05
status: open
tags: [dotnet, opentelemetry, testing, metrics]
```

**Problem:** The PDP telemetry primitives (`ActivitySource`, `Meter`, `Counter<long>`) are process-wide statics. Tests that assert on emitted metrics/spans via `MeterListener`/`ActivityListener` see measurements from every test in the process, so a naive assertion is flaky under xUnit's cross-class parallelism.

**Finding:** Isolate a metric-counter assertion by a discriminator tag the code does NOT transform. The `action` tag is normalized to a bounded vocabulary (known verb or `unknown`) for cardinality safety, so it can no longer serve as a per-test key — filter on a unique **provider** name instead (register a stub `IAuthorizationDecisionProvider` with a `Guid`-based Name and filter measurements where the `provider` tag equals it). The span `pdp.action` tag keeps the *raw* action, so span tests may still isolate by a unique probe action. Combine with a lock-guarded capture list and a delta measurement.

**Evidence:** CS05 PR #24; `PdpDecisionServiceHooksTests.Evaluate_IncrementsDecisionCounter_...` (rewritten to isolate by provider name after the metric-action normalization landed); `CopilotHardeningTests` metric test.

**Implications carried forward:**
- CS06–CS09 adapter tests and any CS that asserts on the shared PDP telemetry must isolate by an untransformed tag (provider) or a raw span tag, never by the normalized metric `action`.

### LRN-023

```yaml
id: LRN-023
date: 2026-07-03
category: process
source_cs: CS12
status: open
tags: [orchestration, observability, aspire, integration]
claim_area: observability
```

**Problem:** CS12 (observability) and CS05 (PDP) were developed concurrently. CS12's AppHost wiring fanned OTLP to the collector for the four services that existed on its branch base, but CS05 added a fifth ServiceDefaults service (`authz-pdp`, with its own PDP-decision `ActivitySource`/`Meter`) that merged to `main` in parallel — so after both merged, `authz-pdp` was silently unwired and its telemetry went nowhere.

**Finding:** A cross-cutting deliverable phrased as "all services" (here: "fan ServiceDefaults OTel out to the collector") is a moving target when sibling CSs add new services concurrently. Neither implementer CS catches it — the gap only exists in the *merged* tree. The **plan-vs-implementation close-out review caught it** by grepping the CURRENT `AppHost.cs` for every `AddServiceDefaults()`/`AddProject` consumer instead of trusting the branch-time service set. Fixed in PR #32.

**Evidence:** CS12 PVI review round 1 = NEEDS-FIX (authz-pdp unwired); PR #32 wired it (`OTEL_EXPORTER_OTLP_ENDPOINT` + `WaitFor(observability)`); round 2 = GO. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- For any cross-cutting "all services / all X" deliverable, the close-out plan-vs-impl review MUST enumerate the current set from the merged tree, not the branch-base set — concurrent sibling merges can add members after your branch forks.
- When a CS adds a new ServiceDefaults service (CS06–CS09 adapters, future services), wire it to the observability collector in the same PR.

### LRN-024

```yaml
id: LRN-024
date: 2026-07-03
category: architectural
source_cs: CS12
status: open
tags: [grafana, otel-lgtm, security, aspire, observability]
claim_area: observability
```

**Problem:** Exposing the bundled `grafana/otel-lgtm` Grafana with anonymous access for a frictionless lab. Lowering only the anonymous org role to `Editor` (from `Admin`) is insufficient: the image ships Grafana with the default `admin/admin` account, so anyone reaching the exposed UI could still log in as admin and escalate past the Editor cap.

**Finding:** A complete anonymous-Editor "kiosk" needs the anonymous settings PLUS the default-admin auth paths closed: `GF_AUTH_ANONYMOUS_ENABLED=true` + `GF_AUTH_ANONYMOUS_ORG_ROLE=Editor` + **`GF_AUTH_DISABLE_LOGIN_FORM=true`** (no UI login) + **`GF_AUTH_BASIC_ENABLED=false`** (no HTTP Basic Auth). Disabling the login form ALONE still leaves Basic Auth (`curl -u admin:admin`) open. Separately, model the OTLP ingest ports (4317/4318) as `tcp` (not `http`) endpoints so `WithExternalHttpEndpoints()` marks ONLY the Grafana UI external, keeping ingest off-box; build the `http://host:port` exporter URL explicitly via `ReferenceExpression`. Datasource/dashboard provisioning is file-based at image startup and is unaffected by disabling interactive auth.

**Evidence:** CS12 Copilot + GPT-5.5 review rounds; verified on `grafana/otel-lgtm:0.28.0`: `disableLoginForm=true`, `admin/admin` Basic Auth no longer authenticates as admin, anonymous `/api/org` works, Prometheus/Loki/Tempo datasources + both dashboards still provision. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- Any future externally-exposed dev UI backed by an image with a default admin account (Grafana, etc.) must disable BOTH the login form AND Basic Auth for an anonymous-only posture — anonymous role alone is not a boundary.
- Model non-UI container ingress ports as `tcp` so `WithExternalHttpEndpoints()` does not inadvertently expose them.

### LRN-025

```yaml
id: LRN-025
date: 2026-07-03
category: process
source_cs: CS06
status: open
tags: [multi-agent, claim, rebase, workboard-auto-approve, ci]
```

**Problem:** During CS06's workboard claim PR, a *different* orchestrator closed out a dependency CS (CS05: content merge + active→done) on `main` while the claim branch was being prepared, advancing `main` past the claim branch's base.

**Finding:** The `workboard-auto-approve` `validate-and-approve` job compares the PR's **2-dot** `git diff base_sha head_sha` file count against the GitHub API's **3-dot** `changed_files` count and **fails closed** when they diverge ("immutable git diff returned N files but PR reports M changed files"). When a branch falls behind `main`, the 2-dot diff picks up the other agent's merged changes and the counts mismatch. Fix: **rebase the branch onto latest `origin/main`** before the gate runs; the two counts then agree. This is a routine hazard for a fleet of parallel orchestrators — not specific to claim PRs.

**Evidence:** CS06 claim PR #30 `validate-and-approve` failed with a 6-vs-2 changed-files mismatch after CS05 close-out (#29) landed; `git rebase origin/main` (onto cbebaf1) made CI green and it squash-merged. `.github/workflows/workboard-auto-approve.yml`.

**Implications carried forward:**
- In a multi-orchestrator repo, rebase any branch onto latest `origin/main` before push/merge if `main` advanced since branching — especially when a dependency CS may have closed out concurrently.

### LRN-026

```yaml
id: LRN-026
date: 2026-07-03
category: architectural
source_cs: CS06
status: open
tags: [pdp, adapters, rbac, casbin, aspnet, parity]
claim_area: engines
```

**Problem:** The CS05 `ScenarioCatalogRunner` passes a provider ONLY when it returns the exact `Decision` AND primary reason code (`Reasons[0].Code`) in the reference's per-action ordering — but the fintech rules are mostly ABAC (tenant, subject-is-maker, pending, SoD, threshold), while CS06's engines (ASP.NET Core policies, Casbin) are RBAC baselines.

**Finding:** Factor the adapter so a shared `FintechRuleEvaluator` owns the engine-agnostic part (per-action ordering + ABAC + obligations, in lock-step parity with the reference) and delegates ONLY role eligibility to the engine via `IEngineRoleAuthorizer.IsRoleAuthorized(action, roles)`. Each adapter is then thin (`Evaluate => FintechRuleEvaluator.Evaluate(request, this)`) and encodes its eligible-role SETS in its engine's native policy form (ASP.NET `RolesAuthorizationRequirement`; Casbin `(role, action)` policy pairs). Catalog parity is guaranteed by the shared evaluator while the engine genuinely owns the RBAC decision — realizing "same question, swappable engine" and honoring the CS06 plan-review amendment by handling ALL 22 cases rather than unsupported-denying non-RBAC ones.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/{FintechRuleEvaluator,IEngineRoleAuthorizer}.cs` + `AspNetCore/AspNetCorePolicyProvider.cs` + `Casbin/CasbinDecisionProvider.cs`; both adapters pass all 22 `FintechScenarioCatalog` scenarios (PR #34; PDP tests 235/235).

**Implications carried forward:**
- CS07–CS09 adapter authors: reuse the `IEngineRoleAuthorizer` seam for RBAC-only engines. For richer engines — OpenFGA (ReBAC, CS07), OPA/Rego (CS08), Cedar (CS09) — weigh how much of the fintech decision the engine should own *natively* vs. compose via the shared evaluator; don't force the shared-evaluator split where the engine can express the full decision.

### LRN-027

```yaml
id: LRN-027
date: 2026-07-04
category: architectural
source_cs: CS08
status: open
tags: [pdp, adapters, opa, http, resilience, fail-closed]
claim_area: engines
```

**Problem:** An out-of-process PDP adapter must satisfy the synchronous `IAuthorizationDecisionProvider.Evaluate` while calling an HTTP engine. ServiceDefaults adds `AddStandardResilienceHandler()` to ALL `IHttpClientFactory` clients (`ConfigureHttpClientDefaults`), raising the concern that a synchronous `HttpClient.Send` would throw through the async-only resilience pipeline.

**Finding:** On .NET 10, synchronous `HttpClient.Send` works through the standard resilience handler for a named client — verified end-to-end (adapter → live OPA → 22/22), so a sync-over-`Send` out-of-process adapter is viable without `.GetAwaiter().GetResult()`. Pair it with three fail-closed disciplines the mocked unit tests can't exercise but a real anonymous `/evaluate` endpoint needs: (1) a backstop `catch (Exception)` so config/construction throws (bad `Opa:BaseUrl`/timeout surfaced inside `CreateClient`) Deny rather than 500; (2) a STABLE, non-sensitive `Reason.Message` (log the detail) since the message is returned to anonymous callers; (3) validate the engine's returned reason code against the bounded `ReasonCodes` vocabulary (fail closed on unknown) so an out-of-process engine can't leak internal detail or inflate `pdp.reason` metric cardinality.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Opa/OpaDecisionProvider.cs`; `src/AuthzEntitlements.ServiceDefaults/Extensions.cs` (`AddStandardResilienceHandler`); live `POST /api/authz/scenarios/verify` 22/22 with `Pdp:Provider=opa`; Copilot PR #38 flagged (1) message info-leak and (2) untrusted reason-code.

**Implications carried forward:**
- CS07/CS09 out-of-process adapters (OpenFGA, Cedar): sync `HttpClient.Send` via a named client is fine; always fail closed on ANY exception, sanitize caller-facing messages, and validate the engine's decision/reason against `ReasonCodes` before mapping to `AccessDecision`.

### LRN-028

```yaml
id: LRN-028
date: 2026-07-04
category: process
source_cs: CS08
status: open
tags: [multi-agent, adapters, tests, merge-order, pdp, ci]
claim_area: engines
```

**Problem:** CS06 and CS08 each merged green in isolation, but after both landed on `main` a CS06 test (`AdapterProviderSelectionTests.AddPdp_RegistersReferenceAndBothAdapters`) failed: it asserted the EXACT registered provider set `[aspnet, casbin, reference]` and CS08's new `opa` provider made it `[aspnet, casbin, opa, reference]`. Each PR's CI was green against its own base, so nothing caught it pre-merge.

**Finding:** Exhaustive-set assertions over a registry that multiple parallel CSs extend create cross-CS coupling CI cannot catch. Assert MEMBERSHIP of the CS's own additions (`Assert.Contains`) plus a uniqueness check that matches the production invariant — the factory de-dupes case-insensitively, so assert `names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length` — never an exact-equal of the whole set. Fixed in #40.

**Evidence:** `tests/AuthzEntitlements.Authz.Pdp.Tests/AdapterProviderSelectionTests.cs`; `main` went 263/1 PDP after #38 merged atop CS06; PR #40 relaxed to membership + case-insensitive uniqueness (Copilot flagged the case-sensitivity mismatch).

**Implications carried forward:**
- CS09 (Cedar) and any future registry-extending CS: assert your OWN additions are present, never the exhaustive set, so the next parallel adapter doesn't red `main`.

### LRN-029

```yaml
id: LRN-029
date: 2026-07-04
category: process
source_cs: CS08
status: open
tags: [opa, rego, csharp, tooling, tests, windows]
```

**Problem:** Two authoring gotchas surfaced during CS08 implementation.

**Finding:** (1) `opa fmt` reformats freshly-authored `.rego` (tabs/spacing) even when `opa check` is clean — run `opa fmt -w infra/opa/policy` BEFORE any `opa fmt --list` gate to avoid a false diff. (2) C# raw *interpolated* string literals with trailing braces (`$$"""{"k":"{{x}}"}}"""`) fail to compile (CS9007) because the closing `}}` is parsed as an interpolation close — use plain string concatenation (or `{{{{` escaping) for JSON-with-braces xUnit fixtures.

**Evidence:** cs08-impl-policy + cs08-impl-adapter sub-agent reports; `OpaDecisionProviderTests.cs` uses concatenation for the parameterized deny-reason JSON fixture.

**Implications carried forward:**
- CS09 (Cedar policy + tests): run the formatter's write mode before the check gate; avoid raw interpolated strings for brace-heavy test fixtures.

### LRN-030

```yaml
id: LRN-030
date: 2026-07-04
category: architectural
source_cs: CS07
status: open
tags: [authz, pdp, adapter, fail-closed, security, openfga]
```

**Problem:** CS07's `OpenFgaProvider` built and passed all tests, but review (GPT-5.5 R16 + Copilot rd.12/13) found it FAIL-OPEN: `Evaluate` threw on a not-configured/unreachable engine, and since `PdpDecisionService` wraps providers in no try/catch, `/api/authz/evaluate` returned a raw 500 instead of a Deny. `/api/authz/rebac/verify` likewise only caught around `EnsureBootstrappedAsync`, not the (singleton, bootstrapped-once) scenario `Check` loop — so a *later* call could still 500.

**Finding:** An out-of-process authz adapter must FAIL CLOSED on ANY engine failure: catch (never throw), return a Deny with a provider-local reason code and a **stable, non-sensitive** message (log the cause; never surface network/config detail to anonymous callers), and wrap the WHOLE request flow, not just the first call. Mirror the established sibling `OpaDecisionProvider` (provider-local `ProviderUnavailable`/`EngineUnavailable`, sanitized message, backstop `catch (Exception)`). Build+tests do NOT catch fail-open — only review does; add a fail-closed unit test (blank ApiUrl → Deny, not throw) to lock it in.

**Evidence:** `OpenFgaProvider.Evaluate` try/catch → `Deny(EngineUnavailable)`; `RebacEndpoints` `UnavailableProblem` stable messages + whole-flow `/verify` wrap; `OpenFgaRegistrationTests.Evaluate_FailsClosed_WhenEngineUnavailable`. Mirrors LRN-027 (OPA fail-closed).

**Implications carried forward:**
- CS09 (Cedar) and any future out-of-process adapter: fail closed on every engine-error path (Deny + stable message + logged cause), add a fail-closed test, and wrap the entire endpoint flow — the singleton-bootstrap gotcha makes "the first call fails" reasoning wrong.

### LRN-031

```yaml
id: LRN-031
date: 2026-07-04
category: process
source_cs: CS07
status: open
tags: [openfga, rebac, sdk, csharp, aspire, followups]
```

**Problem:** OpenFGA (out-of-process, async SDK, versioned models) integration surfaced several authoring gotchas plus deferred hardening.

**Finding:** (1) The sync `IAuthorizationDecisionProvider.Evaluate` bridges the async `OpenFga.Sdk` with `GetAwaiter().GetResult()` (sanctioned by the contract) — the pattern any async adapter needs. (2) OpenFGA authorization models are **immutable/versioned**: bootstrap writes the exact embedded model and pins the returned model id (favour correctness over reusing a possibly-stale prior version); a dedicated store per `StoreName` keeps tuple reconciliation O(seed). (3) `Dictionary.KeyCollection` IS `IReadOnlyCollection` on net10 so a `Keys` cast does not throw, but materialize (`.ToArray()`) to avoid a fragile runtime-type-dependent cast. (4) `openfga/openfga` runs as a two-step `migrate` (one-shot) + `run` server on postgres (`OPENFGA_DATASTORE_ENGINE/URI`).

**Evidence:** `OpenFgaRebacService` (lazy client, idempotent bootstrap, model-id pinning, read-diff tuple write); `OpenFgaProvider` sync bridge; `AppHost.cs` migrate+run containers; Copilot rounds (SupportedActions cast, per-boot model-version growth).

**Implications carried forward:**
- Follow-ups (deferred, non-blocking dev-loop hardening): make the OpenFGA authorization-model id configurable/pinned to avoid per-boot model-version growth on a persistent shared store; use a targeted tuple-existence reconciliation instead of read-all (fine for the dedicated tiny-seed store today); adopt `Assert.Skip` for the integration tests when the repo moves to xUnit v3 (currently a soft `return` skip, since 2.9.3 has no dynamic skip and adding `Xunit.SkippableFact` was out of scope).

### LRN-032

```yaml
id: LRN-032
date: 2026-07-04
category: architectural
source_cs: CS09
status: open
tags: [pdp, adapter, cedar, monocloud, dotnet10, parity]
```

**Problem:** CS09 integrates Cedar (in-process, `MonoCloud.Cedar` native bindings) as a fifth engine that must answer the shared 22-scenario `FintechScenarioCatalog` with the SAME `Decision` AND primary reason code as the reference — but Cedar is a declarative permit/forbid engine with no ordered "first-failing reason", and `PolicySet.ParsePolicies` assigns its own sequential `policyN` ids (ignoring `@id`), so a determining-policy set can't be mapped back to a reason code from raw-text policies.

**Finding:** (1) Build the `PolicySet` from explicit `Policy(source, id)` objects (not `ParsePolicies`) so `AuthorizationSuccessResponse.GetReason()` returns STABLE, semantic ids the adapter maps to `ReasonCodes`. (2) Model each action as a broad `permit` + one annotated `forbid` per deny reason; on Deny, map the determining-forbid set to the reference's FIRST-failing reason by selecting the LOWEST `Precedence` value (per-action order) — reproducing the reference's short-circuit ordering (LRN-021) for ANY input, not just isolated-failure catalog rows. (3) Per LRN-026, let Cedar own the FULL decision natively (like OPA/Rego), not the role-gate-only `IEngineRoleAuthorizer` split — Cedar is expressive enough; the head-to-head with OPA is that both answer the same catalog. (4) `MonoCloud.Cedar` 0.1.0 restores/builds 0/0 and loads its win-x64 native binary under the .NET 10 RC runtime with no extra setup. (5) Obligations, the unknown-action guard, and fail-closed (any Cedar error → provider-local `ProviderUnavailable` Deny, never throw/permit; pass the exception object to the logger for stack traces) are adapter-side, mirroring `OpaDecisionProvider`.

**Evidence:** `Providers/Adapters/Cedar/{CedarPolicyModel,CedarDecisionProvider}.cs`; `CedarDecisionProviderTests` (22/22 catalog parity + per-scenario + obligations + combined-failure ordering + fail-closed + selection); `Directory.Packages.props` MonoCloud.Cedar 0.1.0 pin; `docs/authz/cedar-adapter.md` (+ Amazon Verified Permissions as the managed/cloud option). Full-solution build 0/0; PDP `dotnet test` 358/358.

**Implications carried forward:**
- Future declarative-policy adapters (and CS16 explainability / CS20 migration): to recover an ORDERED reason from an unordered engine, encode failures as annotated forbids with stable ids + an explicit precedence map, and select the first-failing (lowest precedence) determining member.
- CS23/CS24 (comparison/perf): Cedar (in-process, `cedar`) and AVP (managed) are the Cedar data points; AVP runs the same policies managed (documented, not wired).

### LRN-033

```yaml
id: LRN-033
date: 2026-07-04
category: process
source_cs: CS09
status: open
tags: [pdp, parity, testing, fail-closed, tenant, security]
```

**Problem:** CS09's Cedar adapter passed all 22 `FintechScenarioCatalog` scenarios and full build/test, but GPT-5.5 review (R1 Block) found a FAIL-OPEN tenant-isolation gap the catalog missed: with BOTH tenants null/blank, Cedar mapped them to `""` and `"" == ""` PERMITTED, whereas the reference `TenantMatches` fails closed (`!IsNullOrWhiteSpace(subject) && !IsNullOrWhiteSpace(resource) && equal`). Every catalog row uses non-blank tenants, so tests stayed green over a real vuln.

**Finding:** A shared parity catalog of "realistic" values does NOT exercise fail-closed predicates on degenerate/boundary inputs. For every fail-closed rule (tenant, maker, status, scope), add explicit tests with null/empty/whitespace on EACH side, and assert engine parity against the `ReferenceDecisionProvider` oracle (Decision + `Reasons[0].Code`), not just a hardcoded expectation. The Cedar fix: normalize null/whitespace tenant → `""` AND require both sides non-empty in the forbid (`principal.tenant != "" && resource.tenant != "" && principal.tenant == resource.tenant`).

**Evidence:** `CedarDecisionProvider.NormalizeTenant`; the four tenant forbids in `CedarPolicyModel`; `CedarDecisionProviderTests` 7 blank/null/whitespace-tenant tests asserting equivalence to `ReferenceDecisionProvider`. GPT-5.5 R1 Block → R2 Go-with-amendments.

**Implications carried forward:**
- CS16/CS17/CS20/CS23/CS24 and any adapter/eval CS: the 22-scenario catalog is necessary but NOT sufficient — augment with degenerate-input fail-closed parity tests against the reference oracle. Consider adding blank/whitespace-attribute rows to the shared catalog so every engine is held to them.

### LRN-034

```yaml
id: LRN-034
date: 2026-07-04
category: architectural
source_cs: CS17
status: open
tags: [pdp, authzen, fail-closed, validation, wire-boundary, security]
```

**Problem:** CS17's new AuthZEN Access Evaluation endpoint (`POST /api/authz/authzen/evaluation`) is a real, audited decision surface over UNTRUSTED external wire input, but it initially reused `AuthZenMapper`'s lenient safe defaults. GPT-5.5 review (R1 Needs-Fix) found a FAIL-OPEN: a present-but-unparseable `amount` coerced to null → `ReferenceDecisionProvider` treats null amount as $0 → a large transfer could return Permit + `post_immediately` (threshold bypass); and an omitted `maker_id` on approve/reject makes `SubjectIsMaker` false → segregation-of-duties passes (self-approval bypass). All 22 catalog scenarios use well-formed attributes, so tests stayed green over the gap — the LRN-033 pattern recurring at a new boundary.

**Finding:** A lenient internal mapper is safe for a TYPED in-process caller (`/evaluate` takes a built `AccessRequest`) but becomes fail-OPEN when reused at a new UNTRUSTED wire boundary. Add boundary-specific, action-aware fail-closed validation BEFORE evaluation: reject a present-but-unparseable numeric field for any action; require the attributes each action's rules key on (`bank.transaction.create` → parseable `amount` + non-blank `maker_id`; approve/reject → non-blank `maker_id` + `status`). Do NOT tighten the shared reference provider (cross-CS scope); harden at the new boundary and add a test that EVERY existing catalog scenario still validates (guard against over-tightening).

**Evidence:** `AuthZenRequestValidation.Validate` (fail-closed shape + attribute checks); `AuthZenEndpoints` calls it (400) before `PdpDecisionService.Evaluate`; `AuthZenConformanceTests` (+9: unparseable/missing amount, missing maker/status on create + approve/reject, `Validate_EveryCatalogScenario_PassesValidation`). GPT-5.5 R1 Needs-Fix → R2 Go.

**Implications carried forward:**
- Any future CS adding a new external decision/enforcement endpoint (CS14/CS15/CS19/CS21): treat the wire boundary as untrusted and add action-aware fail-closed input validation; a passing shared catalog does not prove the boundary is safe.

### LRN-035

```yaml
id: LRN-035
date: 2026-07-04
category: process
source_cs: CS17
status: open
tags: [ci, testing, posture, process]
```

**Problem:** CS17's exit criterion "policy changes are gated by CI tests" collides with this repo's DELIBERATE posture that GitHub Actions run process-gates-only (`harness lint` + drift + review-evidence) while .NET build/test is the LOCAL correctness gate (CONTEXT.md; `.github/workflows/` carry no dotnet step). Adding an active `.NET` CI workflow would change that posture AND interacts with the `workflow-pins` gate (actions must be SHA-pinned).

**Finding:** When a CS deliverable's literal wording conflicts with an established repo posture/decision, do NOT silently change the posture. Deliver the INTENT (here: a runnable policy test suite of +59 golden/property/conformance tests that any policy change must pass) + a documented, ready-to-adopt opt-in path (a `policy-tests.yml` snippet in `docs/authz/policy-lifecycle.md`), and ESCALATE the posture decision to the maintainer (PR #55 Notes). The plan-vs-impl review marked CI-gating `diverged` (intentional), not `dropped`, and returned GO.

**Evidence:** `docs/authz/policy-lifecycle.md` CI note + adoption snippet; PR #55 Notes (escalation); the plan-vs-impl review in `done_cs17_*` (D1-CI = diverged, Outcome GO).

**Implications carried forward:**
- Maintainer decision pending: adopt a scoped `.NET` policy-tests workflow, or keep the local-gate posture? Until decided, CS17's gate is the local `dotnet test` suite.
- Future eval/testing CSs (CS23/CS24) that mention "CI" should resolve this posture first.

### LRN-036

```yaml
id: LRN-036
date: 2026-07-04
category: tooling
source_cs: CS16
status: open
tags: [dotnet, line-endings, lint, windows, ci]
```

**Problem:** The dotnet dispatch profile's `dotnet format --verify-no-changes` self-check is incompatible with this repo's enforced LF convention (`.gitattributes` `* text=auto eol=lf`). With no `.editorconfig` `end_of_line`, `dotnet format` defaults to CRLF and flags LF files that have comments inside argument/parameter lists — a whole-solution run flags 6+ PRE-EXISTING untouched files (AppHost.cs, AuthorizationSetup.cs, BankSeeder.cs, ...), so the gate already fails on `main`. Separately, the file-authoring tool writes **CRLF** for new `.cs` files on Windows, and the `harness lint` **text-encoding gate did NOT flag** those CRLF `.cs` files (lint passed green) — the CRLF only surfaced as a `git add` "CRLF will be replaced by LF" warning.

**Finding:** `harness lint` (text-encoding) + `.gitattributes eol=lf` are the AUTHORITATIVE line-ending gates, NOT `dotnet format`. Do not convert files to CRLF to satisfy `dotnet format`. For authored/new `.cs` (or any) files, explicitly convert working-copy CRLF→LF (`[IO.File]::WriteAllText($p, ($t -replace "\r\n","\n"), (New-Object Text.UTF8Encoding $false))`) before committing — do not rely on the text-encoding lint to catch `.cs` CRLF; git's eol=lf will normalize the committed blob, but a clean working copy avoids the add-time warning and reviewer confusion. Treat the dotnet-profile `dotnet format --verify-no-changes` self-check as advisory for this repo until a repo `.editorconfig` with `end_of_line = lf` is added (or the check is dropped from the profile).

**Evidence:** CS16 foundation sub-agent report (dotnet format flags 8 files, 6 pre-existing untouched); this session's `git add` emitted "CRLF will be replaced by LF" for 3 foundation-created `.cs` files while `harness lint` had reported 22/0 with those files present. Repo enforces LF via `.gitattributes:2`.

**Implications carried forward:**
- Any .NET CS with new source files (CS17+/CS20/CS24): convert authored files to LF explicitly; trust `harness lint` + `.gitattributes`, not `dotnet format`, for line endings. Consider a dedicated CS to add `.editorconfig end_of_line = lf` so `dotnet format` aligns with the repo mandate.

### LRN-037

```yaml
id: LRN-037
date: 2026-07-04
category: tooling
source_cs: CS16
status: open
tags: [opa, rego, testing, windows]
```

**Problem:** A Rego-editing task needs `opa test` to validate policy changes, but the `opa` CLI is not preinstalled on the dev box, and the .NET test suite (which mocks the OPA HTTP response) does not exercise the actual Rego.

**Finding:** The official OPA binary at `https://openpolicyagent.org/downloads/latest/opa_windows_amd64.exe` runs **standalone** (no install/PATH changes): download it, run `opa test infra/opa/policy -v`, then delete it. CS16's OPA sub-agent used this to validate the added `rule` field (`opa test` 51/51) without any environment setup.

**Evidence:** CS16 `cs16-opa` sub-agent report (fetched OPA 1.18.2, ran `opa test` 51/51, removed the binary). AppHost pins `openpolicyagent/opa:1.18.2-static` for the container path.

**Implications carried forward:**
- CS17 (policy lifecycle/testing), CS20, CS24 and any Rego-touching CS: validate Rego edits with the standalone `opa` download rather than assuming a preinstalled CLI or relying solely on the mocked C# adapter tests.

### LRN-038

```yaml
id: LRN-038
date: 2026-07-04
category: architectural
source_cs: CS16
status: open
tags: [openfga, rebac, testing, mocking, fail-closed]
```

**Problem:** CS16 needed to assert the OpenFGA adapter's permit/deny `DecisionExplanation` (engine=openfga, DeterminingRule=relationship, the relationship-tuple ref) in the OFFLINE default test suite, but `OpenFgaProvider.Evaluate` reaches the explanation only after a live `Check`, and `OpenFgaRebacService` is a **sealed, non-virtual** concrete class with no seam to force `allowed=true` offline (a blank `ApiUrl` throws in `BuildClient`).

**Finding:** The offline suite can only verify the relationship-tuple reference FORMAT (from the pure `OpenFgaRequestMapper` output) + the fail-closed/boundary explanations; the actual permit/deny Engine/DeterminingRule assertion requires the live-server integration suite (self-skipping) or a runtime smoke. To make ReBAC permit/deny explanations unit-testable offline, `OpenFgaRebacService` would need an extracted interface (e.g. `IOpenFgaCheckClient`) the provider depends on — a small refactor deferred out of CS16 (additive-only) but worth doing when ReBAC is next touched.

**Evidence:** CS16 `cs16-openfga` sub-agent report; `OpenFgaRebacService` is `sealed` with concrete `CheckAsync`; CS16 verified the permit/deny explanation via the runtime `/evaluate` smoke instead.

**Implications carried forward:**
- CS20 (migration/portability), CS24 (perf), or any ReBAC-touching CS: extract an `IOpenFgaCheckClient` seam so ReBAC decisions/explanations are unit-testable without a live OpenFGA server.

### LRN-039

```yaml
id: LRN-039
date: 2026-07-04
category: process
source_cs: CS16
status: open
tags: [ci, review-evidence, pr-body, review-log]
```

**Problem:** The `read-only-gates` / `review-log-evidence` CI gate rejected the CS16 content PR body with "## Review log row N contains template placeholder cell(s)" even though every row was fully filled — the offending cells merely contained the literal word "placeholder(s)" (e.g. "placeholders/args aligned") and `<role>`/`<action>` angle-bracket tokens inside a prose evidence cell.

**Finding:** `check-review-evidence` scans Review-log cells for template-placeholder patterns and flags the literal substring `placeholder` and `<...>` angle-bracket tokens ANYWHERE in a cell (not just the template's `_(...)_` form). When authoring Review-log evidence prose, avoid the word "placeholder" and any `<...>` tokens (write "format-string/arg alignment", "the `p, role, action` policy line", etc.). Also: the A3+A4 gate requires a `verdict=Go` row whose `analyzed_head` equals the CURRENT PR HEAD SHA — every new commit needs a fresh decisive-review row at the new HEAD.

**Evidence:** CS16 PR #56 CI: `review-log-evidence` failed on "row 4 contains template placeholder cell(s)" until "placeholders/args aligned" → "format-string + args aligned" and "`p, <role>, <action>`" → "`p, role, action`"; `A3+A4 review-evidence` failed until a `Go` row at HEAD `8f3b8cf`/`6f5e025` was appended.

**Implications carried forward:**
- Every content-PR review-log author: keep evidence cells free of the word "placeholder" and `<...>` tokens, and append a fresh `Go`-at-HEAD row after each new commit (including review-fix commits) or the gate fails.

## Applied

_(no entries yet)_

## Obsolete

_(no entries yet)_

## Deferred

_(no entries yet)_