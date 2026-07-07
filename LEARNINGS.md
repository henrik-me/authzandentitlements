# Learnings

Learnings filed during the project. See [`RETROSPECTIVES.md`](RETROSPECTIVES.md) for harvest procedure and entry format.

---

> **ID sequencing:** Use sequential IDs starting from LRN-001. The linter emits
> warnings for gaps in the sequence but treats them as non-fatal; gaps do not
> cause exit code 1.

**Harvest log — 2026-07-04 (yoga-ae-c3, task CS28h).** First full harvest since project start: all 48 then-open learnings dispositioned (49 total — LRN-006 was already `obsolete`), grouped by theme, and routed to fix/consolidation clickstops. Post-harvest tally: **46 open, 2 deferred, 1 obsolete**. Per the harvest procedure an entry tracked by an unclosed CS stays `open` and flips to `applied` at that CS close-out; CI-enforcement items are `deferred`; superseded items are `obsolete`.

| Disposition | Learnings | Tracking |
|---|---|---|
| Fix — governance tenant-scoping & fail-closed | LRN-044, LRN-049 | planned **CS29** |
| Fix — NuGet suppression/pin re-evaluation | LRN-003, LRN-005 | planned **CS30** |
| Fix — adapter test seams & degenerate parity | LRN-031, LRN-033, LRN-038 | planned **CS31** |
| Fix — observability & audit-event enrichment | LRN-013, LRN-014 | planned **CS32** |
| Consolidate into project-local doc blocks | 37 how-to LRNs (001,002,004,007–012,015–030,032,034,036,037,039,041–043,045–048) | planned **CS33** |
| Deferred — CI enforcement (private-tier) | LRN-035, LRN-040 | re-eval 2026-10-01 / on tier change |
| Obsolete — fixed upstream | LRN-006 | agent-harness v0.13.0 |

---

## Open

### LRN-092

```yaml
id: LRN-092
date: 2026-07-07
category: tooling
source_cs: CS60
status: open
tags: [observability, otlp, grafana, aspire, dual-export, telemetry]
claim_area: observability
```

**Problem:** The Aspire dashboard showed only console logs (no structured logs / traces / metrics) **and** the CS12 Grafana dashboards were empty; the tempting misdiagnosis is "OTLP telemetry export is broken," which would rewire a working delivery path instead of fixing the real causes.

**Finding:** OTLP delivery actually **works**; four causes combined to hide the telemetry. (a) `ContainerLifetime.Persistent` + dynamic host ports let `otel-lgtm` containers accumulate and collide across runs and checkouts, so an operator could open a **stale** instance's empty Grafana while services exported to a different one (collector/Grafana **split-brain**). (b) The CS12 dashboards are 100% `http_server_*` (RED) metrics, so an **idle** stack shows empty panels by design — they need driven inbound traffic. (c) Telemetry was routed **only** to the lgtm collector (the AppHost overrode `OTEL_EXPORTER_OTLP_ENDPOINT`), leaving the Aspire dashboard console-only — no dual-export. (d) **No e2e ever asserted telemetry arrival**, so the regression stayed invisible. CS60 fixes all four: a **single per-run collector** (dropped persistent lifetime; the named `/data` volume still persists history), **dual-export** to both the Aspire dashboard (`OTEL_EXPORTER_OTLP_ENDPOINT`) and the collector (`LGTM_OTLP_ENDPOINT` + a second per-signal ServiceDefaults OTLP exporter, since `UseOtlpExporter()` cannot be combined with `AddOtlpExporter()`), an **e2e telemetry-arrival guard**, and a **stale-container cleanup** runbook. Debugging note: query otel-lgtm's Prometheus with **`curl`** (the image ships `curl`, not `wget` — a `wget` probe returns false-empty results), and remember a persistent `/data` volume can surface **stale series** from prior runs.

**Evidence:** A clean single-collector reproduction (CS60 Task 1): all **7** services appeared as Prometheus `job` values (`audit-service, authz-pdp, bank-api, bank-web, edge-gateway, entitlements-service, governance-service`); `sum(http_server_request_duration_seconds_count)` was **0 while idle** and **357–427 after driving ~420 inbound requests** — exactly the metric the CS12 dashboards query; after dual-export the Aspire proxy (DCP) held **7 established connections** (one per service) to the dashboard OTLP endpoint; and a clean slate converged to a **single** collector container. Implemented in `src/AuthzEntitlements.AppHost/AppHost.cs`, `src/AuthzEntitlements.ServiceDefaults/Extensions.cs`, and `tests/AuthzEntitlements.E2E.Tests/TelemetryArrivalE2ETests.cs`.

**Disposition:** **applied by CS60** — the single per-run collector + dual-export + e2e arrival guard shipped in content PR #213 (squash-merged 2026-07-07 as `fda5819`). The debugging notes (curl-not-wget; stale `/data` series) and split-brain root cause are captured above.

### LRN-091

```yaml
id: LRN-091
date: 2026-07-06
category: operational
source_cs: CS51
status: applied
tags: [get-latest-first, harness, version-pin, startup, sequencing]
claim_area: orchestrator-loop
```

**Problem:** Running the harness CLI (`startup --pull-ff-only`) as the **first action instead of getting latest first** pulled a newer pin mid-command while still executing an older CLI from a stale snapshot — colliding a fresh pull with a stale-CLI `sync` and yielding a cryptic `Template file not found` for a managed target (`DISPATCH-PREAMBLE.md`) that exists only in the newer version. Compounded by hand-maintained `#<ver>` literals in consumer-owned docs that lag the real pin.

**Finding:** Get latest **first** with a plain `git pull` (never `startup`/`sync`/any `npx …agent-harness` command, which runs the stale pin), **then** read the pin from `harness.config.json` `version` (the single source of truth — the harness-managed docs' `#<ver>` command literals are sync-rendered from it), **then** invoke the harness at that pin. Consumer-owned docs must **look up** the command pin from `harness.config.json` rather than hard-code it (historical `#v…` mentions in narrative/evidence are fine). The harness should also enforce this (startup pull-first + re-exec/validate at the pulled pin), tracked via agent-harness#502.

**Evidence:** This session began at `harness.config.json` = v0.17.0 (HEAD `5b0668c`) but the agent's base-instruction snapshot pinned `#v0.12.0`; the first `startup` at `#v0.12.0` failed `sync --mode=check` with `Template file not found: …/template/managed/DISPATCH-PREAMBLE.md (required for managed target "DISPATCH-PREAMBLE.md")`; re-running at `#v0.17.0` (the `harness.config.json` `version`) → 23 passed / 0 failed, no drift. The prior v0.16.0→v0.17.0 incident (PR #157 adds `DISPATCH-PREAMBLE.md`) is recorded in CS51 Background. Consumer-owned pin literals de-hardcoded to a `harness.config.json` lookup in `INSTRUCTIONS.md` (`instructions.harness`), `README.md`, and `ARCHITECTURE.md` by CS51.

**Disposition:** **applied by CS51** — the INSTRUCTIONS.md `instructions.harness` note + the de-hardcoding of consumer-owned pin literals (content PR #209, squash-merged 2026-07-06 as `4de8b09`) are the consumer mitigation; upstream enforcement remains tracked via agent-harness#502.

### LRN-090

```yaml
id: LRN-090
date: 2026-07-06
category: architectural
source_cs: CS59
status: applied
tags: [oidc, logout, keycloak, bank-web, id-token-hint]
claim_area: bank-web-logout
```

**Problem:** Signing out of `bank-web` when there was **no active OIDC session** dead-ended on Keycloak's error page **"We are sorry… Missing parameters: id_token_hint"** (HTTP 400) instead of returning home. The `/logout` endpoint unconditionally drove an OIDC RP-initiated logout (`Results.SignOut([Cookie, Oidc])`).

**Finding:** ASP.NET Core's `OpenIdConnectHandler.SignOutAsync` sets `message.IdTokenHint = await Context.GetTokenAsync(Options.SignOutScheme, "id_token")` — it sources `id_token_hint` from the **saved cookie tokens** (`SaveTokens = true`, `SignOutScheme` = the cookie scheme), **not** from the `AuthenticationProperties` passed to `SignOut`. When `/logout` runs with no active session (expired cookie, a prior sign-out, or a dropped chunk of the large `.AspNetCore.Cookies`), `GetTokenAsync("id_token")` returns null, yet the handler still redirects to Keycloak's end-session endpoint emitting `post_logout_redirect_uri` (the `SignedOutCallbackPath` `/signout-callback-oidc`) with an **empty** `id_token_hint`. The dev realm's `bank-web` client allows post-logout redirects (`redirectUris: ["*"]`, `post.logout.redirect.uris: "+"`), and per the OIDC RP-Initiated Logout spec Keycloak requires `id_token_hint` to validate a supplied `post_logout_redirect_uri` — absent it, HTTP 400. Stashing the id_token in the sign-out `AuthenticationProperties` does **not** help (the handler ignores them). **Fix/prevention:** guard the endpoint — when `GetTokenAsync("id_token")` is empty, `SignOutAsync` the local cookie and `Results.LocalRedirect("/")` instead of driving the OIDC end-session; the authenticated path still carries the hint correctly and is unchanged.

**Evidence:** `src/AuthzEntitlements.Bank.Web/Program.cs` (the `/logout` guard + `options.SaveTokens = true` / `options.SignedOutCallbackPath = "/signout-callback-oidc"`); `infra/keycloak/authz-bank-realm.json` (the `bank-web` client's `redirectUris` `*` / `post.logout.redirect.uris` `+`); the ASP.NET Core aspnetcore `OpenIdConnectHandler.SignOutAsync` source (`message.IdTokenHint = await Context.GetTokenAsync(Options.SignOutScheme, "id_token")`); the CS59 live reproduction (unauthenticated `/logout` → 302 to `…/logout?post_logout_redirect_uri=…` with **no** `id_token_hint` → Keycloak 400 "We are sorry…"; after the fix → local redirect home; the authenticated browser flow emits `id_token_hint` unchanged).

**Disposition:** **open** — fixed in-band by CS59 (`src/AuthzEntitlements.Bank.Web/Program.cs`, the `/logout` guard). Flips `open → applied` at CS59 close-out.

### LRN-089

```yaml
id: LRN-089
date: 2026-07-06
category: architectural
source_cs: CS58
status: applied
tags: [aspire, env, keycloak, jwt, e2e, regression]
claim_area: aspire-run
```

**Problem:** After the CS56 `aspire run` fix, every authenticated request to the internal services still failed: `teller1`/`manager1` saw **no accounts/transactions** (bank-web rendered its "fail-closed" empty state) and break-glass returned **HTTP 500**. CS56/CS57 did not catch it — CS57's e2e asserts only that `bank-web` serves 200, never performing an authenticated read *through* bank-api.

**Finding:** Aspire injects `ASPNETCORE_ENVIRONMENT` **only from a `launchSettings.json` profile**. `bank-web` and `edge-gateway` have one (Development); the five internal *service* projects (`bank-api`, `governance-service`, `entitlements-service`, `audit-service`, `authz-pdp`) do **not**, so under `aspire run` they default to **Production**. `AuthenticationSetup`/`GatewayAuthenticationSetup` set `RequireHttpsMetadata = !environment.IsDevelopment()`, so in Production the JWT-bearer handler **rejects the HTTP dev Keycloak authority** (`http://localhost:8088/realms/authz-bank`) with `InvalidOperationException: The MetadataAddress or Authority must use HTTPS…` — a **500 on every request that reaches the auth middleware** (a present bearer is enough; even the anonymous break-glass endpoint 500s because `UseAuthentication` still authenticates the default scheme). Fix (Decision #1): in `AppHost.cs`, force `ASPNETCORE_ENVIRONMENT=Development` on the five internal services **in run mode only** (`builder.ExecutionContext.IsRunMode`), leaving the security-sensitive `RequireHttpsMetadata` gate untouched and `aspire publish` environment-neutral. **Prevention:** the e2e must exercise an authenticated read (and role-gated write + governance break-glass) *through the gateway with a real token* — a `bank-web` home-page 200 is insufficient. **Corollary (Decision #5):** an authenticated e2e under `Aspire.Hosting.Testing` needs **fixed ports** (`appHost.Configuration["DcpPublisher:RandomizePorts"] = "false"` before `BuildAsync`) so Keycloak binds its declared **8088** and the token issuer + the services' injected authority + the reachable JWKS all agree — otherwise the default port-proxy (LRN-088) puts Keycloak on a dynamic port and every authenticated call 401s.

**Evidence:** `src/AuthzEntitlements.AppHost/AppHost.cs` (the run-mode `ASPNETCORE_ENVIRONMENT=Development` block on `entitlementsService`/`bankApi`/`auditService`/`authzPdp`/`governanceService`); `src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs` (`RequireHttpsMetadata = !environment.IsDevelopment()`) + `src/AuthzEntitlements.Edge.Gateway/Auth/GatewayAuthenticationSetup.cs`; `tests/AuthzEntitlements.E2E.Tests/AuthenticatedFlowE2ETests.cs` (the `teller1`/`manager1` authenticated flow with the fixed-8088 pin); the CS58 live reproduction (services logged `Hosting environment: Production`; forcing Development yielded `GET /api/accounts` 200/3, break-glass 201); LRN-088 (the port-proxy behaviour the fixed-port pin overrides).

**Disposition:** **open** — fixed in-band by CS58 (`AppHost.cs`, guarded by the new authenticated e2e). Flips `open → applied` at CS58 close-out once the `RUN_ASPIRE_E2E=1` gate passes (and fails if the D1 fix is reverted — the regression proof).

### LRN-088

```yaml
id: LRN-088
date: 2026-07-06
category: tooling
source_cs: CS57
status: applied
tags: [aspire, testing, e2e, keycloak, endpoints]
claim_area: e2e-tests
```

**Problem:** The CS57 e2e smoke test (`Aspire.Hosting.Testing` `StartAsync`) failed on the first attempt with connection-refused when hitting the hardcoded `http://localhost:8088` Keycloak authority.

**Finding:** `Aspire.Hosting.Testing`'s `DistributedApplicationTestingBuilder` **proxies fixed host ports to dynamically-allocated host ports** — so a resource pinned to host port 8088 in `AppHost.cs` (Keycloak, for a stable issuer under `aspire run`) is NOT reachable at `localhost:8088` from a test; resolve endpoints via `app.GetEndpoint(name, "http")` / `app.CreateHttpClient(name, "http")` instead of hardcoding. Also, Keycloak (dev mode) stamps the OIDC `issuer` from the **request host**, so under the testing proxy the issuer is the proxied host:port, not the fixed-8088 authority — an e2e should assert the realm-path + http-scheme shape (a coherent http realm issuer), not the exact `aspire run` authority. Both CS56 regressions remain caught: an HTTPS-flip breaks the http discovery/issuer, and a service-port collision fails `WaitForResourceHealthyAsync`.

**Evidence:** `tests/AuthzEntitlements.E2E.Tests/AspireStackSmokeE2ETests.cs` (dynamic endpoint resolution + issuer-shape assertion); the CS57 impl first-attempt failure (hardcoded 8088 → connection refused) fixed by dynamic resolution; contrast with `aspire run`, which binds 8088 directly (CS56).

**Disposition:** **applied** — implemented in-band by CS57 (`tests/AuthzEntitlements.E2E.Tests`, merged PR #197 `c908d27`): the e2e resolves Keycloak's endpoint via `app.GetEndpoint("keycloak","http")` + asserts the issuer's http-scheme/realm-path shape, and ran green. Flipped `open → applied` at CS57 close-out.

### LRN-087

```yaml
id: LRN-087
date: 2026-07-06
category: operational
source_cs: CS56
status: applied
tags: [aspire, keycloak, endpoints, dotnet10, regression]
claim_area: aspire-run
```

**Problem:** The .NET 10 GA + Aspire 13.4.6 lockstep bump (PR #189) silently broke the default `aspire run`: `bank-web` login failed OIDC discovery ("response ended prematurely") and several project resources showed **Finished** / **Failed to start**. Two independent regressions rode the bump and neither was caught by `dotnet build`/`dotnet test` (which never evaluate the running app model).

**Finding:** (1) `Aspire.Hosting.Keycloak` 13.4.6 declares Keycloak's fixed host endpoint as HTTP 8088→container 8080 but, in run mode, subscribes to a `BeforeStart` HTTPS-endpoint update that rewrites that `http` endpoint to `https`/targetPort 8443 whenever a developer certificate is available — so host 8088 bound the container's HTTPS (8443) listener and `http://localhost:8088/...` returned an empty reply. Fix: `.WithoutHttpsCertificate()` records an `HttpsCertificateAnnotation{UseDeveloperCertificate=false}` that gates the flip off, keeping HTTP 8088→8080 and the stable `http://localhost:8088` issuer. (2) Under 13.4.6, an `AddProject` resource with no HTTP endpoint (no `launchSettings.json`, no `WithHttpEndpoint()`) is no longer assigned an endpoint or `ASPNETCORE_URLS`, so the five internal services fell back to Kestrel `:5000`, collided, and left `.GetEndpoint("http")` references unresolved. Fix: declare an explicit `.WithHttpEndpoint()` (dynamic port) on each. Lesson: declare project HTTP endpoints explicitly, and guard both regressions in the AppHost app-model smoke test — critically, assert the anti-flip `HttpsCertificateAnnotation`, because the flip fires only at `BeforeStart` (never during the Docker-free `BuildAsync`), so the endpoint annotation alone does not catch it.

**Evidence:** `src/AuthzEntitlements.AppHost/AppHost.cs` (Keycloak `.WithoutHttpsCertificate()`; `.WithHttpEndpoint()` on bank-api/audit-service/entitlements-service/governance-service/authz-pdp); `tests/AuthzEntitlements.AppHost.Tests/AppHostApplicationModelSmokeTests.cs` (project-http-endpoint + Keycloak 8088→8080 HTTP + anti-flip annotation guards); `docs/observability/aspire-run-500-triage.md` § "CS56"; decompiled `Aspire.Hosting.Keycloak.AddKeycloak` + `Aspire.Hosting.ResourceBuilderExtensions.SubscribeHttpsEndpointsUpdate`/`WithoutHttpsCertificate` (13.4.6-preview.1.26319.6); PR #189 (the .NET 10 GA + Aspire 13.4.6 bump).

**Disposition:** **applied** — both regressions fixed in-band by CS56 (`AppHost.cs`, guarded by the app-model smoke test; content PR #193 merged as `388a653`). The live `aspire run` acceptance gate passed (all 7 project services Running/healthy on unique ports, `http://localhost:8088` OIDC discovery HTTP 200 + `teller1` token round-trip, bank-web 200 — Decision #6). Flipped `open → applied` at CS56 close-out.

### LRN-086

```yaml
id: LRN-086
date: 2026-07-05
category: tooling
source_cs: CS54
status: open
tags: [pdp, grpc, metadata, topaz, adapter, casing]
claim_area: pdp-adapters
```

**Problem:** The Topaz adapter (`TopazCheckService`) attaches gRPC auth metadata with **mixed-case** keys — `metadata.Add("Authorization", ...)` and `metadata.Add("Aserto-Tenant-Id", ...)` — which conflicts with the lowercase-gRPC-metadata-keys convention (LRN-073, codified in CS54 into `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety"). Surfaced by the Copilot R4 review on CS54 PR #187.

**Finding:** gRPC/HTTP2 requires lowercase header keys; whether a mixed-case key is accepted depends on client-side normalization (Grpc.Core's `Metadata` typically lowercases keys on `Add`). In the default lab config Topaz uses anonymous auth (`apiKey` / `tenantId` empty), so the mixed-case headers are never actually sent — the issue is latent until a Topaz API key / tenant is configured. Remediation: verify Topaz relies on client-side normalization, or lowercase the keys for consistency with the convention and to remove the fragility.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/TopazCheckService.cs` (the auth-header interceptor adds `Authorization` + `Aserto-Tenant-Id`); CS54 PR #187 Copilot R4 review; convention: `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety" (Lowercase gRPC metadata / `CallCredentials` keys).

**Disposition:** **open** — surfaced by CS54 (documentation-only) but out of scope to fix there (Topaz code is CS46's deliverable). For the Topaz / CS46 owner to verify or lowercase; `claim_area: pdp-adapters` surfaces it at the next pdp-adapters harvest / before-claim gate.

### LRN-085

```yaml
id: LRN-085
date: 2026-07-05
category: operational
source_cs: CS52
status: open
tags: [hold, claim-gate, coverage, refactor-waves, validation-gate]
claim_area: cs52
```

**Problem:** CS52's remaining work — Wave 0b (the report-only→blocking CI coverage gate) and Waves A/B/C (the ~80-item refactoring catalog + per-area gap-closure tests toward 95% line / 90% branch) — must NOT be continued or claimed until a maintainer explicitly says "go". Wave 0 (coverage measurement infrastructure) is done + merged (PRs #177/#179); everything after it is held at the maintainer's request.

**Finding:** Deliberate forward **claim-gate** backstop (a maintainer hold), not a retrospective finding. `harness harvest` surfaces this `open` / `claim_area: cs52` entry at the weekly scan immediately and at the before-claim gate for any CS52-area claim once ≥14 days stale. Per the bounded-before-claim invariant, a CS52 wave/sub-CS claim PR must not open while this entry is undispositioned. The always-on immediate guards are the active CS file's `## Hold / claim gate` section + the `⏸ Paused` WORKBOARD row (whose not-time-reclaimable note overrides the default 7-day reclaim).

**Evidence:** `project/clickstops/active/active_cs52_product-eval-refactor-and-coverage.md` (`## Hold / claim gate`); `WORKBOARD.md` (CS52 Active Work row = `⏸ Paused`); user directive 2026-07-05 ("mark cs52 as HELD, before any agent picks this up I need to say go").

**Disposition:** **open** — intentional maintainer hold. Lift only when a maintainer explicitly confirms the next CS52 wave is in scope ("go"). On lift, flip `status` + add a `**Disposition:**` recording the confirming maintainer + date, restore the WORKBOARD row to `🟢 Active`, and remove the CS file's ⛔ hold block.

### LRN-078

```yaml
id: LRN-078
date: 2026-07-05
category: tooling
source_cs: CS48
status: applied
tags: [aspire, apphost, ci, test-coverage, resource-naming]
```

**Problem:** The Aspire **AppHost** (`Program.Main` / application-model construction) has **no CI coverage** — `dotnet-ci.yml` only builds + runs unit tests, and no test exercises `Program.Main`. A boot-blocking resource-name collision (the `unleash` and `openfga` **containers** shared a name with the same-named shared-Postgres **databases**; Aspire names are case-insensitive + must be unique) shipped undetected, so `aspire run` crashed on startup.

**Finding:** `dotnet build` + unit tests are insufficient to catch AppHost wiring defects (duplicate resource names, bad `WaitFor` graphs) because the `DistributedApplicationBuilder` is only evaluated when the AppHost actually runs. A minimal test that constructs the AppHost application model and asserts it does not throw would fail CI on such collisions.

**Evidence:** CS48 validation (PR #160); the fix in `src/AuthzEntitlements.AppHost/AppHost.cs` (containers renamed `unleash-server`/`openfga-server`); `docs/validation/local-stack-validation.md` §3 + §6.

**Disposition:** **applied by CS50** (content PR #168, squash-merged 2026-07-05 as `2e27035`). The recommended follow-up shipped: `tests/AuthzEntitlements.AppHost.Tests` constructs the AppHost application model via `Aspire.Hosting.Testing` (`CreateAsync` → `BuildAsync`, Docker-free — never `StartAsync`) and asserts it builds without throwing + case-insensitive resource-name uniqueness, registered in `AuthzEntitlements.sln` so `dotnet test`/CI runs it with no workflow change. Verified both directions: passes on the current AppHost, fails on a reintroduced duplicate (the exact CS48 collision class, `DistributedApplicationException`). The immediate collision remains fixed (CS48).

### LRN-079

```yaml
id: LRN-079
date: 2026-07-05
category: tooling
source_cs: CS50
status: applied
tags: [dotnet, sln, windows, line-endings, text-encoding, cpm]
```

**Problem:** `dotnet sln add <proj>` on Windows rewrites `AuthzEntitlements.sln` with **CRLF line endings + a UTF-8 BOM**, which violates the repo's `.gitattributes` LF mandate and fails the harness `text-encoding` gate (part of `harness lint`).

**Finding:** After every `dotnet sln add` / `dotnet sln remove`, re-normalize the `.sln` to **LF, no BOM** before committing (strip a leading `EF BB BF`, replace `\r\n`→`\n`, `[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))`). Post-normalization the `git diff` shows only the intended project-registration lines. The same gotcha applies to any tool that rewrites tracked text on Windows.

**Evidence:** CS50 (PR #168) — post-`dotnet sln add` the file carried CRLF + BOM; normalizing kept the diff to the 15 added registration lines and `harness lint` passed 23/0. Mirrors the create/edit-tool CRLF behaviour already noted in CONVENTIONS.md (LF, no BOM).

**Disposition:** **Applied by CS53** (content PR #182, squash `ec0bd5c`, 2026-07-05; plan-vs-impl scope fix PR #183, squash `d6549f3`). Codified into the `CONVENTIONS.md` `conventions.project` "Language + build" block: `dotnet sln add` is the observed CRLF+BOM offender, so after any `dotnet sln` edit (`add` — or, conservatively, `remove`) re-normalize `AuthzEntitlements.sln` to LF / no-BOM before committing and confirm the diff is only the intended registration lines.

### LRN-080

```yaml
id: LRN-080
date: 2026-07-05
category: tooling
source_cs: CS50
status: applied
tags: [xunit, dotnet, usings, test-project]
```

**Problem:** New xUnit test source files in this repo do **not** resolve `[Fact]` / `FactAttribute` via global usings — omitting an explicit `using Xunit;` yields `CS0246` (type or namespace not found).

**Finding:** Every test `.cs` must include an explicit `using Xunit;` even though the test projects enable `ImplicitUsings` (that does not bring in the xUnit namespace). Mirror the existing suites (e.g. `tests/AuthzEntitlements.Edge.Gateway.Tests/*.cs`).

**Evidence:** CS50 (PR #168) — `tests/AuthzEntitlements.AppHost.Tests/AppHostApplicationModelSmokeTests.cs` includes `using Xunit;`; all existing test files do the same.

**Disposition:** **Applied by CS53** (content PR #182, squash `ec0bd5c`, 2026-07-05). Codified into the `CONVENTIONS.md` `conventions.project` "Language + build" block: every xUnit test `.cs` must include an explicit `using Xunit;` even with `ImplicitUsings` (which does not import the `Xunit` namespace), else `CS0246`.

### LRN-081

```yaml
id: LRN-081
date: 2026-07-05
category: process
source_cs: CS47
status: applied
tags: [review, fact-verification, vendor-claims, docs, de-scope, oso]
```

**Problem:** CS47's de-scope of Oso inherited a **factual error** from the CS26 feasibility notes — that the Oso Cloud dev-server is "`latest`-only / unpinnable" — and propagated it into ADR 0008 + three eval docs. The independent GPT-5.5 rubber-duck review caught it: the dev-server **is** pinnable (versioned ECR tags e.g. `:v1.2.3`, per Oso's "Pin Dev Server versions in CI" guidance, plus a downloadable native binary).

**Finding:** When a de-scope/adoption decision rests on a vendor's packaging/hosting shape, **verify each load-bearing claim against primary sources** (the ECR/registry tag list, the vendor's own docs, the package registry) — not a single indirect check. The corrected, defensible Oso de-scope rationale is **"no in-process .NET/Polar library (`nuget.org/packages/Oso` → 404) AND no self-hostable *production* server (the dev-server is vendor-scoped to development/testing; production is the paid managed Oso Cloud)"** — NOT "unpinnable/latest-only". An independent reviewer whose model differs from the implementer is the effective catch for inherited-fact errors in prose PRs (REVIEWS.md § 2.6a fact-claim verification).

**Evidence:** CS47 (PR #170) — the rubber-duck (gpt-5.5) round flagged the claim; re-verified 2026-07-05 against `osohq.com/docs/develop/local-dev/oso-dev-server` (pinnable tags + native binary), `nuget.org/packages/Oso` (404), `nuget.org/packages/OsoCloud` (exists); corrected across `docs/adr/0008-oso-descoped-from-expansion-engines.md` + comparison-matrix / market-survey / survey / TCO before merge.

**Disposition:** **applied** by CS47 — the corrected rationale shipped in ADR 0008 + the eval docs, and the re-evaluation trigger keys off the exact corrected constraints. The general fact-verification convention is a candidate for REVIEWS.md consolidation at harvest.

### LRN-082

```yaml
id: LRN-082
date: 2026-07-05
category: process
source_cs: CS47
status: applied
claim_area: cs46
tags: [cross-cs, docs-staleness, oso, consistency, file-ownership]
```

**Problem:** The same inherited "unpinnable `latest`-only" Oso phrasing that CS47 corrected also exists in **`project/clickstops/active/active_cs46_keto-topaz-adapters.md`** (CS46's plan Background), which is an **actively-owned CS (owner yoga-ae)** that CS47 must not edit (file-ownership rule). After CS47 merged, `main` briefly holds ADR 0008 (correct) alongside CS46's plan (stale) — an internal inconsistency CS47 cannot fix in-band.

**Finding:** When a fact-correction spans files owned by another in-flight CS, the correcting CS **surfaces** the finding (learning + note) rather than editing the other CS's files. CS46's owner should correct its Background's Oso reference (the dev-server is pinnable but development-only; no self-hostable production path — see ADR 0008) at CS46 implementation or close-out. `claim_area: cs46` surfaces this at the CS46 harvest / before-claim gate.

**Evidence:** CS47 (PR #170) plan-vs-impl + rubber-duck reviews flagged `active_cs46_keto-topaz-adapters.md` (~line 22, "unpinnable `latest`-only dev-server"); left untouched per file-ownership rules; ADR 0008 is the corrected authority.

**Disposition:** **applied** — CS46 (this CS) reconciled its plan Background's Oso reference to ADR-0008 (Oso's only self-hostable path is a **development-only** dev-server with no production self-hosting path) at close-out.

### LRN-083

```yaml
id: LRN-083
date: 2026-07-05
category: architectural
source_cs: CS46
status: applied
tags: [keto, rebac, adapter, fail-closed, serialization, dotnet]
```

**Problem:** The Ory Keto ReBAC adapter (CS46) seeds the shared `RebacSeedTuples` graph via Keto's write API. Object→object structural tuples (e.g. `customer:acme -> owner -> account:acme-checking`) must be written as Zanzibar **subject_sets** (namespace+object, empty relation), not `subject_id`s. The generated `Ory.Keto.Client 0.11.0-alpha.0` `KetoCreateRelationshipBody.ToJson()` emits `"subject_id": ""` even for a subject-set body.

**Finding:** Verified live against `oryd/keto:v26.2.0`: a `PUT /admin/relation-tuples` whose JSON carries BOTH an (empty) `subject_id` AND a `subject_set` makes Keto **store `subject_id=""` and silently DROP the `subject_set`** — the structural relationship is lost and indirect-path checks (RM / branch / region) fail. Correctness therefore hinges on `subject_id` being **absent** (not empty) whenever a `subject_set` is present. Remediation: build the write body from a hand-rolled record serialized with `System.Text.Json` `JsonIgnoreCondition.WhenWritingNull` (subject-set tuples omit `subject_id` entirely), guarded by an offline test that asserts the **absence** (not emptiness) of `subject_id`. Also: Keto's `PUT /admin/relation-tuples` **appends** (it is NOT an idempotent upsert) — re-seeding a persistent store duplicates tuples; harmless here because the DSN is in-memory and the adapter is a once-per-process DI singleton.

**Evidence:** CS46 Keto PR #172; `src/AuthzEntitlements.Authz.Pdp/Providers/Keto/KetoSeedTupleMapper.cs` (`WhenWritingNull`) + `KetoCheckService.WriteSeedRelationshipsAsync`; `tests/AuthzEntitlements.Authz.Pdp.Tests/KetoSeedTupleMapperTests.cs`; a live query showed the structural tuples stored as clean `subject_set`s (no `subject_id`) with catalog parity 3/3. The GPT-5.5 R1 review caught the fail-open.

**Disposition:** **applied** — remediated in CS46 (PR #172).

### LRN-084

```yaml
id: LRN-084
date: 2026-07-05
category: architectural
source_cs: CS46
status: applied
tags: [topaz, aserto, opa, adapter, offline, docker]
```

**Problem:** The Topaz/Aserto adapter (CS46) drives Topaz as a full-decision engine over its OPA bundle. Topaz normally pulls its policy bundle from an OCI registry (a push/pull the lab must avoid — the plan's escalation gate), and its authorizer + directory span three ports (8282 gRPC / 8383 REST / 9292 directory), so booting it offline and querying a pure-policy decision are non-obvious.

**Finding:** `ghcr.io/aserto-dev/topaz:0.33.14` boots and answers fully **offline** from a **local** OPA bundle: `infra/topaz/config.yaml` sets `opa.local_bundles.paths: /policy` (bind-mounted from `infra/opa/policy`) and REMOVES the stock OCI `services`/`bundles` blocks, so no registry is contacted; anonymous auth is enabled (api-key off). The .NET Aserto authorizer `QueryAsync` (query `x = data.authz.bank.decision`, `input=<AccessRequest>`) requires an explicit `IdentityContext { Type = IDENTITY_TYPE_NONE }` (the authorizer REJECTS an unset/UNKNOWN identity type), and the Rego decision object lands at `response.result[0].bindings.<var>`. Topaz serves the authorizer over TLS with a self-signed dev cert → connect insecure but scope the any-cert acceptance to **loopback** endpoints (non-loopback uses normal CA validation). The whole fintech decision comes from the OPA bundle + request input; Topaz's Zanzibar directory is left empty (documented parity boundary) — the "OPA standalone vs OPA-inside-Topaz" head-to-head.

**Evidence:** CS46 Topaz PR #176; `infra/topaz/config.yaml`; `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Topaz/TopazCheckService.cs`; live full `FintechScenarioCatalog` decision+reason parity 2/2 against a real `ghcr.io/aserto-dev/topaz:0.33.14`.

**Disposition:** **applied** — implemented in CS46 (PR #176).

### LRN-069

```yaml
id: LRN-069
date: 2026-07-04
category: operational
source_cs: CS27
status: open
tags: [hold, claim-gate, cloud-deploy, validation-gate]
claim_area: cs27
```

**Problem:** CS27 (Azure deployment of the app) must not be started before the current stack is validated in detail locally and a maintainer explicitly puts cloud deployment in scope. Cloud/Azure (or any other cloud) deployment is out of scope at this time.

**Finding:** Deliberate forward **claim-gate** backstop, not a retrospective finding. `harness claim CS27` runs `harness harvest --claim-area cs27`; this `open` / `claim_area: cs27` entry surfaces at the **weekly** `harness harvest` immediately and at the **before-claim** gate once ≥14 days stale (harvest v0.16.0 gates `claim_area` matches by `--stale-days`, default 14 — verified empirically). `category: operational` scopes it to CS27's claim only (no noise on unrelated claims). Per the bounded-before-claim invariant a CS27 claim PR must not open while this is undispositioned. The always-on immediate guards are the CS file's `## Hold / claim gate` + CS27's HIGH-RISK registration in `harness.config.json` (`reviews.high_risk_clickstops`).

**Evidence:** `project/clickstops/planned/planned_cs27_azure-app-deploy.md` (`## Hold / claim gate`); `harness.config.json` `reviews.high_risk_clickstops`; user directive 2026-07-04 ("not in scope at this time to deploy to azure ... no deployments until validated locally").

**Disposition:** **open** — intentional hold. Lift only when (1) the current stack is validated locally + documented, (2) a maintainer confirms cloud deploy is in scope, and (3) demo/lab observability is warranted. On lift, flip `status` and record the confirming maintainer + date here.

### LRN-070

```yaml
id: LRN-070
date: 2026-07-04
category: operational
source_cs: CS43
status: open
tags: [hold, claim-gate, observability, validation-gate]
claim_area: cs43
```

**Problem:** CS43 (full OpenMeter metering, local) must not be started before the current stack is validated in detail locally and the demo/lab is confirmed to genuinely need full metering. Although CS43 is local-only, it adds heavy net-new infra (Kafka/ClickHouse/Redis).

**Finding:** Deliberate forward **claim-gate** backstop. `harness claim CS43` runs `harness harvest --claim-area cs43`; this `open` / `claim_area: cs43` entry surfaces at the **weekly** harvest immediately and at the **before-claim** gate once ≥14 days stale (harvest v0.16.0 gates `claim_area` matches by `--stale-days`, default 14). `category: operational` scopes it to CS43's claim only. Per the bounded-before-claim invariant a CS43 claim PR must not open while this is undispositioned. The always-on immediate guards are the CS file's `## Hold / claim gate` + CS43's HIGH-RISK registration in `harness.config.json`.

**Evidence:** `project/clickstops/planned/planned_cs43_full-openmeter-metering-local.md` (`## Hold / claim gate`); `harness.config.json` `reviews.high_risk_clickstops`; user directive 2026-07-04 ("proper detailed validation of the current stack ... until additional details on the observability is warranted for the demo/lab").

**Disposition:** **open** — intentional hold. Lift only when (1) the current stack is validated locally + documented, (2) a maintainer confirms this work is in scope, and (3) the demo/lab observability need is warranted. On lift, flip `status` and record the confirming maintainer + date here.

### LRN-071

```yaml
id: LRN-071
date: 2026-07-04
category: operational
source_cs: CS44
status: open
tags: [hold, claim-gate, cloud-deploy, validation-gate]
claim_area: cs44
```

**Problem:** CS44 (Azure deployment of OpenMeter) must not be started before the current stack + local OpenMeter (CS43) are validated locally and a maintainer explicitly puts cloud deployment in scope. Cloud/Azure deployment is out of scope at this time.

**Finding:** Deliberate forward **claim-gate** backstop. `harness claim CS44` runs `harness harvest --claim-area cs44`; this `open` / `claim_area: cs44` entry surfaces at the **weekly** harvest immediately and at the **before-claim** gate once ≥14 days stale (harvest v0.16.0 gates `claim_area` matches by `--stale-days`, default 14). `category: operational` scopes it to CS44's claim only. Per the bounded-before-claim invariant a CS44 claim PR must not open while this is undispositioned. The always-on immediate guards are the CS file's `## Hold / claim gate` + CS44's HIGH-RISK registration in `harness.config.json`; CS44 also depends on CS27 + CS43 being implemented first.

**Evidence:** `project/clickstops/planned/planned_cs44_openmeter-azure-deploy.md` (`## Hold / claim gate`); `harness.config.json` `reviews.high_risk_clickstops`; user directive 2026-07-04 ("not in scope at this time to deploy to azure or any other cloud ... no deployments until validated locally").

**Disposition:** **open** — intentional hold. Lift only when (1) the current stack + local OpenMeter are validated locally + documented, (2) a maintainer confirms cloud deploy is in scope, and (3) demo/lab observability is warranted. On lift, flip `status` and record the confirming maintainer + date here.

### LRN-072

```yaml
id: LRN-072
date: 2026-07-05
category: tooling
source_cs: CS26
status: applied
tags: [pdp, grpc, http2, cleartext, h2c, adapter, dotnet]
claim_area: pdp-adapters
```

**Problem:** The SpiceDB and Cerbos adapters reach a local dev container over cleartext HTTP/2 (h2c)
gRPC. .NET's `SocketsHttpHandler` refuses HTTP/2-over-cleartext by default, so the LIVE adapter
cannot connect even when the container is running — and the offline suites still pass, so the
failure is invisible until a real container is exercised (a showstopper).

**Finding:** Set `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",
true)` in a STATIC constructor that runs BEFORE any `SocketsHttpHandler`/`GrpcChannel` is created —
the runtime caches the flag on first handler construction, so late-setting is silently inert. Keep
it in the adapter check-service static ctor (load-bearing placement; inert on the default reference
path). Pair it with fail-closed rejection of `https://` endpoints (this path is h2c-only) plus
`Uri.TryCreate` absolute-URI + scheme validation so a misconfigured endpoint fails closed with a
clear message. Verified against the grpc-dotnet docs.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs` (static
ctor sets the switch at line 48; `BuildClients` rejects `https://` via `Uri.TryCreate` + scheme
checks, lines 140-153) + `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosCheckService.cs`
(static ctor at line 40; `BuildClient` https rejection at lines 120-133); PR #134 + PR #139 Review
logs.

**Implications carried forward:**
- Any future cleartext-gRPC adapter (CS46 Keto/Topaz) must set the switch in an early static ctor
  and reject `https://`; a green offline suite does NOT prove the live gRPC path works.

**Disposition:** **applied by CS54** — codified into `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety" (the four out-of-process adapter safety patterns, scoped by transport/role) plus a pointer in the `CONVENTIONS.md` `conventions.project` block. Content PR #187 (squash `a909104`); flipped to `applied` at CS54 close-out.

### LRN-073

```yaml
id: LRN-073
date: 2026-07-05
category: tooling
source_cs: CS26
status: applied
tags: [pdp, grpc, metadata, authorization, spicedb, adapter]
claim_area: pdp-adapters
```

**Problem:** SpiceDB preshared-key auth sends an `authorization` gRPC metadata header. A mis-cased
key (`Authorization`) is rejected by the gRPC stack, so a correctly-configured key still fails to
authenticate.

**Finding:** gRPC metadata / `CallCredentials` keys MUST be lowercase (`authorization`, never
`Authorization`). The Copilot review caught the mis-cased key; add an offline config regression test
that asserts the header casing so a future edit cannot silently re-break live auth.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/SpiceDb/SpiceDbCheckService.cs`
(`CallCredentials.FromInterceptor` → `metadata.Add("authorization", ...)`, line 161); PR #134 Review
log ("lowercased gRPC metadata key (authorization)").

**Implications carried forward:**
- Any gRPC adapter using metadata / `CallCredentials` (CS46 Keto/Topaz) must lowercase every
  metadata key.

**Disposition:** **applied by CS54** — codified into `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety" (the four out-of-process adapter safety patterns, scoped by transport/role) plus a pointer in the `CONVENTIONS.md` `conventions.project` block. Content PR #187 (squash `a909104`); flipped to `applied` at CS54 close-out.

### LRN-074

```yaml
id: LRN-074
date: 2026-07-05
category: architectural
source_cs: CS26
status: applied
tags: [pdp, fail-closed, full-decision, obligations, cerbos, security]
claim_area: pdp-adapters
```

**Problem:** A full-decision out-of-process adapter (Cerbos owns the whole fintech decision in CEL)
can fail OPEN while mapping the engine response back to `AccessDecision`: an unknown/typo
permit-obligation token could be dropped → a permit WITHOUT the maker-checker requirement; a known
action returning no output row could be misclassified as `UnknownAction`; an ambiguous multi-rule
output could silently pick one.

**Finding:** The response mapper must fail CLOSED on every ambiguity — an unknown obligation token →
deny (never drop the obligation); a known-action-with-no-output → `ProviderUnavailable` (not
`UnknownAction`); multiple/ambiguous output rows → deny (never arbitrarily pick one). GPT-5.5 R1
caught two fail-opens (delegation/OBO + malformed obligation) plus one misclassification.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosDecisionProvider.cs`
(`TryMapObligations` returns false → fail closed on an unknown token, line 203; no-output branch →
`FailClosed`/`UnknownActionDeny`, lines 114-124) +
`src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosCheckService.cs` (`ExtractOutputToken`
returns null when `outputs.Count != 1`, so a multi-rule activation fails closed, lines 72-86); PR
#139 "Fail-closed boundaries" + Review log.

**Implications carried forward:**
- When adding any full-decision engine (CS46 Topaz OPA bundle), enumerate every way the engine
  output can be unknown / empty / ambiguous and fail each closed, with explicit tests.

**Disposition:** **applied by CS54** — codified into `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety" (the four out-of-process adapter safety patterns, scoped by transport/role) plus a pointer in the `CONVENTIONS.md` `conventions.project` block. Content PR #187 (squash `a909104`); flipped to `applied` at CS54 close-out.

### LRN-075

```yaml
id: LRN-075
date: 2026-07-05
category: architectural
source_cs: CS26
status: applied
tags: [pdp, obo, delegation, break-glass, fail-open, engine-swap, security, cross-cutting]
claim_area: pdp-adapters
```

**Problem:** OBO (`Subject.Actor`), manager→delegate delegation (`Context.Delegation`), and
break-glass (`Context.BreakGlass`) are CS19/CS21 constraints only `ReferenceDecisionProvider`
enforces. Every non-reference engine (aspnet/casbin via `FintechRuleEvaluator`,
opa/openfga/spicedb/cedar, and cerbos) evaluates only the human subject — so swapping `Pdp:Provider`
to a non-reference engine can PERMIT an OBO/delegation/break-glass call the reference engine DENIES
(fail-OPEN). Surfaced by the CS26 Cerbos review (PR #139).

**Finding:** This is cross-cutting, not Cerbos-only. CS26 patched Cerbos in-adapter (a fail-closed
short-circuit on `Actor`/`Delegation`/`BreakGlass`); the durable fix is a shared fail-closed guard
at `AuthorizationDecisionProviderFactory` keyed on an `ISupportsExtendedAuthorizationContext`
capability, covering the enforced path AND the factory-resolved playground/shadow/what-if paths.
Filed as CS45.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Cerbos/CerbosDecisionProvider.cs`
lines 80-94 (the in-adapter fail-closed guard on `Subject.Actor`/`Context.Delegation`/
`Context.BreakGlass`); PR #139 Review log (R1 fail-OPEN blocker);
`project/clickstops/planned/planned_cs45_delegation-obo-adapter-guard.md`.

**Implications carried forward:**
- Until CS45 lands, every newly-added engine (CS46 Keto/Topaz) MUST carry a fail-closed
  OBO/delegation/break-glass guard; do not ship a non-reference engine without it.

**Disposition:** **Applied by CS45** (content PR #159, squash `fa15868`, 2026-07-05). The durable remedy shipped: a capability marker `ISupportsExtendedAuthorizationContext` + a fail-closed `ExtendedContextGuardProvider` that `AuthorizationDecisionProviderFactory` wraps around every non-capable provider, denying `Subject.Actor`/`Context.Delegation`/`Context.BreakGlass` requests with `ExtendedContextUnsupported` across the enforced + factory-resolved paths; the CS26 Cerbos in-adapter guard was removed in favour of the single authoritative seam. The per-adapter guard is no longer needed for new engines — the factory seam covers every non-capable engine automatically (CS46 Keto/Topaz inherit it).

### LRN-076

```yaml
id: LRN-076
date: 2026-07-05
category: process
source_cs: CS26
status: applied
tags: [pdp, testing, ci, integration, env-gated, parity, full-decision]
claim_area: pdp-adapters
```

**Problem:** A full-decision engine's policy correctness (the Cerbos CEL in
`infra/cerbos/policies/bank.yaml`) is NOT exercised by the offline suite or CI (CI runs no Cerbos
container). A green offline build/test says nothing about whether the CEL policy reproduces the
reference engine's ordered checks, reason codes, and the 10,000 maker-checker threshold.

**Finding:** For any out-of-process full-decision (or ReBAC) adapter, prove the live policy/schema
against a real PINNED container via an env-gated integration test (`CERBOS_TEST_ENDPOINT` /
`SPICEDB_TEST_ENDPOINT`) that soft-skips when the variable is unset — the offline suite and CI stay
Docker-free and green while a documented local run validates the CI-invisible surface. Cerbos
22-scenario `Decision`+reason parity was validated this way both before and after the review fixes.

**Evidence:** `tests/AuthzEntitlements.Authz.Pdp.Tests/CerbosIntegrationTests.cs` (soft-skips unless
`CERBOS_TEST_ENDPOINT` is set) + `tests/AuthzEntitlements.Authz.Pdp.Tests/SpiceDbIntegrationTests.cs`
(soft-skips unless `SPICEDB_TEST_ENDPOINT` is set); PR #139 Testing (22-scenario parity vs a real
`ghcr.io/cerbos/cerbos:0.53.0` container); PR #134.

**Implications carried forward:**
- CS46 Keto/Topaz must carry env-gated integration tests validating the live policy/directory
  against a pinned container; treat a green offline suite as necessary-but-insufficient for
  out-of-process engines.

**Disposition:** **applied by CS54** — codified into `docs/authz/pdp-contract.md` § "Out-of-process engine adapter safety" (the four out-of-process adapter safety patterns, scoped by transport/role) plus a pointer in the `CONVENTIONS.md` `conventions.project` block. Content PR #187 (squash `a909104`); flipped to `applied` at CS54 close-out.

## Applied

### LRN-077

```yaml
id: LRN-077
date: 2026-07-05
category: process
source_cs: CS40
status: applied
tags: [branch-protection, ruleset, merge-gates, bypass, review-evidence, copilot-review, dependabot]
claim_area: ci-merge-gating
```

**Problem:** After branch protection was added, even a fully-green PR could not merge without admin bypass, so 100% of merges routed through admin override — defeating the "not bypassed" goal — despite the ruleset already having required status checks + a `pull_request` rule.

**Finding:** The root cause was the ruleset's **`update` ("Restrict updates") rule** — a bare `{"type":"update"}` rule that lets *only bypass actors* update matching refs, making every non-admin merge to `main` impossible. **Removing `update`** (while keeping the `pull_request` rule + required checks, which already block direct pushes) restored bypass-free merges without permitting direct pushes; rule-suite results flipped `bypass` → `pass`. Two coupled lessons: (1) **never add a bare `update` ("Restrict updates") ruleset rule** if you want PR-based bypass-free merges — it locks the ref to bypass-actors-only. (The `creation` rule is safe to keep: it only restricts *creating* new matching refs, not merges/updates to an existing `main`.) (2) **Enforce Copilot review via the `copilot-review-attached` required *status check*, not the `copilot_code_review` ruleset rule**: Copilot only ever submits *COMMENTED* reviews (never *APPROVED*), so a hard `copilot_code_review` rule deadlocks every PR into bypass. Operating under the resulting required review-evidence gates follows **LRN-068**'s loop, with one refinement: the reliable way to (re-)request Copilot on the current HEAD is the REST call `gh api repos/<o>/<r>/pulls/<n>/requested_reviewers -X POST -f "reviewers[]=copilot-pull-request-reviewer[bot]"` (the GraphQL `gh pr edit --add-reviewer "…[bot]"` can fail with "Could not resolve user").

**Evidence:** CS40 — ruleset `push to main` (id 18513457) required checks = [`build-test`, `structural-gate`, `read-only-gates`, `copilot-review-attached`, `independence-invariant`]; `update` + `copilot_code_review` + `code_coverage` + `code_quality` removed; Admin-only bypass; thread-resolution on. PRs #143 (policy doc), #145 (WORKBOARD), #148 (close-out) all merged **bypass-free** — rule-suite for merge commit `9e1c7b…` = `pass`, not bypass. Policy: `docs/ci/review-pr-hardening.md`.

**Implications carried forward:** This delivers the required-status-check enforcement that deferred **LRN-035** / **LRN-040** were awaiting (`build-test` is now required-to-merge). Durable harness-side gaps hit while enforcing the gates are tracked upstream: **henrik-me/agent-harness#496** (`structural-gate` managed/composed drift on Dependabot GitHub-Actions bumps to managed workflows), **henrik-me/agent-harness#497** (`review-gates` should auto-rerun `copilot-review-attached` via a `pull_request_review` trigger), and corroborated **henrik-me/agent-harness#393** (port `review-gates` bot-author/fork skip-reasons).

**Disposition:** **Applied by CS40** (content PR #143, close-out #148, 2026-07-05). Residual harness fixes tracked upstream (henrik-me/agent-harness#496, henrik-me/agent-harness#497, henrik-me/agent-harness#393); the deferred CI-enforcement learnings LRN-035/040 are now satisfiable and can be flipped `deferred → applied` at the next harvest.

### LRN-003

```yaml
id: LRN-003
date: 2026-07-03
category: architectural
source_cs: CS01
status: applied
tags: [security, nuget, aspire, opentelemetry]
claim_area: security-hardening
```

**Problem:** CS01 ships with known-vulnerable preview packages whose advisories are suppressed to achieve a clean build; this defers real supply-chain risk rather than resolving it.

**Finding:** 15 NuGet audit advisories across 3 packages are suppressed: MessagePack 2.5.192 (2 High + 9 Moderate, transitive via `Aspire.AppHost.Sdk` dashboard/DCP — not directly controllable) and OpenTelemetry 1.14.0 exporter + Api (4 Moderate, direct via the `aspire-servicedefaults` template). They are localhost-only dev-loop packages in CS01 with no untrusted-input path.

**Evidence:** PR #2; `Directory.Build.props` NuGetAuditSuppress list; `done/done_cs01_aspire-foundations.md` Notes.

**Implications carried forward:**
- CS18 (security hardening) must revisit: drop suppression entries as non-vulnerable stable Aspire 13 / OTel packages ship, or pin patched versions via CPM.

**Disposition:** **Applied by CS30** (content PR #95, squash `23e4036`, 2026-07-04): all 15 suppressions dropped — OpenTelemetry bumped to 1.16.0 (Instrumentation.Runtime 1.15.1) and MessagePack pinned to the patched 2.5.302 via CPM transitive pinning; `dotnet list --vulnerable` clean across all 20 projects under `TreatWarningsAsErrors`. See `docs/security/nuget-audit-reeval-2026-07-04.md`.

### LRN-005

```yaml
id: LRN-005
date: 2026-07-03
category: tooling
source_cs: CS02
status: applied
tags: [nuget, cpm, security, efcore, msbuild]
claim_area: security-hardening
```

**Problem:** Under `TreatWarningsAsErrors` + CPM, a clean build broke when EF Core Design 10.0.0-rc.1 dragged in a newly-advisory'd transitive MSBuild package.

**Finding:** `Microsoft.EntityFrameworkCore.Design` 10.0.0-rc.1 pulls `Microsoft.Build.Tasks.Core`/`Microsoft.Build.Utilities.Core` 17.14.8, carrying **CVE-2025-55247 / GHSA-w3q9-fxm7-j8fq** (High; Linux-only, design-time MSBuild temp-dir DoS). Remediate — do NOT suppress — by pinning the patched `17.14.28` on the same minor line via CPM **transitive pinning** (`CentralPackageTransitivePinningEnabled=true` + `PackageVersion` entries). This removes the advisory from `dotnet list package --vulnerable` (a real fix), unlike `NuGetAuditSuppress`.

**Evidence:** PR #5; `Directory.Packages.props` (transitive pins + `CentralPackageTransitivePinningEnabled`); `dotnet list package --vulnerable --include-transitive` (CVE absent post-pin).

**Implications carried forward:**
- CS18 (security hardening): drop the MSBuild pin once EF Core Design ships against patched MSBuild.
- Prefer patched-version CPM transitive pinning over suppression for any new transitive advisory (extends LRN-002/003).

**Disposition:** **Applied by CS30** (content PR #95, squash `23e4036`, 2026-07-04): the re-evaluation ran and **retained** the `Microsoft.Build.*` 17.14.28 pin — EF Core Design 10.0.0-rc.1 still resolves the vulnerable 17.14.8 without it (verified: GHSA-w3q9-fxm7-j8fq High reappears). The drop-trigger (EF Core RC1→GA build referencing patched MSBuild) is now tracked durably in `docs/security/nuget-audit-reeval-2026-07-04.md`.

### LRN-065

```yaml
id: LRN-065
date: 2026-07-05
category: architectural
source_cs: CS21
status: applied
tags: [pdp, break-glass, security, fail-closed, elevation, integrity]
```

**Problem:** CS21 break-glass elevation initially keyed on `EvaluateCore`'s PRIMARY (first) deny reason: a base Deny whose `Reasons[0].Code` was in the elevatable set `{MissingScope, RoleNotAuthorized}` was raised to Permit under an active grant. But the rule evaluators check capability (scope/role) BEFORE integrity (tenant / maker-checker-SoD / subject-is-maker / pending) and short-circuit on the first failure — so a request that lacked capability AND violated an integrity invariant surfaced the elevatable capability reason, MASKING the integrity violation, and was wrongly elevated (bypassing tenant isolation / SoD). GPT-5.5 review R1 caught it.

**Finding:** An elevation/override control that keys on the primary/first denial reason is UNSOUND whenever denials are short-circuited in a capability-before-integrity order. The fix is an INDEPENDENT hard-invariant guard (`PassesHardInvariants`) that re-checks every integrity invariant for the action, so elevation applies only to a PURE missing-capability denial. Reuse the provider's existing integrity predicates (no duplicated rule logic). Test the combined-failure class (missing capability AND integrity violation), not only pure-invariant denials — the "pure invariant" tests pass even when the control is broken.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs` `PassesHardInvariants` (CS21, commit `6ab32bb`); regression tests `BreakGlassTests.BreakGlass_DoesNotElevate_*_Masking*` (5) + 2 positive controls + catalog rows `break-glass-missing-scope-masking-{tenant-mismatch,sod}`.

**Implications carried forward:** Any future override/elevation/emergency/admin-bypass control that raises a denial must RE-VERIFY the hard invariants independently, never trust the primary reason code; and its test suite must include capability+integrity combined-failure cases.

**Disposition:** Applied to CONVENTIONS.md `conventions.project` (fail-closed/security) by the weekly LRN harvest 2026-07-05.

### LRN-066

```yaml
id: LRN-066
date: 2026-07-05
category: architectural
source_cs: CS21
status: applied
tags: [governance, retention, in-memory-store, break-glass, mandatory-review, control-interaction]
```

**Problem:** CS21 added a bounded in-memory retention cap to the break-glass grant store to prevent unbounded growth / DoS. The first version treated any EXPIRED grant as "terminal" and evicted terminal-first. But expired-but-UNREVIEWED break-glass grants ARE the pending-review queue the mandatory-post-review control depends on — so under over-cap writes the cap could silently drop a grant that still owed its mandatory review, making the control unenforceable and losing the audit correlation id. Copilot review caught it.

**Finding:** A retention/eviction policy on a store that ALSO backs a mandatory-follow-up control must rank items still pending that follow-up as LEAST evictable, not "terminal". CS21 uses a 3-tier `EvictionRank`: reviewed (0, evict first) → still-active (1) → expired-but-unreviewed (2, evict LAST). When two controls share the same state, their retention semantics must be reconciled explicitly.

**Evidence:** `src/AuthzEntitlements.Governance.Service/BreakGlass/BreakGlassGrantStore.cs` `EvictionRank` (CS21, commit `cfc1c96`); tests `Issue_OverCap_EvictsReviewedBeforeActive` + `Issue_OverCap_PreservesExpiredUnreviewedOverActive`.

**Implications carried forward:** When adding retention/GC/eviction to any store, enumerate every downstream control that reads that store and confirm eviction never removes an item a control still needs (esp. mandatory-review / audit-completion queues).

**Disposition:** Applied to CONVENTIONS.md `conventions.project` (fail-closed/security) by the weekly LRN harvest 2026-07-05.

### LRN-067

```yaml
id: LRN-067
date: 2026-07-05
category: architectural
source_cs: CS21
status: applied
tags: [pdp, delegation, obo, scopes, least-privilege, security]
```

**Problem:** CS21 manager→delegate delegation initially enforced only the DELEGATE's token scopes (`Actor.Scopes`, via the CS19 OBO intersection) plus an active grant, but NOT the manager's GRANT scopes. So a delegate whose token carried broader `agent.bank.*` scopes could use an active delegation grant for actions the manager never delegated. Copilot review caught it.

**Finding:** Delegation authorization must bound the delegate by the INTERSECTION of the delegate's own capability (token) AND the delegator's granted scopes — the grant is authoritative for what was actually delegated. The PDP `DelegationGrant` must carry the delegated `Scopes` and require the action's required scope to be present in BOTH `Actor.Scopes` and the grant's `Scopes` (fail-closed on either).

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs` `grantAllowsScope` + `Contracts/DelegationGrant.cs` `Scopes` (CS21, commit `01c52b0`); test `DelegationGrantTests` grant-scope-omit case + catalog row `delegation-grant-scope-omits-required-denies`.

**Implications carried forward:** For any act-on-behalf-of / delegation model, enforce the delegator's grant as the least-privilege ceiling, not just the delegate's asserted capability.

**Disposition:** Applied to CONVENTIONS.md `conventions.project` (fail-closed/security) by the weekly LRN harvest 2026-07-05.

### LRN-068

```yaml
id: LRN-068
date: 2026-07-05
category: process
source_cs: CS21
status: applied
tags: [copilot-review, review-evidence, merge-gates, convergence, multi-round]
```

**Problem:** CS21's content PR #120 went through 8 GitHub Copilot review rounds + 11 GPT-5.5 rounds. Copilot did NOT auto-re-review on push, re-flagged already-fixed items on re-review, and each fix push re-staled the `read-only-gates` review-log freshness check (the latest `## Review log` `analyzed_head` must equal the current HEAD) and the `copilot-review-attached` gate (which requires a Copilot review ON the current HEAD).

**Finding:** Converging a multi-fix, security-sensitive PR under the Copilot-review + review-evidence gates requires a disciplined loop per fix push: (1) re-request Copilot (`gh pr edit --add-reviewer copilot-pull-request-reviewer`) for the new HEAD; (2) wait ~5–7 min for Copilot to review the CURRENT HEAD (it is slow and must be on HEAD); (3) get a fresh independent GPT-5.5 diff review of the delta and append a `## Review log` row with `analyzed_head == HEAD`; (4) resolve ALL threads — fix genuine findings, reply-and-resolve false re-flags pointing at the shipped fix; (5) re-run the stale `read-only-gates` / `review-threads-resolved` runs (thread resolution + PR-body edits don't always auto-trigger them). BATCH fixes into fewer commits to minimize rounds. Copilot's findings were genuinely valuable here (a real integrity-masking bypass, a retention-vs-review bug, a CWE-117 log-forging vector), so treat it as a serious reviewer, not noise — but budget significant wall-clock for the round-trip.

**Evidence:** CS21 PR #120 — 8 Copilot rounds + 11 GPT-5.5 review rows in the PR body; `read-only-gates` repeatedly failed on `stale Go verdict (analyzed_head != HEAD)` and `COPILOT_OUTCOME: failure` until the current HEAD carried both a Copilot review and a matching Review-log row.

**Implications carried forward:** Budget wall-clock for a security-sensitive PR's review convergence; keep the `## Review log` strictly in sync with HEAD; prefer larger batched fix commits over many tiny ones; expect Copilot re-flags and resolve them with a shipped-fix reply rather than re-fixing.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` (review-of-record) by the weekly LRN harvest 2026-07-05.

### LRN-057

```yaml
id: LRN-057
date: 2026-07-04
category: architectural
source_cs: CS15
status: applied
tags: [audit, replay, tamper-evident, abac, follow-up]
```

**Problem:** CS15's Audit Explorer "replay" cannot faithfully reproduce a recorded decision: the CS13 tamper-evident audit row stores only `subject id / action / resource type+id / a single tenant / decision / reason / trace`, not the ABAC inputs (`amount`, `maker`, `status`, subject `roles`, context `scopes`, or a distinct resource tenant/branch).

**Finding:** CS15 ships an honest best-effort replay — "open in Playground" pre-filled with the captured fields + the recorded decision for comparison, with a banner naming the uncaptured inputs — rather than a misleading auto-replay. **Faithful 1:1 replay** needs the PDP to persist the full `AccessRequest` snapshot per audit row, which changes the security-critical CS13 store (a new non-hashed column + ingest-contract change) and warrants its own security-reviewed CS.

**Evidence:** CS15 PR #84 — `Playground.razor` replay pre-fill + banner; `Audit.razor` "Replay in Playground" links; `docs/product/authz-playground-and-audit-explorer.md` § "Replay design — a deliberate fidelity trade-off"; plan-vs-impl GO flagged this as the one documented divergence.

**Implications carried forward:**
- A future CS (candidate for the CS30+ queue) can add a per-row request snapshot to Audit.Service for true replay; scope it with a CS13 security review.

**Disposition:** Delivered by **CS36** (audit request-snapshot for faithful decision replay), merged in PR #140 (commit `8fe3911`). The PDP now persists the full `AccessRequest` as a **non-hashed** `RequestSnapshot` column and the Audit Explorer replays a recorded decision **1:1** (roles/scopes/amount/maker/status/resource tenant+branch/OBO actor); the tamper-evident hash chain is byte-identical (`ComputeRowHash`/`AuditPayload` unchanged). Independent security review PASS + GPT-5.5 plan-vs-implementation GO. Entry flipped `open`→`applied` on CS36 close-out.

### LRN-050

```yaml
id: LRN-050
date: 2026-07-04
category: architectural
source_cs: CS22
status: applied
claim_area: compliance
tags: [fail-closed, live-probe, evidence, cancellation, review]
```

**Problem:** CS22's compliance tool live-probes Governance.Service for certification/least-privilege evidence and self-skips when offline. The first implementation collapsed a REACHED-but-erroring service (HTTP 500/401/404) into the same "unreachable → self-skip" path as a transport failure, so a running-but-broken governance service would report `collected=false` (all-clear) with exit 0 — a fail-OPEN evidence gap. A self-report claimed the distinction was handled; the independent rubber-duck caught that it was not.

**Finding:** Any evidence/probe tool that self-skips offline MUST classify three cases distinctly, never two: (a) transport failure / timeout → self-skip (`collected=false`); (b) service REACHED but non-success status OR malformed body → **fail closed** (clear error + non-zero exit); (c) genuine caller cancellation → **propagate** `OperationCanceledException`, not mask it as "offline". Collapsing (b) into (a) hides a broken service behind an all-clear report; collapsing (c) into (a) breaks cancellation. Because an HttpClient timeout and a caller cancellation both surface as `OperationCanceledException`, separate them with `catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)` (timeout → self-skip) and let a genuinely-cancelled caller token propagate.

**Evidence:** CS22 PR #83 — R1 GPT-5.5 review Needs-Fix on `HttpGovernanceClient` mapping non-2xx → `GovernanceUnreachableException` (fail-open); Copilot then caught the caller-cancellation-swallowed variant. Fixed + `HttpGovernanceClientTests` (reached 500/401/404/503 → fail closed; transport + timeout → self-skip; caller-cancel → propagates); Program maps the fail-closed exception → exit 1.

**Implications carried forward:**
- CS15 (playground/audit explorer), CS32 (observability-audit-enrichment), and any future live-probe/evidence tool: implement the three-way classification up front (transport→self-skip; reached-error/malformed→fail-closed; caller-cancel→propagate).

**Disposition:** Applied to CONVENTIONS.md `conventions.project` fail-closed section by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-051

```yaml
id: LRN-051
date: 2026-07-04
category: tooling
source_cs: CS22
status: applied
tags: [grafana, prometheus, otlp, metrics, dashboard, observability]
```

**Problem:** CS22's Grafana compliance dashboard queries custom governance/entitlements/gateway counters that no prior dashboard had exercised, so their exact Prometheus-exposed names could only be INFERRED from the OTLP→Prometheus mangling pattern, not confirmed against a live scrape.

**Finding:** The shipped `pdp-performance.json` confirms the mangling: `pdp.decisions.total` → `pdp_decisions_total`; `pdp.evaluate.duration` (unit ms) → `pdp_evaluate_duration_milliseconds_bucket`. Applying the same rule (dots→underscores, `_total` suffix on counters), the governance counters SHOULD expose as `governance_decisions_total` (labels `type`/`outcome`), `governance_grants_issued_total`, `governance_grants_revoked_total`, `governance_reviews_run_total`; entitlements as `entitlements_decisions_total`; gateway as `gateway_decisions_total` — verified in the C# meter sources but NOT scrape-verified. The flagship SoD panel uses the unambiguous, well-established `pdp_decisions_total{action="governance.access.request",decision="Deny"}`; the non-SoD panels should be scrape-confirmed before being treated as audit-grade.

**Evidence:** CS22 PR #83 — `GovernanceMetrics.cs:22-26`, `EntitlementsMetrics.cs:20-21`, `GatewayMetrics.cs:15` define the instrument names; `pdp-performance.json:48` confirms the mangling pattern. The cs22-dashboard sub-agent flagged the inference explicitly.

**Implications carried forward:**
- CS32 / CS15 / any live-run pass: `aspire run`, exercise governance + entitlements decisions, and confirm the `/metrics` names + label values resolve before relying on the non-SoD compliance panels.

**Disposition:** Applied to CONVENTIONS.md `conventions.project` dev-observability section by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-052

```yaml
id: LRN-052
date: 2026-07-04
category: process
source_cs: CS22
status: applied
tags: [ci, pr-evidence, review-gates, admin-merge, private-free-tier]
```

**Problem:** On CS22's content PR #83, the CI `pr-evidence-lint` / `review-gates` jobs (`read-only-gates`, `review-log-evidence`, `copilot-review-attached`, `independence-invariant`, `review-threads-resolved`, `structural-gate`) repeatedly failed with **0 steps in ~3s** — a runner/job-provisioning flake, not gate-logic output — while an equivalent `harness-pr-check` run at the SAME sha succeeded, and `dotnet-ci/build-test` passed.

**Finding:** When the CI evidence-gate jobs fail with zero steps, the authoritative substitute is the LOCAL aggregator: `harness pr-evidence --base <merge-base> --head <head> --pr-body <file> --repo <slug> --pr <n>` (identical B1 / A3+A4 / A6 / A5+A16 logic as CI `read-only-gates`). If it exits 0 with the current PR state (Copilot attached at HEAD + threads resolved + Review-log Go row at HEAD), the substance is verified. On this branch-protection-unenforceable private-free-tier repo the harness gates are advisory, so the solo-orchestrator content-PR admin-merge doctrine (OPERATIONS.md § Content/release-PR admin-merge) applies: `gh pr merge <pr> --admin --squash`, with a PR comment documenting the environmental failure + the local pass for traceability.

**Evidence:** CS22 PR #83 — `read-only-gates` failed 0 steps/3s across two reruns; local `harness pr-evidence` = 4 passed / 0 failed; `build-test` green; admin-squash-merged (`6220a13`) with a documented merge note.

**Implications carried forward:**
- Future content-PR close-outs on this repo: do not block on the zero-step evidence-gate flake — run local `harness pr-evidence`, confirm `build-test` green, then admin-merge with a documented note.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` CI billing/public-tier & merge triage by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-053

```yaml
id: LRN-053
date: 2026-07-04
category: process
source_cs: CS15
status: applied
tags: [ci, github-actions, billing, copilot-review, repo-visibility, branch-protection]
```

**Problem:** On CS15's content PR #84 every CI job (`build-test`, all `review-gates` jobs, `pr-evidence-lint`) failed in 2–4s and the Copilot reviewer posted only *"The job was not started because recent GitHub Actions payments have failed or your spending limit needs to be increased."* This is the same 0-step signature LRN-052 attributed to a "runner/job-provisioning flake".

**Finding:** The real root cause is a **repo-level GitHub Actions billing / spending-limit block** on the private free-tier repo — jobs never start (`gh api .../jobs/<id>` shows `started_at: null`), so ALL checks fail instantly AND the Copilot code review cannot run (so `copilot-review-attached` can never go green, and the solo-orchestrator admin-merge doctrine's conditions 3–4 are unsatisfiable). The fix is **making the repo public** (`gh repo edit --visibility public --accept-visibility-change-consequences`): public repos get free unlimited Actions, so re-running the workflows (`gh run rerun`) then executes for real. Caveat: going public **auto-activates the branch-protection ruleset** (which requires public/Pro), so content PRs become `BLOCKED` (Copilot COMMENTED ≠ required APPROVED) and need `gh pr merge --admin --squash` (the documented solo path). Also re-evaluate the private-tier constraint record (`harness init`).

**Evidence:** CS15 PR #84 — `dotnet-ci/build-test` job `started_at: null`; Copilot review body verbatim billing message; after `gh repo edit --visibility public`, reran workflows → `build-test` green (1m15s), Copilot posted a real 22/22-file review, all gates green; merge state flipped to `BLOCKED` (protection now enforced) → admin-squash-merged.

**Implications carried forward:**
- Supersedes LRN-052's "flake" attribution when the symptom is 0-step failures on a private free-tier repo: check `started_at`/the Copilot billing message first; if it is the billing block, going public (or fixing Actions billing) is the only real unblock.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` CI billing/public-tier & merge triage by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-054

```yaml
id: LRN-054
date: 2026-07-04
category: tooling
source_cs: CS15
status: applied
tags: [blazor, razor, dependency-injection, cs0542]
```

**Problem:** A Blazor `.razor` page failed to compile with `CS0542: 'Audit': member names cannot be the same as their enclosing type` — the page `Audit.razor` used `@inject IAuditClient Audit`.

**Finding:** A Razor component's generated class is named after the file (`Audit`), so an injected member (or any member) named `Audit` collides with the type name. Name injected members something distinct from the component (e.g. `AuditApi`). Applies to any `Page.razor` whose natural field name equals the file/type name.

**Evidence:** CS15 PR #84 — build error `CS0542 at Audit.razor(4,22)`; fixed by renaming `@inject IAuditClient AuditApi`.

**Implications carried forward:**
- Any new Razor page: don't name an `@inject`/field the same as the component; a domain-suffixed name (`XApi`, `XClient`) avoids the collision.

**Disposition:** Applied to CONVENTIONS.md `conventions.project` Blazor CS0542 bullet (refined, co-cited with LRN-048) by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-055

```yaml
id: LRN-055
date: 2026-07-04
category: tooling
source_cs: CS15
status: applied
tags: [harness-review, copilot-engage, pr-body, model-audit, review-log]
```

**Problem:** Running `harness review <pr> --copilot-only` (to engage/poll Copilot) **rewrote the PR-body `## Model audit` + `## Review log`**: it set `Reviewer agent` to the GitHub actor running it (the orchestrator id, colliding with `Implementer agent` → `read-only-gates` agent-identity failure), duplicated/re-cased the `Implementer models`, and appended a Copilot review row **outside** the `harness:local-*` marker block. A Copilot row also trips `review-log-evidence` ("reviewer model must be gpt-5.5").

**Finding:** After ANY `harness review` invocation, re-verify + re-fix the PR body before relying on the gates: `Reviewer agent` must be a **distinct label** (convention: `rubber-duck`) from `Implementer agent`; the `## Review log` holds only the **gpt-5.5 rubber-duck** rows (no Copilot row — Copilot is tracked by the separate `copilot-review-attached` gate); and the **latest Go row's `analyzed_head` must equal the current PR HEAD** (A4). Keep the whole Model-audit/Review-log block inside the marker comments.

**Evidence:** CS15 PR #84 — post-`harness review` body had `Reviewer agent = yoga-ae-c2` (== Implementer) → `read-only-gates` "agent-identity violation"; a `copilot`-model Review-log row → `review-log-evidence` "reviewer model copilot is not gpt-5.5"; both fixed by rewriting the body via `gh pr edit --body-file` (single here-string, no-BOM), after which all gates passed.

**Implications carried forward:**
- Every content-PR close-out that engages Copilot via `harness review`: treat the body as needing a manual re-fix afterward; don't re-run `harness review` after the final body fix (it re-munges).

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` CI review-evidence gates by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-056

```yaml
id: LRN-056
date: 2026-07-04
category: process
source_cs: CS15
status: applied
tags: [merge, rebase, review-evidence, semantic-conflict, trial-merge]
```

**Problem:** By CS15 merge time, `main` had advanced ~10 commits (CS22 compliance + CS29 governance) touching files CS15 also changed (`AppHost.cs`, `Bank.Web/Program.cs`) and adding a whole `Compliance` project to the `.sln`. GitHub reported `MERGEABLE` (no textual conflict), but the LRN-035 class of **semantic** merge break (two PRs each green vs their own base) was a live risk — and rebasing to re-verify would change the PR HEAD and invalidate the A4 review evidence (`analyzed_head == HEAD`).

**Finding:** Verify the **combined** state without touching the PR branch: create a throwaway local branch from the PR head, `git merge origin/main` into it, then `dotnet restore/build/test` the full solution. Green ⇒ the squash-merge (same 3-way base) produces the same tree, so `gh pr merge --admin --squash` is safe and the PR head/evidence stay intact. Red ⇒ rebase + fix + re-review. This preserves review evidence while clearing the semantic-merge risk.

**Evidence:** CS15 PR #84 — trial-merge branch `cs15-trialmerge`: `merge origin/main` auto-merged `AppHost.cs`/`Bank.Web/Program.cs` cleanly; combined `dotnet test` **1132/1132**, build 0/0; admin-squash-merged as `db058f2` with `main`'s `push` CI subsequently green.

**Implications carried forward:**
- Any content PR whose base moved with overlapping-file CSs: run the local trial-merge build+test before `--admin` merge instead of blind-merging or a head-changing rebase.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` Multi-agent merge hygiene by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-058

```yaml
id: LRN-058
date: 2026-07-04
category: architectural
source_cs: CS19
status: applied
claim_area: security-hardening
tags: [authz, obo, delegation, fail-closed, claims, security]
```

**Problem:** The first CS19 on-behalf-of (OBO) claim helper gated a delegation on `subject_type != "user"` (any non-human), so a typo / unknown / mis-cased `subject_type` (e.g. `robot`, `AGENT`) plus a non-blank `on_behalf_of` silently resolved as a valid delegation.

**Finding:** "Any non-human" is NOT fail-closed for delegation. Resolve an OBO delegation only for an explicit **ordinal allow-list of recognized delegate kinds** matching the PDP `Actor.Type` domain (`{agent, service}`); reject blank/unknown/mis-cased values. Ordinal (case-sensitive) matters — the claim is a fixed lowercase machine token, so `AGENT`/`Service` must not match. The constrained-delegation decision is the intersection (user-permit ∧ agent delegated scope), so an agent can never exceed the user; a base user-Deny passes through unchanged and the `Actor==null` path stays byte-identical.

**Evidence:** PR #85 GPT-5.5 R1 (Needs-Fix) → R2 Go; `ActorClaims.cs`/`GatewayActorClaims.cs` `DelegationCapableTypes = new(StringComparer.Ordinal){agent,service}` in `TryGetDelegation`; `ReferenceDecisionProvider` OBO wrapper; `ActorClaimsTests`/`GatewayActorClaimsTests` (`robot`/`AGENT`/`Service`/blank → false, `service` → true).

**Implications carried forward:**
- CS21 (break-glass/delegation) reuses these helpers + `Subject.Actor` — keep the ordinal known-delegate-kind allow-list; a break-glass grant is an OBO delegation with an elevated, expiring scope set on the same claim contract.

**Disposition:** Applied to CONVENTIONS.md `conventions.project` fail-closed section by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-059

```yaml
id: LRN-059
date: 2026-07-04
category: architectural
source_cs: CS19
status: applied
claim_area: security-hardening
tags: [security, logging, cwe-117, log-forging, audit, codeql]
```

**Problem:** The PDP audit sink wrote request/engine-derived string fields (subject/actor ids + types from the anonymous, caller-controlled `/evaluate` body, and OpenFGA relationship-tuple policy references) into an `ILogger` message template. CodeQL `cs/log-forging` flagged them (CWE-117) once the repo went public.

**Finding:** Untrusted values rendered into a log message can inject newlines to forge fake log lines — **even via structured `ILogger` placeholders**, because the default renderer substitutes the values into the text (CodeQL does not treat structured logging alone as a barrier). Sanitize CR/LF from **every** rendered string in the sink (a `Clean()` helper), including joined collections like `PolicyReferences` (GPT-5.5 R5 caught that these embed caller-derived ids for OpenFGA). The audit-of-record (the hash-chained `PdpDecisionAuditEvent` → Audit.Service) keeps raw values; only the human-readable log string is sanitized. Fix the finding, don't dismiss it — the `code_scanning` ruleset rule blocks merge on open high+ alerts.

**Evidence:** PR #85 CodeQL alerts 10–12 (`cs/log-forging`, medium) fixed on re-scan; GPT-5.5 R5 (Needs-Fix on the `PolicyReferences` path) → R6 Go; `LoggingPdpDecisionAuditSink.Clean` wraps all 16 rendered string args + regression test (newline-laden subject/actor id + policy-reference → no raw CR/LF in the message or structured props).

**Implications carried forward:**
- CS32 (observability + audit enrichment) and any service writing request-derived data to `ILogger`: apply the same CR/LF sanitization to untrusted rendered fields; `BankAuthorizationAuditMiddleware` and other sinks logging request fields likely carry the same latent pattern.

**Disposition:** Repo-wide ILogger CR/LF log-forging (CWE-117) sanitization sweep delivered by CS34 (LogSanitizer in ServiceDefaults across OpenFGA/Edge.Gateway/Bank.Api sinks, PR #113); the durable convention captured in CONVENTIONS.md `conventions.project` by CS37 (weekly harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-060

```yaml
id: LRN-060
date: 2026-07-04
category: process
source_cs: CS19
status: applied
tags: [ci, github, ruleset, codeql, copilot, merge, public-repo]
```

**Problem:** CS19 was implemented under a private free-tier GitHub Actions **billing block** (all PR jobs failed to start; the Copilot review returned only the billing-error stub). Mid-CS the repo was made **public** to restore Actions capacity, which simultaneously activated CodeQL `code_scanning` + a "push to main" **ruleset** (`copilot_code_review` + `code_scanning` + `build-test`/`structural-gate` required checks + `required_review_thread_resolution`).

**Finding:** When a repo goes public mid-CS: (1) re-trigger stale/never-run PR CI by **close/reopen** (fires `pull_request` `reopened` on all workflows, preserves HEAD + the review-log `analyzed_head` anchor — a push would restart the review cycle); (2) **re-engage Copilot** (`harness copilot-engage`) so it delivers a *real* review (the billing-era one was a stub) — it will surface genuine findings on the diff over several rounds; (3) CodeQL will raise real code-scanning findings that must be **fixed** (not dismissed) to satisfy the `code_scanning` ruleset rule; (4) stale/**duplicate check-runs** from reruns can leave a spurious `mergeStateStatus=BLOCKED` even when every ruleset requirement is met — verify required checks green on HEAD + 0 unresolved threads + a Copilot review at HEAD + 0 open high+ CodeQL alerts, then **owner admin-merge** (`gh pr merge --admin`); repo-wide auto-merge is disabled.

**Evidence:** PR #85 — billing-stub Copilot review (18:17Z) → post-public real reviews (19:14/19:27/19:49Z); CodeQL alerts 10–12 fixed; `gh pr merge 85 --squash --admin` after a persistent stale `BLOCKED`; ruleset `18513457` (see the "merge policy" repository memory). Repo-made-public is the **tier-change trigger** for the deferred LRN-035 / LRN-040 (the ruleset now enforces `build-test` + `structural-gate` as required-to-merge — their "needs branch protection" residual is satisfied; re-disposition at the next harvest).

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` CI billing/public-tier & merge triage by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-061

```yaml
id: LRN-061
date: 2026-07-04
category: process
source_cs: CS23
status: applied
tags: [sub-agents, parallel, lint, text-encoding, windows]
```

**Problem:** CS23 fanned out 5–6 parallel background documentation sub-agents sharing one working tree; several reported a red `harness lint` self-check even though their own file was verified LF/no-BOM.

**Finding:** `harness lint`'s text-encoding gate scans the whole cwd/git tree, so one sub-agent's transiently-CRLF (mid-write) file fails the aggregate lint for *every* concurrently-running sub-agent. A per-deliverable "lint exit 0" self-check is therefore unreliable during a parallel doc wave. Mitigate by: (1) the orchestrator normalizes all new files to LF + re-runs `harness lint` at wave end (authoritative gate); and (2) brief parallel sub-agents that a *sibling-owned* file failing the aggregate encoding gate is expected — surface it as an escalation, never edit a file outside your ownership to fix it.

**Evidence:** cs23-survey-rebac and cs23-survey-policy each reported `harness lint` fail citing sibling files (`docs/eval/survey/entitlements-and-flags.md` CRLF; `docs/adr/*.md` CRLF) they did not own; both were LF by the time those siblings finished; the orchestrator's wave-end `harness lint` was 22/0.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` Multi-agent merge hygiene by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-062

```yaml
id: LRN-062
date: 2026-07-04
category: process
source_cs: CS23
status: applied
tags: [adr, conventions, docs]
```

**Problem:** CS23 authored the repo's first ADRs. CONVENTIONS.md fixes ADR structure to "title, date, status, context, decision, consequences" and forbids sections outside that "without updating this file", yet the CS23 plan explicitly required per-engine "when-to-use" guidance in the ADRs.

**Finding:** Resolve the conflict by *extending* the format in the CONVENTIONS.md project-local block, not by trimming the deliverable: project ADRs add `## Alternatives considered` + `## When to use / when not` after `## Consequences`. This satisfies both the plan and the "don't add sections without updating this file" rule. A sub-agent that hits this kind of doc-standard-vs-plan conflict should escalate it (cs23-adr did) rather than silently choosing.

**Evidence:** CONVENTIONS.md project-local block "Architecture Decision Records (ADRs)"; `docs/adr/README.md` "Authoring a new ADR"; the cs23-adr escalation. The convention edit shipped in PR #111.

**Disposition:** The ADR-format extension (## Alternatives considered + ## When to use / when not) shipped in CONVENTIONS.md `conventions.project` ADR block (PR #111); confirmed present by the CS37 harvest (2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-063

```yaml
id: LRN-063
date: 2026-07-04
category: process
source_cs: CS23
status: applied
tags: [copilot, review, docs, merge]
```

**Problem:** A small CS23 follow-up docs PR (#117, two matrix rows) took several review rounds: each push produced a fresh Copilot review that surfaced one more minor wording-consistency nit (canonical enum names, a license cell, "port" vs "native .NET bindings", one cell's phrasing vs another table).

**Finding:** On a docs/prose PR, Copilot review tends to surface successive minor wording-consistency suggestions, and each new commit requires a fresh Copilot review at the new HEAD (A16) plus a fresh local Go (A5) — so "push a one-line fix, re-engage, repeat" can loop. Fix the *substantive/factual* comments, but for genuinely non-blocking "consider aligning" style suggestions on already-accurate text, **resolve the thread with a rationale** (the `review-threads-resolved` gate needs threads resolved, not zero comments) rather than pushing another commit and re-spinning the review+engage cycle.

**Evidence:** PR #117 HEADs 78676d6 → 826dd67 → d998d01, each with a new Copilot COMMENTED review adding one nit; the final non-blocking OPA-phrasing suggestion was resolved-with-rationale (both phrasings accurate) to terminate the loop at a HEAD already carrying a GPT-5.5 Go + Copilot review.

**Disposition:** Applied to REVIEWS.md `reviews.project-gates` Review-of-record & automated reviewers by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-064

```yaml
id: LRN-064
date: 2026-07-04
category: process
source_cs: CS25
status: applied
tags: [docs, eval, tco, pricing, vendor, honesty-caveat]
```

**Problem:** CS25 authored a managed-vs-self-host **TCO** doc covering five commercial authorization SaaS (Auth0/Okta FGA, AuthZed Cloud, Oso Cloud, Permit.io, AVP). Concrete list prices, tier limits, and SLA percentages are volatile and mostly **not first-party**: only AVP published an authoritative per-request number; every other vendor's public price came via third-party aggregators (g2/saasworthy/toolradar), and all enterprise/dedicated tiers are quote-only ("contact sales").

**Finding:** For eval-lab economics docs, author at the **pricing-model + cost-driver** level (what you are metered on — per-MAU / per-request / per-tuple / per-seat / flat / custom — and who carries which ops burden), NOT at the exact-figure level. Anchor every quantitative claim to a dated **Sources** section, add a prominent honesty caveat ("read this for the model, not the number"), and mark each figure indicative. This mirrors the CS24 benchmark doc's "absolute latencies are environment-specific" caveat and keeps the doc durable as prices drift. Separately: repo-grounded claims in such a doc must still be fact-checked against the shipped surface — the one factual error caught pre-merge was an off-by-one Postgres-DB count (five→six incl. the optional `unleash` DB).

**Evidence:** `docs/eval/managed-vs-selfhost-tco.md` (CS25, PR #114) — caveat + Sources (accessed 2026-07-04); AVP ~$5/million cited from the AWS what's-new (2025-06), all other vendor prices caveated/aggregator-sourced. Independent GPT-5.5 rubber-duck R1–R4 verified every repo-backed fact-claim (engine/CS maps, image pins, six Postgres DBs, AVP-AWS-only, azd target).

**Disposition:** Applied to CONVENTIONS.md `conventions.project` Eval-lab economics docs by CS37 (weekly LRN harvest 2026-07-04). Landed on main: PR #135 (commit c2bea79).

### LRN-013

```yaml
id: LRN-013
date: 2026-07-03
category: architectural
source_cs: CS04
status: applied
tags: [audit, yarp, gateway, aspnetcore, dotnet]
claim_area: observability
```

**Problem:** The CS04 edge gateway must emit accurate, audit-ready coarse-authorization *decision* events, but an audit middleware that wraps the YARP proxy cannot infer the edge decision from the HTTP status code alone.

**Finding:** A proxy-wrapping audit middleware sees the FINAL status, which conflates edge decisions with downstream ones: a request the edge *allows and routes* can still be 403'd by the backend's fine-grained authz (→ misreported as an edge deny), and `context.GetEndpoint() != null` still matches ASP.NET's synthetic **405** endpoint (→ an unmatched method audited as a false `allow/routed`). The reliable "was actually forwarded" signal is the **YARP proxy pipeline** itself: set the edge-authorized marker inside `MapReverseProxy(proxyPipeline => { proxyPipeline.Use(...); proxyPipeline.UseSessionAffinity(); proxyPipeline.UseLoadBalancing(); proxyPipeline.UsePassiveHealthChecks(); })`, which runs only for a request that matched a real proxy route, cleared its coarse policy, and is about to be forwarded. Then audit only genuine decisions (routed, or an edge 401/403 short-circuit). Three GPT-5.5 rubber-duck rounds surfaced these three misclassifications in sequence.

**Evidence:** PR #17; GPT-5.5 rubber-duck R1 (downstream-403), R2 (any-request marker), R3 (405 synthetic endpoint) → R4 Go; runtime-verified (`DELETE /api/accounts` → 405 with zero audit events; `POST /api/accounts` coarse.authenticated → allow/routed). `src/AuthzEntitlements.Edge.Gateway/Program.cs`, `Audit/GatewayAuditMiddleware.cs`.

**Implications carried forward:**
- For any proxy/gateway that audits decisions, key the decision off the proxy pipeline (was-forwarded), not the final status; only audit genuine authz decisions. Fine-grained decisions belong to the terminal service (`Bank.Api` `BankAuthorizationAuditMiddleware`; later the PDP in CS05).
- Follow-up (non-blocking): enrich edge-denial events with RouteId/RequiredScope (unset via `IReverseProxyFeature` on short-circuits), and skip auditing non-authz-decision requests (unmatched 404 / method-mismatch 405) uniformly across both gates.

**Disposition:** Applied by **CS32** (PR #112, merged `6f596a2`): edge-denial audit events now carry RouteId/RequiredScope — `GatewayAuditMiddleware` falls back from `IReverseProxyFeature` (unset on a short-circuit 401/403 deny) to the matched endpoint's YARP `RouteModel` metadata; and BOTH gates uniformly skip auditing non-authz requests (unmatched 404 / method-mismatch 405) via `ShouldAudit`. See `docs/authz/audit-enrichment-and-skip-contract.md`.

### LRN-014

```yaml
id: LRN-014
date: 2026-07-03
category: tooling
source_cs: CS04
status: applied
tags: [aspire, otel, dotnet, runtime, windows]
claim_area: observability
```

**Problem:** During CS04 `aspire run` verification, `Bank.Api` returned an empty-body HTTP 500 on **every** request (including `/alive`), which blocked the edge gateway's `WaitFor(bank-api)` so the gateway never started.

**Finding:** `Bank.Api` is unchanged by CS04, Keycloak was reachable, and the edge gateway run **standalone** (without the Aspire-injected OTLP export env) served and enforced correctly — so the 500 is an environmental Aspire/OTLP-export interaction under `dotnet run` of the AppHost, orthogonal to CS04. Verifying the gateway standalone (Keycloak on its fixed port + `Bank.Api` as the YARP destination) is a reliable way to isolate service logic from the Aspire orchestration/OTLP layer.

**Evidence:** this session; `GET http://localhost:5000/alive` → 500 (len 0) under `aspire run`; the same gateway build served 200 + correct 401/403/routed decisions standalone. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- Triage the OTLP exporter / OpenTelemetry-instrumentation vs .NET 10 RC1 interaction (candidate for the CS12 observability stack); until then, isolate service-level runtime verification from the AppHost OTLP wiring when it misbehaves.
- **CS12 update (2026-07-03):** CS12 landed the real OTLP collector (`grafana/otel-lgtm`) and pointed every service's `OTEL_EXPORTER_OTLP_ENDPOINT` at it, but a full `aspire run` reproduction of the empty-body 500 was NOT performed (a parallel `aspire run` may be active; CS12 verified the stack standalone instead). Whether routing OTLP at a ready collector (with `WaitFor(observability)`) resolves the 500 remains open — reproduce on the next clean full `aspire run`.

**Disposition:** Applied by **CS32** (PR #112, merged `6f596a2`): triaged the `aspire run` empty-body 500. An offline probe proved the OTLP exporter is **request-path isolated** (a dead OTLP endpoint leaves `/alive` + `/api/*` at 200) and all 7 exporting services already `WaitFor(observability)`, so no missing-`WaitFor` fix exists — the 500 is a non-reproducible early-RC/environmental interaction. Documented root cause + a clean-machine confirmation runbook in `docs/observability/aspire-run-500-triage.md`; `AppHost.cs` unchanged. Definitive confirmation via a routine full `aspire run` remains an open follow-up.

### LRN-031

```yaml
id: LRN-031
date: 2026-07-04
category: process
source_cs: CS07
status: applied
tags: [openfga, rebac, sdk, csharp, aspire, followups]
```

**Problem:** OpenFGA (out-of-process, async SDK, versioned models) integration surfaced several authoring gotchas plus deferred hardening.

**Finding:** (1) The sync `IAuthorizationDecisionProvider.Evaluate` bridges the async `OpenFga.Sdk` with `GetAwaiter().GetResult()` (sanctioned by the contract) — the pattern any async adapter needs. (2) OpenFGA authorization models are **immutable/versioned**: bootstrap writes the exact embedded model and pins the returned model id (favour correctness over reusing a possibly-stale prior version); a dedicated store per `StoreName` keeps tuple reconciliation O(seed). (3) `Dictionary.KeyCollection` IS `IReadOnlyCollection` on net10 so a `Keys` cast does not throw, but materialize (`.ToArray()`) to avoid a fragile runtime-type-dependent cast. (4) `openfga/openfga` runs as a two-step `migrate` (one-shot) + `run` server on postgres (`OPENFGA_DATASTORE_ENGINE/URI`).

**Evidence:** `OpenFgaRebacService` (lazy client, idempotent bootstrap, model-id pinning, read-diff tuple write); `OpenFgaProvider` sync bridge; `AppHost.cs` migrate+run containers; Copilot rounds (SupportedActions cast, per-boot model-version growth).

**Implications carried forward:**
- Follow-ups (deferred, non-blocking dev-loop hardening): make the OpenFGA authorization-model id configurable/pinned to avoid per-boot model-version growth on a persistent shared store; use a targeted tuple-existence reconciliation instead of read-all (fine for the dedicated tiny-seed store today); adopt `Assert.Skip` for the integration tests when the repo moves to xUnit v3 (currently a soft `return` skip, since 2.9.3 has no dynamic skip and adding `Xunit.SkippableFact` was out of scope).

**Disposition:** Applied by **CS31** (PR #100, merged `66fbc7d`): `OpenFgaOptions.AuthorizationModelId` pin (pin-when-configured, else write-then-pin — avoids per-boot model-version growth) + targeted per-tuple existence reconciliation replacing read-all. See `OpenFgaRebacService`/`OpenFgaModelPinTests`. The xUnit-v3 `Assert.Skip` adoption for the live tests remains a future follow-up (still a soft-return skip).

### LRN-033

```yaml
id: LRN-033
date: 2026-07-04
category: process
source_cs: CS09
status: applied
tags: [pdp, parity, testing, fail-closed, tenant, security]
```

**Problem:** CS09's Cedar adapter passed all 22 `FintechScenarioCatalog` scenarios and full build/test, but GPT-5.5 review (R1 Block) found a FAIL-OPEN tenant-isolation gap the catalog missed: with BOTH tenants null/blank, Cedar mapped them to `""` and `"" == ""` PERMITTED, whereas the reference `TenantMatches` fails closed (`!IsNullOrWhiteSpace(subject) && !IsNullOrWhiteSpace(resource) && equal`). Every catalog row uses non-blank tenants, so tests stayed green over a real vuln.

**Finding:** A shared parity catalog of "realistic" values does NOT exercise fail-closed predicates on degenerate/boundary inputs. For every fail-closed rule (tenant, maker, status, scope), add explicit tests with null/empty/whitespace on EACH side, and assert engine parity against the `ReferenceDecisionProvider` oracle (Decision + `Reasons[0].Code`), not just a hardcoded expectation. The Cedar fix: normalize null/whitespace tenant → `""` AND require both sides non-empty in the forbid (`principal.tenant != "" && resource.tenant != "" && principal.tenant == resource.tenant`).

**Evidence:** `CedarDecisionProvider.NormalizeTenant`; the four tenant forbids in `CedarPolicyModel`; `CedarDecisionProviderTests` 7 blank/null/whitespace-tenant tests asserting equivalence to `ReferenceDecisionProvider`. GPT-5.5 R1 Block → R2 Go-with-amendments.

**Implications carried forward:**
- CS16/CS17/CS20/CS23/CS24 and any adapter/eval CS: the 22-scenario catalog is necessary but NOT sufficient — augment with degenerate-input fail-closed parity tests against the reference oracle. Consider adding blank/whitespace-attribute rows to the shared catalog so every engine is held to them.

**Disposition:** Applied by **CS31** (PR #100, merged `66fbc7d`): added degenerate-input (null/empty/whitespace) fail-closed parity tests asserting equivalence to the `ReferenceDecisionProvider` oracle (Decision + `Reasons[0].Code`) for every in-process engine (reference/aspnet/casbin/cedar); OPA/OpenFGA boundaries are covered at the mapper/pure level and OPA ABAC degenerate parity stays in the Rego suite. See `tests/AuthzEntitlements.Authz.Pdp.Tests/DegenerateInputParityTests.cs`. The shared 22-scenario `FintechScenarioCatalog` is intentionally unchanged.

### LRN-038

```yaml
id: LRN-038
date: 2026-07-04
category: architectural
source_cs: CS16
status: applied
tags: [openfga, rebac, testing, mocking, fail-closed]
```

**Problem:** CS16 needed to assert the OpenFGA adapter's permit/deny `DecisionExplanation` (engine=openfga, DeterminingRule=relationship, the relationship-tuple ref) in the OFFLINE default test suite, but `OpenFgaProvider.Evaluate` reaches the explanation only after a live `Check`, and `OpenFgaRebacService` is a **sealed, non-virtual** concrete class with no seam to force `allowed=true` offline (a blank `ApiUrl` throws in `BuildClient`).

**Finding:** The offline suite can only verify the relationship-tuple reference FORMAT (from the pure `OpenFgaRequestMapper` output) + the fail-closed/boundary explanations; the actual permit/deny Engine/DeterminingRule assertion requires the live-server integration suite (self-skipping) or a runtime smoke. To make ReBAC permit/deny explanations unit-testable offline, `OpenFgaRebacService` would need an extracted interface (e.g. `IOpenFgaCheckClient`) the provider depends on — a small refactor deferred out of CS16 (additive-only) but worth doing when ReBAC is next touched.

**Evidence:** CS16 `cs16-openfga` sub-agent report; `OpenFgaRebacService` is `sealed` with concrete `CheckAsync`; CS16 verified the permit/deny explanation via the runtime `/evaluate` smoke instead.

**Implications carried forward:**
- CS20 (migration/portability), CS24 (perf), or any ReBAC-touching CS: extract an `IOpenFgaCheckClient` seam so ReBAC decisions/explanations are unit-testable without a live OpenFGA server.

**Disposition:** Applied by **CS31** (PR #100, merged `66fbc7d`): extracted `IOpenFgaCheckClient` — the one-member forward-Check seam `OpenFgaProvider` depends on (`OpenFgaRebacService` implements it; DI resolves the same singleton) — so the ReBAC permit/deny `DecisionExplanation` (engine/DeterminingRule/tuple ref) is unit-testable OFFLINE via a `FakeOpenFgaCheckClient`. See `tests/AuthzEntitlements.Authz.Pdp.Tests/OpenFgaProviderSeamTests.cs`.

### LRN-044

```yaml
id: LRN-044
date: 2026-07-04
category: architectural
source_cs: CS20
status: applied
tags: [rebac, openfga, rbac, translation, fail-closed]
```

**Problem:** CS20's RBAC→ReBAC translator had to decide what an RBAC policy can *faithfully* become in ReBAC, and what constraints a mechanically-generated OpenFGA model must satisfy to be valid.

**Finding:** (1) The "roles as usersets" translation faithfully carries ONLY the pure **role→permission** dimension; contextual/ABAC gates (scope, tenant, subject==maker, pending status, the maker-checker threshold, SoD) are structurally outside the RBAC projection and must stay with the ABAC engines (reference/Cedar/OPA) or be modeled as ReBAC contextual/relationship tuples — never smuggled into the flat role→permission matrix (`bank.account.read` is scope-gated, so it is intentionally absent from the permission set). (2) OpenFGA relation identifiers must match `^[a-z][a-z0-9_]{0,62}$` (start with a lowercase letter, `[a-z0-9_]`, max 63); a mechanical translator must **fail closed** — reject any permission that sanitizes to a leading digit/underscore, empty, or >63 chars — rather than emit an invalid model. The **Copilot** review (not the GPT-5.5 rounds) caught the initial sanitizer using the wrong limit (50) and allowing invalid leading chars; verify engine-identifier rules against the engine's own model-syntax docs.

**Evidence:** PR #71 (`a57475e`); `RbacToRebacTranslator.Sanitize` regex `^[a-z][a-z0-9_]{0,62}$` + `MaxRelationLength=63`; the in-process `TranslatedRebacGraph.Check` parity resolver proving translated-ReBAC == RBAC across the full user×permission grid with no live OpenFGA server; OpenFGA authorization-model-syntax identifier rules.

**Implications carried forward:**
- CS26 (expansion engines) / any ReBAC-model work: reuse the roles-as-usersets pattern + the `^[a-z][a-z0-9_]{0,62}$` fail-closed relation rule; keep ABAC/context out of the RBAC projection.
- **Open follow-up:** `RbacPolicy.Create` validates cross-references but not that its `roles`/`permissions` lists are non-empty and distinct (Copilot R3, non-blocking); harden it fail-closed when the migration surface is next touched.

**Disposition:** Applied by **CS29** (PR #89, merged `d25aa77`): `RbacPolicy.Create` now fails closed on empty or duplicate roles/permissions, in addition to the existing dangling-reference checks. See `src/AuthzEntitlements.Authz.Pdp/Migration/RbacPolicy.cs` + `tests/AuthzEntitlements.Authz.Pdp.Tests/RbacPolicyCreateTests.cs`.

### LRN-049

```yaml
id: LRN-049
date: 2026-07-04
category: architectural
source_cs: CS14
status: applied
claim_area: governance
tags: [fail-closed, tenant-scoping, confused-deputy, governance, follow-up]
```

**Problem:** The JIT access-request UI (Bank.Web AccessRequests) consumes the **anonymous, non-tenant-scoped** Governance.Service, which returns ALL requests and does not tenant-scope approve/reject. The first tenant fix only filtered the rendered list; a checker could still submit a known cross-tenant request GUID (confused deputy).

**Finding:** Two fail-closed patterns recurred exactly as LRN-047 predicts (independent review catching what a first pass missed): (1) typed clients must catch `JsonException` on `ReadFromJsonAsync` (not only `HttpRequestException`/timeout) so a malformed 2xx body fails closed instead of throwing a 500; (2) UI-layer authorization must bind an action to the **already-scoped** set (`CanDecide(_pending, id)`), never trust the posted id — filtering only the rendered list is insufficient. The Bank.Web guard is defense-in-depth; the complete fix is **server-side tenant scoping in Governance.Service** (out of CS14 scope).

**Evidence:** CS14 PR #76 — R7 GPT-5.5 review returned Needs-Fix (High) on the cross-tenant decide gap; fixed by `AccessRequestsModel.CanDecide` + tenant-scoped `_pending`; `JsonException` fail-closed added across all four Bank.Web clients with tests.

**Implications carried forward:**
- Follow-up: add server-side tenant scoping (and ideally authenticated principals) to Governance.Service approve/reject/list; file as a planned CS or fold into a governance-hardening CS. Until then the Bank.Web guard is the only tenant boundary for these endpoints.
- CS15 and any UI over an anonymous/un-scoped service: fail closed on malformed 2xx (catch `JsonException`) and bind every mutating action to a server-or-tenant-scoped allow-set, not the raw posted id.

**Disposition:** Applied by **CS29** (PR #89, merged `d25aa77`): server-side tenant scoping added to `Governance.Service`'s access-request endpoints — the caller's tenant is bound to the validated Keycloak token and enforced IN-QUERY, so a cross-tenant get/approve/reject returns 404 (the confused-deputy gap is closed) and the list is tenant-filtered. See `docs/governance/tenant-scoping.md`. Within-tenant approver-`sub` binding and broader principal/access scoping remain documented follow-ups.

### LRN-001

```yaml
id: LRN-001
date: 2026-07-03
category: tooling
source_cs: CS01
status: applied
tags: [aspire, dotnet, scaffolding, windows]
```

**Problem:** Scaffolding the Aspire foundations required generating the AppHost/ServiceDefaults projects and adding the Postgres hosting integration from a non-interactive agent shell.

**Finding:** Aspire CLI 13.1.0 `aspire add <integration>` aborts with exit 5 ("Interactive input is not supported in this environment") even with `--non-interactive --version` — use `dotnet add package Aspire.Hosting.<X> --version <apphost-sdk-band>` instead. The `aspire-apphost` template emits `AppHost.cs` (not `Program.cs`) and a single-SDK csproj (`Sdk="Aspire.AppHost.Sdk/13.1.0"`, no explicit `Aspire.Hosting.AppHost` PackageReference). `dotnet new` on Windows emits CRLF, so authored files must be normalized to LF/no-BOM for the text-encoding gate.

**Evidence:** PR #2 (commit `3919a03`); sub-agent `cs01-aspire-scaffold` report; two failed `aspire add postgres` invocations (exit 5).

**Implications carried forward:**
- CS02+ scaffolding should use `dotnet add package` for Aspire integrations and normalize generated files to LF.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-002

```yaml
id: LRN-002
date: 2026-07-03
category: tooling
source_cs: CS01
status: applied
tags: [nuget, cpm, build, aspire]
```

**Problem:** With `TreatWarningsAsErrors=true` and Central Package Management, a clean `dotnet build` on preview / .NET 10 RC packages was impossible because NuGet audit advisories are promoted to build errors at restore time.

**Finding:** Suppress the SPECIFIC advisory IDs via `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-..." />` items in `Directory.Build.props` — NOT a blanket `NoWarn=NU1902;NU1903`, which would also mask future advisories in later projects. Per-advisory suppression keeps auditing active so any new advisory still fails the build.

**Evidence:** PR #2; `Directory.Build.props`; GPT-5.5 review (sub-agent `cs01-review`) finding #1; `dotnet list package --vulnerable --include-transitive`.

**Implications carried forward:**
- CS02+ adding preview packages will hit the same and can reuse the per-advisory pattern.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-004

```yaml
id: LRN-004
date: 2026-07-03
category: tooling
source_cs: CS02
status: applied
tags: [efcore, npgsql, concurrency, dotnet]
```

**Problem:** CS02 needed Postgres optimistic concurrency on the `Approval` row to close a maker-checker double-decide race, but the standard Npgsql helper was unavailable on the pinned RC stack.

**Finding:** `UseXminAsConcurrencyToken()` was **removed** in `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0-rc.1 (verified absent from the assembly). The replacement is a shadow row-version mapped to the hidden system column: `entity.Property<uint>("xmin").IsRowVersion();`. Npgsql maps it to the Postgres system `xmin` column, so the generated SQL (`dotnet ef migrations script`) has **no physical `xmin` column** even though the migration C# shows `xmin = table.Column<uint>(type:"xid", rowVersion:true)`. A Copilot review flagged the migration C# as creating a physical column — a false positive (empirical `CREATE TABLE "Approvals"` SQL has no such column, and the migration applies cleanly).

**Evidence:** PR #5 (fix commit `9420dc4`); `src/AuthzEntitlements.Bank.Api/Data/BankDbContext.cs:161`; `dotnet ef migrations script` output.

**Implications carried forward:**
- Later EF Core optimistic-concurrency work on Postgres should use `Property<uint>("xmin").IsRowVersion()` and verify the generated SQL (not the migration C#) to confirm the system-column mapping.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-007

```yaml
id: LRN-007
date: 2026-07-03
category: process
source_cs: CS02
status: applied
tags: [review, verification, efcore]
```

**Problem:** CS02 drew findings from two independent reviewers of very different quality; acting on all blindly — or dismissing all blindly — would both have been wrong.

**Finding:** The independent GPT-5.5 review (R1) caught two REAL blocking bugs (in-memory-only double-decide race; caller-controlled, unconstrained tenant/branch). Copilot's 6 comments were ALL false positives, disproven empirically: (a) `.Select(x => x.ToDto()).ToListAsync()` on an EF `IQueryable` does NOT throw — EF Core client-evaluates the **top-level projection** (all list endpoints returned 200 with data); (b) the `xmin` row-version maps to the Postgres system column (no physical column; see LRN-004). Lesson: **verify review comments against the running system** (build/test/curl) before either fixing or dismissing — a confident "will throw at runtime" claim was falsified by one `GET`.

**Evidence:** PR #5 Copilot review (6 comments, all resolved with an evidence comment); live `GET /api/{tenants,branches,accounts,transactions}` → 200; `dotnet ef migrations script` (no physical `xmin` column).

**Implications carried forward:**
- Keep the independent-model rubber-duck as the review-of-record; treat automated-reviewer comments as leads to verify, not directives.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-008

```yaml
id: LRN-008
date: 2026-07-03
category: tooling
source_cs: CS03
status: applied
tags: [keycloak, aspire, realm-import, docker]
```

**Problem:** The `authz-bank` realm import crashed Keycloak on startup (standalone and under Aspire).

**Finding:** Keycloak 26's default-on `organization` feature throws `IllegalArgumentException: Session not bound to a realm` in `setupClientServiceAccountsAndAuthorizationOnImport` when a realm export includes a client with `serviceAccountsEnabled` (here `bank-workload`). Disable it with `KC_FEATURES_DISABLED=organization` (container env / Aspire `.WithEnvironment`). Separately, Aspire's `WithRealmImport` enforces Keycloak's `<realm>-realm.json` filename convention (a bare directory `--import-realm` is lenient), so the file must be `authz-bank-realm.json` (not `authz-realm.json`).

**Evidence:** PR #12; container stack trace (`InfinispanOrganizationProvider.getRealm`); "File name / realm name mismatch" import error; `AppHost.cs`, `infra/keycloak/authz-bank-realm.json`.

**Implications carried forward:**
- Any Keycloak-26 realm import with a service account needs `KC_FEATURES_DISABLED=organization`; name realm files `<realm>-realm.json`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-009

```yaml
id: LRN-009
date: 2026-07-03
category: architectural
source_cs: CS03
status: applied
tags: [keycloak, aspire, oidc, issuer]
```

**Problem:** bank-api rejected valid Keycloak tokens under Aspire with "The issuer ... is invalid".

**Finding:** A dynamic/proxied Aspire Keycloak endpoint makes Keycloak stamp a different `iss` per access path (browser vs in-process service vs direct container port), so JWT issuer validation fails. Fix: pin Keycloak to a fixed host port (`AddKeycloak(name, port)`) and inject ONE explicit `Keycloak:Authority` (`http://localhost:<port>/realms/<realm>`) shared by every service and the browser, so the issuer is identical everywhere.

**Evidence:** PR #12; runtime `WWW-Authenticate: error_description="The issuer 'http://localhost:62370/realms/authz-bank' is invalid"`; `AppHost.cs` fixed-port + explicit authority.

**Implications carried forward:**
- CS04 (edge gateway) and any Aspire+Keycloak OIDC reuse the fixed-port + explicit-stable-authority pattern.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-010

```yaml
id: LRN-010
date: 2026-07-03
category: process
source_cs: CS03
status: applied
tags: [jwt, dotnet, authz, testing]
claim_area: security-hardening
```

**Problem:** `RequireRole` returned 403 for tokens that plainly carried the role, yet the unit tests passed.

**Finding:** `JwtBearerOptions.MapInboundClaims` defaults to **true**, remapping Keycloak's top-level `roles` claim to the legacy `ClaimTypes.Role` URI while `RoleClaimType="roles"`, so `IsInRole`/`RequireRole` looked under "roles" and found nothing — a silent auth failure (also remaps `sub`→nameidentifier). Set `options.MapInboundClaims=false`. Synthetic-principal unit tests build the identity directly (`roleType="roles"`) and BYPASS the JWT handler, so they never catch this — reinforces LRN-007 (verify against the running system) and requires an options-level regression test.

**Evidence:** PR #12; temporary `/debug/whoami` showed the role under `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`; `AuthenticationSetup.cs` (`MapInboundClaims=false`) + `AuthenticationSetupTests`.

**Implications carried forward:**
- Any JwtBearer/OIDC wiring with custom claim names needs `MapInboundClaims=false` (bank-web too); add an options-resolution regression test — synthetic-principal tests are insufficient.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-011

```yaml
id: LRN-011
date: 2026-07-03
category: architectural
source_cs: CS03
status: applied
tags: [authz, security, multi-tenant]
claim_area: security-hardening
```

**Problem:** The first CS03 cut authorized the token but still trusted caller-supplied `MakerId`/`CheckerId`/`TenantId`, and reads weren't tenant-scoped (GPT-5.5 R1/R2 blocking).

**Finding:** An AuthN CS must BIND access to the authenticated token, not caller-supplied fields: transaction maker and approval checker = token `sub`; every read tenant-scoped and every write tenant-checked against the token `tenant` claim, fail-closed on a missing/unknown claim. This is defense-in-depth layered OVER the domain maker-checker/SoD rules (which the token binding must not weaken).

**Evidence:** PR #12 GPT-5.5 R1 (5 blocking) + R2 (1 blocking); `TransactionEndpoints.cs` (sub binding + fail-closed tenant), `Auth/TenantScope.cs`; runtime act-as-other + cross-tenant → 403/404.

**Implications carried forward:**
- CS04 (edge) and CS05 (PDP) inherit the token-bound-identity + fail-closed-tenant contract; the `branch` claim is carried but not yet enforced (later ABAC).

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-012

```yaml
id: LRN-012
date: 2026-07-03
category: tooling
source_cs: CS03
status: applied
tags: [keycloak, oidc, client-scopes, par]
```

**Problem:** Access tokens lacked `sub`/`preferred_username`/`email`, and the OIDC web login failed with "Invalid scopes".

**Finding:** A Keycloak realm export that supplies its own `clientScopes` array does NOT auto-seed the built-in `basic`/`profile`/`email`/`roles` scopes, so (a) `sub` (moved to the `basic` scope in KC 24+) and `preferred_username`/`email` are absent from tokens, and (b) requesting undefined `profile`/`email` scopes fails the PAR authorization with "Invalid scopes". Fix: add `sub`/`preferred_username`/`email` mappers to an applied custom default scope (`bank-claims`), request only defined client scopes, and use a Keycloak-accepted dev redirect (`*`).

**Evidence:** PR #12; token decode (missing `sub`) → `bank-claims` mappers; bank-web `/claims` 500 "Invalid scopes: openid profile email bank.read" → scope reduction; `infra/keycloak/authz-bank-realm.json`.

**Implications carried forward:**
- When hand-authoring a Keycloak realm export, either include the built-in scopes or carry the required OIDC claims via a custom default scope; only request client scopes the realm defines.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-015

```yaml
id: LRN-015
date: 2026-07-03
category: architectural
source_cs: CS10
status: applied
tags: [postgres, ef-core, concurrency, dotnet]
claim_area: entitlements
```

**Problem:** Enforcing a hard capacity cap (seat limits) atomically under concurrent writes in EF Core + Postgres.

**Finding:** A `Serializable`-isolation + bounded-retry loop THRASHES under contention — 12 concurrent seat-assigns against a 5-seat plan produced 10 HTTP 500s (retry exhaustion), and a commit-time serialization failure (40001) makes an explicit `tx.RollbackAsync()` throw "transaction has completed", defeating the retry. Replacing it with a **per-subscription Postgres advisory transaction lock** — `SELECT pg_advisory_xact_lock(hashtextextended(<id>, 0))` issued inside the EF transaction via `db.Database.ExecuteSqlInterpolatedAsync(...)` — serializes the count→capacity-check→insert deterministically (blocks rather than conflict-retries; auto-releases at commit). 12- and 30-way concurrent assigns then give exactly the cap, 0 errors, no over-allocation. Prefer pessimistic advisory locks over `Serializable`+retry for hard capacity caps.

**Evidence:** PR #18; `src/AuthzEntitlements.Entitlements.Service/Endpoints/EntitlementsEndpoints.cs` `AssignSeatAsync`; runtime concurrency test (30 concurrent → 5 assigned / 25 denied / 0 errors, seatsUsed=5).

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-016

```yaml
id: LRN-016
date: 2026-07-03
category: process
source_cs: CS10
status: applied
tags: [aspire, dotnet, sub-agent-dispatch]
```

**Problem:** A sub-agent dispatched to add an Aspire service to `AppHost.cs` was scoped to own only `AppHost.cs`, but could not complete without touching a file outside its declared ownership.

**Finding:** Adding an Aspire service reference requires editing BOTH `AppHost.cs` AND `AppHost.csproj` — the Aspire AppHost SDK source-generates the `Projects.<ProjectName>` type from the `<ProjectReference>` in the `.csproj`, so `AppHost.cs` cannot compile without the csproj reference. A sub-agent briefing that adds a new Aspire service must grant write-ownership of `AppHost.csproj` (and, for a whole new project, the `.sln` + `Directory.Packages.props`) alongside `AppHost.cs`.

**Evidence:** PR #18; sub-agent `cs10-entitlements-service` escalation; `AppHost.cs` uses `Projects.AuthzEntitlements_Entitlements_Service`, generated from the `AppHost.csproj` `ProjectReference`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-017

```yaml
id: LRN-017
date: 2026-07-03
category: architectural
source_cs: CS10
status: applied
tags: [dotnet, http, fail-closed, entitlements]
```

**Problem:** An intra-cluster entitlements-decision endpoint conflated a TRANSIENT store failure with a BUSINESS "deny", and a fail-closed client's sentinel fields were wire-deserializable.

**Finding:** When the quota-consume optimistic-retry loop exhausts, returning a `200 {allowed:false}` deny made the `Bank.Api` enforcer mislabel a transient failure as business "quota exceeded" (429). Return a graceful **503** for transient/infrastructure failure (kept distinct from the `200` allow/deny business contract) so a fail-closed client maps it to its `Unavailable` sentinel → 503, not a business status. Relatedly, client-side result records that carry an `IsUnavailable`/`Reason` sentinel "set only locally" must be annotated `[JsonIgnore]` so a wire payload can never inject them. General pattern: keep transient-failure signalling (5xx) distinct from business decisions (2xx allow/deny), and make local-only sentinel fields non-deserializable.

**Evidence:** PR #18; `src/AuthzEntitlements.Entitlements.Service/Endpoints/EntitlementsEndpoints.cs` `ConsumeQuotaAsync`; `src/AuthzEntitlements.Bank.Api/Entitlements/EntitlementsEnforcer.cs` + `EntitlementsContracts.cs`; Copilot PR review (rounds 1–4).

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-018

```yaml
id: LRN-018
date: 2026-07-03
category: process
source_cs: CS10
status: applied
tags: [git, ci, commit-trailers, windows]
```

**Problem:** Integrating `main` into a long-running CS content branch (to pick up a sibling CS) tripped the commit-trailer gates in two non-obvious ways.

**Finding:** (1) The B1 commit-trailer gate enforces the `Co-authored-by: Copilot` trailer on **every** commit in the PR range, INCLUDING the merge commit — `git merge`'s auto-generated "Merge branch …" message has no trailer, so B1 / `harness lint` fail on content PRs (B1 skips only for `workboard-only`-labelled PRs). Fix: `git commit --amend` the merge commit to add the trailer. (2) After a `git rebase --continue` that resolved a conflict, `.git/COMMIT_EDITMSG` keeps the trailer FOLLOWED BY `# Conflicts:` + rebase comment lines; the local `commit-trailers` linter reads `.git/COMMIT_EDITMSG` and treats only the *trailing* run of `Key: Value` lines as the trailer block, so it reports a false "Missing Co-authored-by" even though the committed message is correct. Fix: `git commit --amend --no-edit` to refresh `COMMIT_EDITMSG`.

**Evidence:** CS10 PR #18 merge commit `d6fb750` failed B1 (missing trailer) → amended to `ce39399`; close-out rebase left `# Conflicts:` in `COMMIT_EDITMSG` → local `commit-trailers` false-fail → `git commit --amend --no-edit` cleared it (`Total: 23 passed / 0 failed`).

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-019

```yaml
id: LRN-019
date: 2026-07-03
category: process
source_cs: CS10
status: applied
tags: [multi-agent, git, dotnet, aspire]
claim_area: orchestration
```

**Problem:** With multiple orchestrators running CSs in parallel, a CS branch that spans several hours reliably hits merge conflicts as sibling CSs land on `main`.

**Finding:** The recurring conflict surface is a small, predictable set of **shared integration files**: `AuthzEntitlements.sln`, `Directory.Packages.props`, `src/AuthzEntitlements.AppHost/AppHost.cs`, and `WORKBOARD.md` — CS10 conflicted with CS04 (all four) and then with the CS05 claim (`WORKBOARD.md`). Resolutions are almost always **additive** (keep both sides). Reusable techniques: for `.sln` conflicts, `git checkout --theirs -- AuthzEntitlements.sln` then `dotnet sln add <your new projects>` is safer than hand-merging Project GUIDs; for `AppHost.cs`, keep both service registrations and reconcile a single `var bankApi = …` when a sibling captured that resource into a variable; for `Directory.Packages.props`, keep both per-CS `<ItemGroup>`s. After resolving, expect a new HEAD → re-attest the latest review Go row against it (stale-diff A4) and re-engage Copilot (A16).

**Evidence:** CS10 PR #18 (merged CS04: `.sln` / `Directory.Packages.props` / `AppHost.cs` / `AppHost.csproj` / `Program.cs`) + close-out PR #21 (`WORKBOARD.md` vs the CS05 claim); `.sln` resolved via `checkout --theirs` + `dotnet sln add`.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-020

```yaml
id: LRN-020
date: 2026-07-03
category: process
source_cs: CS10
status: applied
tags: [review, copilot]
```

**Problem:** Copilot PR review, re-engaged after each fix HEAD, kept re-raising already-fixed comments and surfaced a new nit almost every round — risking an unbounded fix → re-engage loop.

**Finding:** Copilot re-emits its FULL comment set on every re-review (it re-scans the whole diff), so previously-addressed items reappear as fresh threads even when the fix is in place; and each re-engage tends to find 1–2 additional (often cosmetic/edge-case) nits. Copilot `COMMENTED` (not `CHANGES_REQUESTED`) is non-blocking, and the A16 gate only needs a Copilot review on the current HEAD submitted after the latest local Go. Convergence tactic: fix the genuinely-substantive findings, then set a hard stop — do a final Copilot re-engage, RESOLVE all resulting threads (real + re-raised), and merge WITHOUT pushing further commits (each new commit resets the loop and re-triggers async review). Triage each thread as "false-positive re-raise of a fixed item" (resolve) vs "new substantive bug" (fix once, then re-enter the hard stop).

**Evidence:** CS10 PR #18 — 5 Copilot rounds; the quota-500 + audit-casing comments were re-raised ~4× after being fixed; the one genuinely-new substantive finding each round (release-lock, quota-remaining off-by-one, transient-503 mislabel) was fixed and the rest resolved; merged after the final re-engage + full thread resolution.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-021

```yaml
id: LRN-021
date: 2026-07-03
category: architectural
source_cs: CS05
status: applied
tags: [pdp, authz, parity, adapters]
```

**Problem:** The CS05 reference PDP is the parity oracle the CS06–CS09 engine adapters are compared against via the shared scenario catalog. An adapter that faithfully mirrors the Bank.Api rules must agree with the reference provider on every scenario — including cases where *several* checks fail at once and only the *first-failing* reason is reported.

**Finding:** Mirroring the rule *set* is not enough — the rule *order* must match Bank.Api too. CS05 R1 review caught the reference provider checking segregation-of-duties (maker==checker) *before* pending-status, whereas Bank.Api `Approval.Decide` checks `Status != Pending` first. For a request that is both a self-approval AND already-decided, the two disagreed on the reason code (`MakerEqualsChecker` vs `NotPending`). The fix reordered the PDP to pending-before-SoD. CS06–CS09 adapters MUST replicate the reference provider's ordered checks (scope → role → subject-is-maker → tenant → pending → SoD), not just the predicates, or they will fail catalog parity on combined-failure scenarios.

**Evidence:** CS05 PR #24; `ReferenceDecisionProvider.EvaluateApprovalDecision`; `Approval.cs:26-35`; catalog scenario `manager-approve-own-txn-sod` (pending) vs `manager-approve-already-approved`; R1 Block → R2 Go.

**Implications carried forward:**
- CS06–CS09 adapter briefings must require matching the reference provider's *check order*, verified by `ScenarioCatalogRunner` (primary-reason-code parity), not just decision parity.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-022

```yaml
id: LRN-022
date: 2026-07-03
category: tooling
source_cs: CS05
status: applied
tags: [dotnet, opentelemetry, testing, metrics]
```

**Problem:** The PDP telemetry primitives (`ActivitySource`, `Meter`, `Counter<long>`) are process-wide statics. Tests that assert on emitted metrics/spans via `MeterListener`/`ActivityListener` see measurements from every test in the process, so a naive assertion is flaky under xUnit's cross-class parallelism.

**Finding:** Isolate a metric-counter assertion by a discriminator tag the code does NOT transform. The `action` tag is normalized to a bounded vocabulary (known verb or `unknown`) for cardinality safety, so it can no longer serve as a per-test key — filter on a unique **provider** name instead (register a stub `IAuthorizationDecisionProvider` with a `Guid`-based Name and filter measurements where the `provider` tag equals it). The span `pdp.action` tag keeps the *raw* action, so span tests may still isolate by a unique probe action. Combine with a lock-guarded capture list and a delta measurement.

**Evidence:** CS05 PR #24; `PdpDecisionServiceHooksTests.Evaluate_IncrementsDecisionCounter_...` (rewritten to isolate by provider name after the metric-action normalization landed); `CopilotHardeningTests` metric test.

**Implications carried forward:**
- CS06–CS09 adapter tests and any CS that asserts on the shared PDP telemetry must isolate by an untransformed tag (provider) or a raw span tag, never by the normalized metric `action`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-023

```yaml
id: LRN-023
date: 2026-07-03
category: process
source_cs: CS12
status: applied
tags: [orchestration, observability, aspire, integration]
claim_area: observability
```

**Problem:** CS12 (observability) and CS05 (PDP) were developed concurrently. CS12's AppHost wiring fanned OTLP to the collector for the four services that existed on its branch base, but CS05 added a fifth ServiceDefaults service (`authz-pdp`, with its own PDP-decision `ActivitySource`/`Meter`) that merged to `main` in parallel — so after both merged, `authz-pdp` was silently unwired and its telemetry went nowhere.

**Finding:** A cross-cutting deliverable phrased as "all services" (here: "fan ServiceDefaults OTel out to the collector") is a moving target when sibling CSs add new services concurrently. Neither implementer CS catches it — the gap only exists in the *merged* tree. The **plan-vs-implementation close-out review caught it** by grepping the CURRENT `AppHost.cs` for every `AddServiceDefaults()`/`AddProject` consumer instead of trusting the branch-time service set. Fixed in PR #32.

**Evidence:** CS12 PVI review round 1 = NEEDS-FIX (authz-pdp unwired); PR #32 wired it (`OTEL_EXPORTER_OTLP_ENDPOINT` + `WaitFor(observability)`); round 2 = GO. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- For any cross-cutting "all services / all X" deliverable, the close-out plan-vs-impl review MUST enumerate the current set from the merged tree, not the branch-base set — concurrent sibling merges can add members after your branch forks.
- When a CS adds a new ServiceDefaults service (CS06–CS09 adapters, future services), wire it to the observability collector in the same PR.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-024

```yaml
id: LRN-024
date: 2026-07-03
category: architectural
source_cs: CS12
status: applied
tags: [grafana, otel-lgtm, security, aspire, observability]
claim_area: observability
```

**Problem:** Exposing the bundled `grafana/otel-lgtm` Grafana with anonymous access for a frictionless lab. Lowering only the anonymous org role to `Editor` (from `Admin`) is insufficient: the image ships Grafana with the default `admin/admin` account, so anyone reaching the exposed UI could still log in as admin and escalate past the Editor cap.

**Finding:** A complete anonymous-Editor "kiosk" needs the anonymous settings PLUS the default-admin auth paths closed: `GF_AUTH_ANONYMOUS_ENABLED=true` + `GF_AUTH_ANONYMOUS_ORG_ROLE=Editor` + **`GF_AUTH_DISABLE_LOGIN_FORM=true`** (no UI login) + **`GF_AUTH_BASIC_ENABLED=false`** (no HTTP Basic Auth). Disabling the login form ALONE still leaves Basic Auth (`curl -u admin:admin`) open. Separately, model the OTLP ingest ports (4317/4318) as `tcp` (not `http`) endpoints so `WithExternalHttpEndpoints()` marks ONLY the Grafana UI external, keeping ingest off-box; build the `http://host:port` exporter URL explicitly via `ReferenceExpression`. Datasource/dashboard provisioning is file-based at image startup and is unaffected by disabling interactive auth.

**Evidence:** CS12 Copilot + GPT-5.5 review rounds; verified on `grafana/otel-lgtm:0.28.0`: `disableLoginForm=true`, `admin/admin` Basic Auth no longer authenticates as admin, anonymous `/api/org` works, Prometheus/Loki/Tempo datasources + both dashboards still provision. `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Implications carried forward:**
- Any future externally-exposed dev UI backed by an image with a default admin account (Grafana, etc.) must disable BOTH the login form AND Basic Auth for an anonymous-only posture — anonymous role alone is not a boundary.
- Model non-UI container ingress ports as `tcp` so `WithExternalHttpEndpoints()` does not inadvertently expose them.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-025

```yaml
id: LRN-025
date: 2026-07-03
category: process
source_cs: CS06
status: applied
tags: [multi-agent, claim, rebase, workboard-auto-approve, ci]
```

**Problem:** During CS06's workboard claim PR, a *different* orchestrator closed out a dependency CS (CS05: content merge + active→done) on `main` while the claim branch was being prepared, advancing `main` past the claim branch's base.

**Finding:** The `workboard-auto-approve` `validate-and-approve` job compares the PR's **2-dot** `git diff base_sha head_sha` file count against the GitHub API's **3-dot** `changed_files` count and **fails closed** when they diverge ("immutable git diff returned N files but PR reports M changed files"). When a branch falls behind `main`, the 2-dot diff picks up the other agent's merged changes and the counts mismatch. Fix: **rebase the branch onto latest `origin/main`** before the gate runs; the two counts then agree. This is a routine hazard for a fleet of parallel orchestrators — not specific to claim PRs.

**Evidence:** CS06 claim PR #30 `validate-and-approve` failed with a 6-vs-2 changed-files mismatch after CS05 close-out (#29) landed; `git rebase origin/main` (onto cbebaf1) made CI green and it squash-merged. `.github/workflows/workboard-auto-approve.yml`.

**Implications carried forward:**
- In a multi-orchestrator repo, rebase any branch onto latest `origin/main` before push/merge if `main` advanced since branching — especially when a dependency CS may have closed out concurrently.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-026

```yaml
id: LRN-026
date: 2026-07-03
category: architectural
source_cs: CS06
status: applied
tags: [pdp, adapters, rbac, casbin, aspnet, parity]
claim_area: engines
```

**Problem:** The CS05 `ScenarioCatalogRunner` passes a provider ONLY when it returns the exact `Decision` AND primary reason code (`Reasons[0].Code`) in the reference's per-action ordering — but the fintech rules are mostly ABAC (tenant, subject-is-maker, pending, SoD, threshold), while CS06's engines (ASP.NET Core policies, Casbin) are RBAC baselines.

**Finding:** Factor the adapter so a shared `FintechRuleEvaluator` owns the engine-agnostic part (per-action ordering + ABAC + obligations, in lock-step parity with the reference) and delegates ONLY role eligibility to the engine via `IEngineRoleAuthorizer.IsRoleAuthorized(action, roles)`. Each adapter is then thin (`Evaluate => FintechRuleEvaluator.Evaluate(request, this)`) and encodes its eligible-role SETS in its engine's native policy form (ASP.NET `RolesAuthorizationRequirement`; Casbin `(role, action)` policy pairs). Catalog parity is guaranteed by the shared evaluator while the engine genuinely owns the RBAC decision — realizing "same question, swappable engine" and honoring the CS06 plan-review amendment by handling ALL 22 cases rather than unsupported-denying non-RBAC ones.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/{FintechRuleEvaluator,IEngineRoleAuthorizer}.cs` + `AspNetCore/AspNetCorePolicyProvider.cs` + `Casbin/CasbinDecisionProvider.cs`; both adapters pass all 22 `FintechScenarioCatalog` scenarios (PR #34; PDP tests 235/235).

**Implications carried forward:**
- CS07–CS09 adapter authors: reuse the `IEngineRoleAuthorizer` seam for RBAC-only engines. For richer engines — OpenFGA (ReBAC, CS07), OPA/Rego (CS08), Cedar (CS09) — weigh how much of the fintech decision the engine should own *natively* vs. compose via the shared evaluator; don't force the shared-evaluator split where the engine can express the full decision.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-027

```yaml
id: LRN-027
date: 2026-07-04
category: architectural
source_cs: CS08
status: applied
tags: [pdp, adapters, opa, http, resilience, fail-closed]
claim_area: engines
```

**Problem:** An out-of-process PDP adapter must satisfy the synchronous `IAuthorizationDecisionProvider.Evaluate` while calling an HTTP engine. ServiceDefaults adds `AddStandardResilienceHandler()` to ALL `IHttpClientFactory` clients (`ConfigureHttpClientDefaults`), raising the concern that a synchronous `HttpClient.Send` would throw through the async-only resilience pipeline.

**Finding:** On .NET 10, synchronous `HttpClient.Send` works through the standard resilience handler for a named client — verified end-to-end (adapter → live OPA → 22/22), so a sync-over-`Send` out-of-process adapter is viable without `.GetAwaiter().GetResult()`. Pair it with three fail-closed disciplines the mocked unit tests can't exercise but a real anonymous `/evaluate` endpoint needs: (1) a backstop `catch (Exception)` so config/construction throws (bad `Opa:BaseUrl`/timeout surfaced inside `CreateClient`) Deny rather than 500; (2) a STABLE, non-sensitive `Reason.Message` (log the detail) since the message is returned to anonymous callers; (3) validate the engine's returned reason code against the bounded `ReasonCodes` vocabulary (fail closed on unknown) so an out-of-process engine can't leak internal detail or inflate `pdp.reason` metric cardinality.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/Opa/OpaDecisionProvider.cs`; `src/AuthzEntitlements.ServiceDefaults/Extensions.cs` (`AddStandardResilienceHandler`); live `POST /api/authz/scenarios/verify` 22/22 with `Pdp:Provider=opa`; Copilot PR #38 flagged (1) message info-leak and (2) untrusted reason-code.

**Implications carried forward:**
- CS07/CS09 out-of-process adapters (OpenFGA, Cedar): sync `HttpClient.Send` via a named client is fine; always fail closed on ANY exception, sanitize caller-facing messages, and validate the engine's decision/reason against `ReasonCodes` before mapping to `AccessDecision`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-028

```yaml
id: LRN-028
date: 2026-07-04
category: process
source_cs: CS08
status: applied
tags: [multi-agent, adapters, tests, merge-order, pdp, ci]
claim_area: engines
```

**Problem:** CS06 and CS08 each merged green in isolation, but after both landed on `main` a CS06 test (`AdapterProviderSelectionTests.AddPdp_RegistersReferenceAndBothAdapters`) failed: it asserted the EXACT registered provider set `[aspnet, casbin, reference]` and CS08's new `opa` provider made it `[aspnet, casbin, opa, reference]`. Each PR's CI was green against its own base, so nothing caught it pre-merge.

**Finding:** Exhaustive-set assertions over a registry that multiple parallel CSs extend create cross-CS coupling CI cannot catch. Assert MEMBERSHIP of the CS's own additions (`Assert.Contains`) plus a uniqueness check that matches the production invariant — the factory de-dupes case-insensitively, so assert `names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length` — never an exact-equal of the whole set. Fixed in #40.

**Evidence:** `tests/AuthzEntitlements.Authz.Pdp.Tests/AdapterProviderSelectionTests.cs`; `main` went 263/1 PDP after #38 merged atop CS06; PR #40 relaxed to membership + case-insensitive uniqueness (Copilot flagged the case-sensitivity mismatch).

**Implications carried forward:**
- CS09 (Cedar) and any future registry-extending CS: assert your OWN additions are present, never the exhaustive set, so the next parallel adapter doesn't red `main`.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-029

```yaml
id: LRN-029
date: 2026-07-04
category: process
source_cs: CS08
status: applied
tags: [opa, rego, csharp, tooling, tests, windows]
```

**Problem:** Two authoring gotchas surfaced during CS08 implementation.

**Finding:** (1) `opa fmt` reformats freshly-authored `.rego` (tabs/spacing) even when `opa check` is clean — run `opa fmt -w infra/opa/policy` BEFORE any `opa fmt --list` gate to avoid a false diff. (2) C# raw *interpolated* string literals with trailing braces (`$$"""{"k":"{{x}}"}}"""`) fail to compile (CS9007) because the closing `}}` is parsed as an interpolation close — use plain string concatenation (or `{{{{` escaping) for JSON-with-braces xUnit fixtures.

**Evidence:** cs08-impl-policy + cs08-impl-adapter sub-agent reports; `OpaDecisionProviderTests.cs` uses concatenation for the parameterized deny-reason JSON fixture.

**Implications carried forward:**
- CS09 (Cedar policy + tests): run the formatter's write mode before the check gate; avoid raw interpolated strings for brace-heavy test fixtures.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-030

```yaml
id: LRN-030
date: 2026-07-04
category: architectural
source_cs: CS07
status: applied
tags: [authz, pdp, adapter, fail-closed, security, openfga]
```

**Problem:** CS07's `OpenFgaProvider` built and passed all tests, but review (GPT-5.5 R16 + Copilot rd.12/13) found it FAIL-OPEN: `Evaluate` threw on a not-configured/unreachable engine, and since `PdpDecisionService` wraps providers in no try/catch, `/api/authz/evaluate` returned a raw 500 instead of a Deny. `/api/authz/rebac/verify` likewise only caught around `EnsureBootstrappedAsync`, not the (singleton, bootstrapped-once) scenario `Check` loop — so a *later* call could still 500.

**Finding:** An out-of-process authz adapter must FAIL CLOSED on ANY engine failure: catch (never throw), return a Deny with a provider-local reason code and a **stable, non-sensitive** message (log the cause; never surface network/config detail to anonymous callers), and wrap the WHOLE request flow, not just the first call. Mirror the established sibling `OpaDecisionProvider` (provider-local `ProviderUnavailable`/`EngineUnavailable`, sanitized message, backstop `catch (Exception)`). Build+tests do NOT catch fail-open — only review does; add a fail-closed unit test (blank ApiUrl → Deny, not throw) to lock it in.

**Evidence:** `OpenFgaProvider.Evaluate` try/catch → `Deny(EngineUnavailable)`; `RebacEndpoints` `UnavailableProblem` stable messages + whole-flow `/verify` wrap; `OpenFgaRegistrationTests.Evaluate_FailsClosed_WhenEngineUnavailable`. Mirrors LRN-027 (OPA fail-closed).

**Implications carried forward:**
- CS09 (Cedar) and any future out-of-process adapter: fail closed on every engine-error path (Deny + stable message + logged cause), add a fail-closed test, and wrap the entire endpoint flow — the singleton-bootstrap gotcha makes "the first call fails" reasoning wrong.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-032

```yaml
id: LRN-032
date: 2026-07-04
category: architectural
source_cs: CS09
status: applied
tags: [pdp, adapter, cedar, monocloud, dotnet10, parity]
```

**Problem:** CS09 integrates Cedar (in-process, `MonoCloud.Cedar` native bindings) as a fifth engine that must answer the shared 22-scenario `FintechScenarioCatalog` with the SAME `Decision` AND primary reason code as the reference — but Cedar is a declarative permit/forbid engine with no ordered "first-failing reason", and `PolicySet.ParsePolicies` assigns its own sequential `policyN` ids (ignoring `@id`), so a determining-policy set can't be mapped back to a reason code from raw-text policies.

**Finding:** (1) Build the `PolicySet` from explicit `Policy(source, id)` objects (not `ParsePolicies`) so `AuthorizationSuccessResponse.GetReason()` returns STABLE, semantic ids the adapter maps to `ReasonCodes`. (2) Model each action as a broad `permit` + one annotated `forbid` per deny reason; on Deny, map the determining-forbid set to the reference's FIRST-failing reason by selecting the LOWEST `Precedence` value (per-action order) — reproducing the reference's short-circuit ordering (LRN-021) for ANY input, not just isolated-failure catalog rows. (3) Per LRN-026, let Cedar own the FULL decision natively (like OPA/Rego), not the role-gate-only `IEngineRoleAuthorizer` split — Cedar is expressive enough; the head-to-head with OPA is that both answer the same catalog. (4) `MonoCloud.Cedar` 0.1.0 restores/builds 0/0 and loads its win-x64 native binary under the .NET 10 RC runtime with no extra setup. (5) Obligations, the unknown-action guard, and fail-closed (any Cedar error → provider-local `ProviderUnavailable` Deny, never throw/permit; pass the exception object to the logger for stack traces) are adapter-side, mirroring `OpaDecisionProvider`.

**Evidence:** `Providers/Adapters/Cedar/{CedarPolicyModel,CedarDecisionProvider}.cs`; `CedarDecisionProviderTests` (22/22 catalog parity + per-scenario + obligations + combined-failure ordering + fail-closed + selection); `Directory.Packages.props` MonoCloud.Cedar 0.1.0 pin; `docs/authz/cedar-adapter.md` (+ Amazon Verified Permissions as the managed/cloud option). Full-solution build 0/0; PDP `dotnet test` 358/358.

**Implications carried forward:**
- Future declarative-policy adapters (and CS16 explainability / CS20 migration): to recover an ORDERED reason from an unordered engine, encode failures as annotated forbids with stable ids + an explicit precedence map, and select the first-failing (lowest precedence) determining member.
- CS23/CS24 (comparison/perf): Cedar (in-process, `cedar`) and AVP (managed) are the Cedar data points; AVP runs the same policies managed (documented, not wired).

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-034

```yaml
id: LRN-034
date: 2026-07-04
category: architectural
source_cs: CS17
status: applied
tags: [pdp, authzen, fail-closed, validation, wire-boundary, security]
```

**Problem:** CS17's new AuthZEN Access Evaluation endpoint (`POST /api/authz/authzen/evaluation`) is a real, audited decision surface over UNTRUSTED external wire input, but it initially reused `AuthZenMapper`'s lenient safe defaults. GPT-5.5 review (R1 Needs-Fix) found a FAIL-OPEN: a present-but-unparseable `amount` coerced to null → `ReferenceDecisionProvider` treats null amount as $0 → a large transfer could return Permit + `post_immediately` (threshold bypass); and an omitted `maker_id` on approve/reject makes `SubjectIsMaker` false → segregation-of-duties passes (self-approval bypass). All 22 catalog scenarios use well-formed attributes, so tests stayed green over the gap — the LRN-033 pattern recurring at a new boundary.

**Finding:** A lenient internal mapper is safe for a TYPED in-process caller (`/evaluate` takes a built `AccessRequest`) but becomes fail-OPEN when reused at a new UNTRUSTED wire boundary. Add boundary-specific, action-aware fail-closed validation BEFORE evaluation: reject a present-but-unparseable numeric field for any action; require the attributes each action's rules key on (`bank.transaction.create` → parseable `amount` + non-blank `maker_id`; approve/reject → non-blank `maker_id` + `status`). Do NOT tighten the shared reference provider (cross-CS scope); harden at the new boundary and add a test that EVERY existing catalog scenario still validates (guard against over-tightening).

**Evidence:** `AuthZenRequestValidation.Validate` (fail-closed shape + attribute checks); `AuthZenEndpoints` calls it (400) before `PdpDecisionService.Evaluate`; `AuthZenConformanceTests` (+9: unparseable/missing amount, missing maker/status on create + approve/reject, `Validate_EveryCatalogScenario_PassesValidation`). GPT-5.5 R1 Needs-Fix → R2 Go.

**Implications carried forward:**
- Any future CS adding a new external decision/enforcement endpoint (CS14/CS15/CS19/CS21): treat the wire boundary as untrusted and add action-aware fail-closed input validation; a passing shared catalog does not prove the boundary is safe.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-036

```yaml
id: LRN-036
date: 2026-07-04
category: tooling
source_cs: CS16
status: applied
tags: [dotnet, line-endings, lint, windows, ci]
```

**Problem:** The dotnet dispatch profile's `dotnet format --verify-no-changes` self-check is incompatible with this repo's enforced LF convention (`.gitattributes` `* text=auto eol=lf`). With no `.editorconfig` `end_of_line`, `dotnet format` defaults to CRLF and flags LF files that have comments inside argument/parameter lists — a whole-solution run flags 6+ PRE-EXISTING untouched files (AppHost.cs, AuthorizationSetup.cs, BankSeeder.cs, ...), so the gate already fails on `main`. Separately, the file-authoring tool writes **CRLF** for new `.cs` files on Windows, and the `harness lint` **text-encoding gate did NOT flag** those CRLF `.cs` files (lint passed green) — the CRLF only surfaced as a `git add` "CRLF will be replaced by LF" warning.

**Finding:** `harness lint` (text-encoding) + `.gitattributes eol=lf` are the AUTHORITATIVE line-ending gates, NOT `dotnet format`. Do not convert files to CRLF to satisfy `dotnet format`. For authored/new `.cs` (or any) files, explicitly convert working-copy CRLF→LF (`[IO.File]::WriteAllText($p, ($t -replace "\r\n","\n"), (New-Object Text.UTF8Encoding $false))`) before committing — do not rely on the text-encoding lint to catch `.cs` CRLF; git's eol=lf will normalize the committed blob, but a clean working copy avoids the add-time warning and reviewer confusion. Treat the dotnet-profile `dotnet format --verify-no-changes` self-check as advisory for this repo until a repo `.editorconfig` with `end_of_line = lf` is added (or the check is dropped from the profile).

**Evidence:** CS16 foundation sub-agent report (dotnet format flags 8 files, 6 pre-existing untouched); this session's `git add` emitted "CRLF will be replaced by LF" for 3 foundation-created `.cs` files while `harness lint` had reported 22/0 with those files present. Repo enforces LF via `.gitattributes:2`.

**Implications carried forward:**
- Any .NET CS with new source files (CS17+/CS20/CS24): convert authored files to LF explicitly; trust `harness lint` + `.gitattributes`, not `dotnet format`, for line endings. Consider a dedicated CS to add `.editorconfig end_of_line = lf` so `dotnet format` aligns with the repo mandate.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-037

```yaml
id: LRN-037
date: 2026-07-04
category: tooling
source_cs: CS16
status: applied
tags: [opa, rego, testing, windows]
```

**Problem:** A Rego-editing task needs `opa test` to validate policy changes, but the `opa` CLI is not preinstalled on the dev box, and the .NET test suite (which mocks the OPA HTTP response) does not exercise the actual Rego.

**Finding:** The official OPA binary at `https://openpolicyagent.org/downloads/latest/opa_windows_amd64.exe` runs **standalone** (no install/PATH changes): download it, run `opa test infra/opa/policy -v`, then delete it. CS16's OPA sub-agent used this to validate the added `rule` field (`opa test` 51/51) without any environment setup.

**Evidence:** CS16 `cs16-opa` sub-agent report (fetched OPA 1.18.2, ran `opa test` 51/51, removed the binary). AppHost pins `openpolicyagent/opa:1.18.2-static` for the container path.

**Implications carried forward:**
- CS17 (policy lifecycle/testing), CS20, CS24 and any Rego-touching CS: validate Rego edits with the standalone `opa` download rather than assuming a preinstalled CLI or relying solely on the mocked C# adapter tests.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-039

```yaml
id: LRN-039
date: 2026-07-04
category: process
source_cs: CS16
status: applied
tags: [ci, review-evidence, pr-body, review-log]
```

**Problem:** The `read-only-gates` / `review-log-evidence` CI gate rejected the CS16 content PR body with "## Review log row N contains template placeholder cell(s)" even though every row was fully filled — the offending cells merely contained the literal word "placeholder(s)" (e.g. "placeholders/args aligned") and `<role>`/`<action>` angle-bracket tokens inside a prose evidence cell.

**Finding:** `check-review-evidence` scans Review-log cells for template-placeholder patterns and flags the literal substring `placeholder` and `<...>` angle-bracket tokens ANYWHERE in a cell (not just the template's `_(...)_` form). When authoring Review-log evidence prose, avoid the word "placeholder" and any `<...>` tokens (write "format-string/arg alignment", "the `p, role, action` policy line", etc.). Also: the A3+A4 gate requires a `verdict=Go` row whose `analyzed_head` equals the CURRENT PR HEAD SHA — every new commit needs a fresh decisive-review row at the new HEAD.

**Evidence:** CS16 PR #56 CI: `review-log-evidence` failed on "row 4 contains template placeholder cell(s)" until "placeholders/args aligned" → "format-string + args aligned" and "`p, <role>, <action>`" → "`p, role, action`"; `A3+A4 review-evidence` failed until a `Go` row at HEAD `8f3b8cf`/`6f5e025` was appended.

**Implications carried forward:**
- Every content-PR review-log author: keep evidence cells free of the word "placeholder" and `<...>` tokens, and append a fresh `Go`-at-HEAD row after each new commit (including review-fix commits) or the gate fails.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-041

```yaml
id: LRN-041
date: 2026-07-04
category: tooling
source_cs: CS13
status: applied
tags: [dotnet, channels, concurrency, testing]
```

**Problem:** The PDP audit-forwarding sink hands each decision to a bounded `Channel<T>` via the non-blocking `ChannelWriter.TryWrite` and must COUNT dropped events (a backpressure signal) without ever blocking the decision hot path. The intuitive `BoundedChannelFullMode.DropWrite` did not work for this.

**Finding:** With `BoundedChannelFullMode.DropWrite`, `TryWrite` ALWAYS returns `true` and drops silently inside the channel, so the writer cannot observe or count drops. Use `BoundedChannelFullMode.Wait` instead: `TryWrite` still never blocks (only `WriteAsync` would await), but it returns `false` when the buffer is full, giving the sink an observable, countable drop. Verified empirically: DropWrite → first=true, second=true; Wait → first=true, second=false.

**Evidence:** `src/AuthzEntitlements.Authz.Pdp/Audit/PdpAuditSinkServiceCollectionExtensions.cs` (FullMode=Wait) + `HttpForwardingPdpDecisionAuditSink.cs` (TryWrite → dropped counter); `HttpForwardingAuditSinkTests` drop-count test; CS13 review-fix `cs13-fix` finding.

**Implications carried forward:**
- Any future non-blocking, drop-counting channel producer should use `FullMode=Wait` + `TryWrite`, not `DropWrite`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-042

```yaml
id: LRN-042
date: 2026-07-04
category: process
source_cs: CS18
status: applied
tags: [docs, citations, review, multi-agent, dotnet]
```

**Problem:** CS18 paired a code change (JWT `TokenValidationParameters` hardening) with docs that cite that same code by `file:line` (`docs/security/threat-model.md`, `secrets-and-least-privilege.md`). Adding lines to `AuthenticationSetup.cs` / `GatewayAuthenticationSetup.cs` shifted every citation below the insertion point, so the docs' `file:line` references silently pointed at the wrong lines — and it happened **twice** (the initial hardening, then again in the Copilot fix-round that added `ValidateIssuerSigningKey`).

**Finding:** When a CS ships a code change AND docs that cite that code by `file:line`, the citations WILL drift each time the code shifts. Mitigations: (1) have doc sub-agents cite the NEW-code control by **file + narrative** (not exact line numbers) when a sibling/fix-round is concurrently editing that file, and reserve `file:line` for stable, unchanged code; (2) at integration time AND after every review-fix round, `grep` the docs for citations to any changed file and re-verify each `file:line` by opening the target — treat citation drift as a mandatory re-verify step, not a one-time check; (3) the independent rubber-duck reviewer must spot-check that cited `file:line` references resolve (REVIEWS.md § 2.6a), which caught nothing here only because the orchestrator re-fixed them pre-review.

**Evidence:** In this session, `AuthenticationSetup.cs` `RequireHttpsMetadata`/`TokenValidationParameters`/`RoleClaimType` lines moved 108→115, 110-119→117-134, 117-118→132-133 across two edits; `docs/security/threat-model.md` citations at lines 129/132/198/209 and `secrets-and-least-privilege.md:102` were re-pointed twice to stay accurate.

**Implications carried forward:**
- Any docs+code CS: re-grep + re-verify every `file:line` citation to a changed file at integration and after each fix-round; prefer file+narrative for lines a concurrent agent is still moving.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-043

```yaml
id: LRN-043
date: 2026-07-04
category: process
source_cs: CS18
status: applied
tags: [recon, verification, multi-agent, citations]
```

**Problem:** The CS18 orchestrator ran a fast/cheap recon agent (gpt-5.4-mini) to map the security surface and threaded its findings into the sub-agent briefings. One finding was **wrong**: it claimed `Bank.Api` is externally exposed via `WithExternalHttpEndpoints()`. A doc implementer (claude-opus-4.8) caught it by opening `AppHost.cs` and confirming Bank.Api has NO external endpoint (only Grafana, edge-gateway, and bank-web do) — the more-secure, already-internal posture.

**Finding:** Recon produced by a fast/inexpensive model can contain confident factual errors. Every downstream agent (and the orchestrator) MUST verify each **current-state claim against source before citing it** — even claims the orchestrator itself provided in the briefing. Brief sub-agents explicitly that recon line numbers/claims are *approximate and unverified*, and that their job includes source-verification; a "briefing correction" note in the deliverable (as the secrets doc did) is the correct outcome, not silent propagation.

**Evidence:** `secrets-lp` sub-agent report + `docs/security/secrets-and-least-privilege.md` "Note — Bank.Api exposure (briefing correction)": `WithExternalHttpEndpoints` appears only at `AppHost.cs:37,:142,:151` (Grafana/edge-gateway/bank-web), NOT on `bank-api` at `AppHost.cs:115-124`.

**Implications carried forward:**
- Treat recon (especially from a cheap model) as a lead, not a fact. Sub-agent briefings must instruct: verify every current-state claim against source before citing; surface corrections in the report.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-045

```yaml
id: LRN-045
date: 2026-07-04
category: process
source_cs: CS20
status: applied
tags: [ci, copilot, review-gates, github-actions]
```

**Problem:** The CS20 content PR's `read-only-gates` job kept failing on the A5+A16 Copilot gate even after Copilot had reviewed an earlier HEAD, and the `pull_request_review`-triggered re-run did not clear it automatically.

**Finding:** The A5+A16 gate requires a Copilot review whose commit == the **current PR HEAD**; every new commit (including review-fix commits) needs Copilot to **re-review the new HEAD** — re-request via `gh api --method POST repos/<o>/<r>/pulls/<n>/requested_reviewers -f "reviewers[]=copilot-pull-request-reviewer[bot]"`. The auto re-run of `pr-evidence-lint` triggered by Copilot's review submission lands in **`action_required`** status (bot-triggered workflow runs need approval on this repo) and will NOT self-clear; once Copilot's current-HEAD review exists, re-run the previously-failed job (`gh run rerun <id> --failed`) or approve the pending run. Copilot re-scans the whole diff on every engage and re-emits its full comment set (REVIEWS.md § 2.4.3) — resolve those re-raises with a disposition, don't re-fix.

**Evidence:** PR #71 — Copilot reviewed `3f3952b` twice (9 comments) but not the later HEADs; A5+A16 failed at `bccb868` until Copilot was re-requested (reviewed `bccb868` @ 05:39:55Z) and run `28696430589` was re-run `--failed`; the review-triggered run `28696547030` sat in `action_required`.

**Implications carried forward:**
- Every .NET content-PR (CS22/CS24/CS14/CS15…): after the final fix commit, re-request Copilot at the merge HEAD, then re-run the failed `read-only-gates` job once Copilot's current-HEAD review lands; expect the review-triggered re-run to need a manual nudge.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-046

```yaml
id: LRN-046
date: 2026-07-04
category: tooling
source_cs: CS24
status: applied
tags: [dotnet, system-text-json, framework-reference, build]
```

**Problem:** Two .NET build/runtime gotchas surfaced building CS24's benchmark project (a plain `Microsoft.NET.Sdk` console/test project referencing the ASP.NET-Core `Authz.Pdp` project, and freezing a shared `JsonSerializerOptions`).

**Finding:** (1) `JsonSerializerOptions.MakeReadOnly()` (parameterless) throws `InvalidOperationException: ... must specify a TypeInfoResolver ... before being marked as read-only` for the default reflection-based serializer; use the `MakeReadOnly(populateMissingResolver: true)` overload to populate the default reflection resolver **and** freeze the instance (freezing is worth doing — a shared mutable `JsonSerializerOptions` that defines an on-disk contract is a footgun). (2) `<FrameworkReference Include="Microsoft.AspNetCore.App" />` does **not** transitively propagate from a referenced `Microsoft.NET.Sdk.Web` project to a plain console/test `Microsoft.NET.Sdk` project; any project that transitively touches ASP.NET-Core types (e.g. constructs the `aspnet` engine adapter) must declare the `FrameworkReference` itself.

**Evidence:** CS24 PR #75 — the benchmark console + test csproj each needed `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; `BenchmarkJson.Options` failed at type-init with the bare `MakeReadOnly()` (`ResultStoreTests` → `TypeInitializationException`) until switched to `MakeReadOnly(populateMissingResolver: true)`.

**Implications carried forward:**
- Any new non-Web .NET project referencing a `Sdk.Web` src project: add the `FrameworkReference` up front (matches the CS-tests convention).
- Freeze reflection-based shared `JsonSerializerOptions` with `MakeReadOnly(populateMissingResolver: true)`.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-047

```yaml
id: LRN-047
date: 2026-07-04
category: process
source_cs: CS24
status: applied
tags: [review, copilot, dotnet, fail-closed, robustness]
```

**Problem:** CS24's new .NET benchmark CLI passed a full-diff GPT-5.5 rubber-duck review (Go, no findings), but Copilot then surfaced a distinct legitimate robustness issue on each of 6 consecutive rounds.

**Finding:** For new .NET tool/CLI/parsing code, budget multiple Copilot rounds and expect Copilot to systematically catch **fail-closed / resource-cleanliness** gaps the rubber-duck misses: a subprocess `ReadToEnd`-before-`WaitForExit` hang, silently-accepted duplicate inputs (`--engines`, duplicate baseline keys), missing `schemaVersion` validation on deserialize, an uncancelled/unobserved async connect on a timeout probe, a mutable shared `JsonSerializerOptions`, and unvalidated numeric arguments. Each was a real hardening — treat "one Copilot finding per round" as convergence-in-progress, not noise (cf. LRN-031/024). Decline only with an explicit on-thread rationale (e.g. the leading-`-` value guard is intentional per LRN-040).

**Evidence:** CS24 PR #75 — 6 Copilot rounds (`fa0dd95`→`93408c8`), one valid fail-closed/robustness finding each, all fixed with tests (benchmark tests 46→52); the GPT-5.5 full-diff review found none of them.

**Implications carried forward:**
- CS22/CS15 and any new .NET CLI/tool code: pre-empt these classes (bounded subprocesses, dedupe inputs, validate schema + numeric args, cancel/await async probes, freeze shared config) before first review to cut Copilot rounds.

**Disposition:** Consolidated into `REVIEWS.md` `reviews.project-gates` local block by **CS33** (PR #119, `8c71a23`).

### LRN-048

```yaml
id: LRN-048
date: 2026-07-04
category: tooling
source_cs: CS14
status: applied
tags: [blazor, dotnet, static-ssr, oidc, warnings-as-errors]
```

**Problem:** Building the first Blazor Web App (Bank.Web) surfaced several .NET-10 Blazor gotchas that break a warnings-as-errors build or the interactive render mode in non-obvious ways.

**Finding:** For a Blazor Web App that calls token-protected APIs: (1) token-forwarding pages MUST be **static SSR** (no `@rendermode InteractiveServer`) so `IHttpContextAccessor.HttpContext` / `GetTokenAsync` are available on the request; an interactive component that needs identity reads it from the **cascaded `AuthenticationState`**, not `IHttpContextAccessor` (a circuit has no per-event HttpContext). (2) `app.MapStaticAssets()` is REQUIRED before `MapRazorComponents` — without it `_framework/blazor.web.js` (and `wwwroot/*`) 404 and interactive islands never hydrate. (3) Analyzer **BL0008** (warnings-as-errors) forbids a property initializer on a `[SupplyParameterFromForm]` property → use `= default!` + `??= new()` in `OnInitialized`. (4) **CS0542**: a routable component whose `@page` last segment matches an `@inject` member name collides with the generated class name → name the injected member distinctly. (5) `@rendermode InteractiveServer` shorthand needs `@using static Microsoft.AspNetCore.Components.Web.RenderMode` or the qualified `RenderMode.InteractiveServer`. (6) Multiple static-SSR `<EditForm>` on one page each need a unique `FormName` + matching `[SupplyParameterFromForm(FormName=...)]`.

**Evidence:** CS14 PR #76 — foundation + maker/checker/entitlements sub-agents each hit one of BL0008/CS0542/RenderMode; the R1 GPT-5.5 review returned Needs-Fix on the missing `MapStaticAssets` (verified via a standalone boot smoke test: `/_framework/blazor.web.js` 404→200). `docs/product/bank-web.md` documents the rendering/token strategy.

**Implications carried forward:**
- CS15 (playground/audit explorer) and any future Blazor UI: default token-protected pages to static SSR + enhanced forms; reserve interactive islands for anonymous-service widgets reading tenant/roles from cascaded auth state; include `MapStaticAssets`; pre-empt BL0008/CS0542 to cut review rounds.

**Disposition:** Consolidated into `CONVENTIONS.md` `conventions.project` local block by **CS33** (PR #119, `8c71a23`).

### LRN-035

```yaml
id: LRN-035
date: 2026-07-04
category: process
source_cs: CS17
status: applied
tags: [ci, testing, posture, process]
```

**Problem:** CS17's exit criterion "policy changes are gated by CI tests" collides with this repo's DELIBERATE posture that GitHub Actions run process-gates-only (`harness lint` + drift + review-evidence) while .NET build/test is the LOCAL correctness gate (CONTEXT.md; `.github/workflows/` carry no dotnet step). Adding an active `.NET` CI workflow would change that posture AND interacts with the `workflow-pins` gate (actions must be SHA-pinned).

**Finding:** When a CS deliverable's literal wording conflicts with an established repo posture/decision, do NOT silently change the posture. Deliver the INTENT (here: a runnable policy test suite of +59 golden/property/conformance tests that any policy change must pass) + a documented, ready-to-adopt opt-in path (a `policy-tests.yml` snippet in `docs/authz/policy-lifecycle.md`), and ESCALATE the posture decision to the maintainer (PR #55 Notes). The plan-vs-impl review marked CI-gating `diverged` (intentional), not `dropped`, and returned GO.

**Evidence:** `docs/authz/policy-lifecycle.md` CI note + adoption snippet; PR #55 Notes (escalation); the plan-vs-impl review in `done_cs17_*` (D1-CI = diverged, Outcome GO).

**Implications carried forward:**
- **Core gap addressed by CS28** (maintainer approved adopting .NET in CI on 2026-07-04): CS28 adds `.github/workflows/dotnet-ci.yml` — full-solution `dotnet build` + `dotnet test` on `pull_request` + `push`→`main`. **Advisory** (see residual below); the "no .NET in CI" gap itself is closed. This learning is now **`deferred`** (see Disposition) for the enforcement follow-up — the advisory check cannot yet be made required-to-merge on the current private tier.
- **Residual (needs branch protection → public repo or GitHub Pro):** the check cannot be required-to-merge, and the merge-order class (CS13↔CS16-style stale-green logical conflicts) is only fully *prevented* by require-up-to-date / a merge queue. CS28's `push`→`main` run detects it reactively; full prevention is a CS28 follow-up.
- Future eval/testing CSs (CS23/CS24) that mention "CI" can now rely on the CS28 `dotnet-ci` check.

**Disposition:** Harvest 2026-07-04 (CS28h): deferred. CS28 added an **advisory** `.github/workflows/dotnet-ci.yml` build+test gate; making it a **required** merge check needs branch-protection required-status-checks, unavailable on this private free-tier repo (discipline-only disposition — see `.harness-known-constraints.md` and INSTRUCTIONS.md § Re-evaluating private-tier disposition). Re-evaluate on tier change (private→public or Free→Pro) or by the deferred_until date. Harvest 2026-07-04 (CS37): still deferred; the required-status-check enforcement residual is being delivered by yoga-ae-c5's branch-protection / CI-merge-gating maintenance (planned CS40 'Review & PR merge-gate hardening'); deferred_until 2026-10-01 not reached — re-evaluate when CS40 lands or by the deferred_until date. Landed on main: PR #135 (commit c2bea79). Harvest 2026-07-05 (open-learnings harvest, yoga-ae-c2): **applied.** CS40 (done — "Review & PR merge-gate hardening") delivered the required-status-check enforcement this entry was deferred pending; verified 2026-07-05 the "push to main" ruleset now REQUIRES the `build-test` check (full-solution `dotnet build` + `dotnet test` from `.github/workflows/dotnet-ci.yml`), so a cross-CS .NET break can no longer silently merge to `main`. Residual: `strict_required_status_checks_policy` is false (no require-up-to-date) and no merge queue exists, so full prevention of stale-green cross-CS logical conflicts remains a branch-protection posture follow-up (yoga-ae-c5 domain) — tracked, not blocking.

### LRN-040

```yaml
id: LRN-040
date: 2026-07-04
category: process
source_cs: CS11
status: applied
tags: [ci, dotnet, merge, multi-agent, main-green]
```

**Problem:** During CS11 close-out, merging latest `main` into the content branch surfaced that `origin/main` itself did NOT compile: CS13's `HttpForwardingAuditSinkTests.SampleEvent` omitted the `DeterminingRule`/`PolicyReferences`/`Narrative` fields that CS16 made **required** on `PdpDecisionAuditEvent`. Two concurrently-developed CSs (CS13, CS16) changed a shared contract + a consumer of it, and the later merge did not rebuild against the earlier — so `main` went red and stayed red until an out-of-band hotfix.

**Finding:** Because this repo's CI is **process-gates-only** (`harness lint` + drift + review-evidence; no `dotnet build`/`test` step — see CONTEXT.md + the CS17 CI-posture learning), a cross-CS **.NET build break lands on `main` undetected** by CI. The LOCAL full-solution `dotnet build`/`dotnet test` is the ONLY code-correctness gate. Mitigations for a multi-orchestrator fleet: (1) before opening/merging a content PR, `git merge origin/main` locally and run the **whole-solution** `dotnet build` + `dotnet test` (not just your project) — this catches a concurrent CS's contract change against your code AND a pre-existing main break; (2) when `main` is very active, merge-latest + **admin squash-merge promptly** — CS11 hit **4 sequential `main` advances** during its review, forcing 3 re-merges, and a slow review cadence loses the race; (3) additive merge conflicts from two CSs adding a service (sln/AppHost/csproj) resolve by keeping BOTH — take `--theirs` for the `.sln` then `dotnet sln add` your projects to regenerate GUIDs/build-configs cleanly.

**Evidence:** CS11 close-out (this session): `origin/main` `d75d6ea` failed `dotnet build` (CS7036 missing `DeterminingRule` in `HttpForwardingAuditSinkTests.cs`) → fixed in the CS11 merge and independently hotfixed on main by **PR #60 "restore green main after CS13xCS16 audit-event merge"**; 3 re-merges (`3823f97`, `5b41c84`, `0fa1f0f`) across advances `9f2df6f`→`4139355`→`d75d6ea`→`2ddc857`.

**Implications carried forward:**
- Every orchestrator: run the whole-solution `dotnet build`/`dotnet test` against the latest-`main`-merged HEAD immediately before merging a content PR; do not rely on CI to catch .NET breaks.
- Consider (as a future CS, mindful of the deliberate process-only-CI posture + `workflow-pins` gate — see the CS17 learning) whether a SHA-pinned .NET build/test CI job is worth adding to catch cross-CS contract breaks automatically.

**Disposition:** Harvest 2026-07-04 (CS28h): deferred. CS28 added an **advisory** `.github/workflows/dotnet-ci.yml` build+test gate; making it a **required** merge check needs branch-protection required-status-checks, unavailable on this private free-tier repo (discipline-only disposition — see `.harness-known-constraints.md` and INSTRUCTIONS.md § Re-evaluating private-tier disposition). Re-evaluate on tier change (private→public or Free→Pro) or by the deferred_until date. Harvest 2026-07-04 (CS37): still deferred; the required-status-check enforcement residual is being delivered by yoga-ae-c5's branch-protection / CI-merge-gating maintenance (planned CS40 'Review & PR merge-gate hardening'); deferred_until 2026-10-01 not reached — re-evaluate when CS40 lands or by the deferred_until date. Landed on main: PR #135 (commit c2bea79). Harvest 2026-07-05 (open-learnings harvest, yoga-ae-c2): **applied.** The required CI gate this entry called for has landed: CS40 (done) made the `build-test` check REQUIRED on the "push to main" ruleset (verified 2026-07-05), so a cross-CS .NET build break like the CS13xCS16 case can no longer silently merge to `main`; the shipped `.github/workflows/dotnet-ci.yml` runs the whole-solution `dotnet build` + `dotnet test`. Residual: require-up-to-date (`strict_required_status_checks_policy`) is off and there is no merge queue, so the stale-green ordering class is not yet fully prevented — a branch-protection posture follow-up (yoga-ae-c5 domain).

## Obsolete

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
