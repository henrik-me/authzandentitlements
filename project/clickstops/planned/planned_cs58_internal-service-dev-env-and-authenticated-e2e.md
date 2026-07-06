# CS58 — Fix internal-service Production env (RequireHttpsMetadata 500) + authenticated teller/manager e2e scenarios

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-06 — user reported that after login **teller1 and manager1 see no accounts/transactions** (bank-web "No accounts are available … fail-closed") and **break-glass fails with 500**. Live reproduction (this session, on the CS56/CS57 `main`) traced both to the same root cause and confirmed the fix. User also requested the e2e gate be expanded with the teller1/manager1 authenticated scenarios that would have caught it.
**Depends on:** none (fixes a regression exposed after CS56; extends the CS57 e2e gate)

## Goal

Fix the systemic **HTTP 500 on every authenticated request** to the internal services under `aspire run` (accounts/transactions reads fail-closed; governance break-glass 500), and **expand the CS57 e2e smoke gate** with authenticated **teller1 + manager1** scenarios (login → see the expected accounts/transactions → create account/transaction per role) so this class of regression is caught automatically. Root cause diagnosed + fix verified live before filing.

## Background

Reproduced live against the CS57 `main` stack (`aspire run`), decoding real tokens and reading service logs:

- **Root cause.** The five **internal** project services (`bank-api`, `governance-service`, `entitlements-service`, `audit-service`, `authz-pdp`) run as **`ASPNETCORE_ENVIRONMENT=Production`** under `aspire run`, because Aspire injects the environment only from a **`launchSettings.json` profile** — which `bank-web` and `edge-gateway` have (`ASPNETCORE_ENVIRONMENT=Development`) and these five internal **service** projects do **not** (same missing-launchSettings root as the CS56 port bug; the `AppHost` project itself also has a `launchSettings.json`, but it is the AppHost, not one of the five services). Confirmed: `bank-api`/`governance-service` log **`Hosting environment: Production`**; `edge-gateway`/`bank-web` log `Development`.
- **The failure.** `AuthenticationSetup`/`GatewayAuthenticationSetup` set `RequireHttpsMetadata = !environment.IsDevelopment()`. In Production that is **true**, so the JWT-bearer handler rejects the **HTTP** dev Keycloak authority (`http://localhost:8088/realms/authz-bank`) with `System.InvalidOperationException: The MetadataAddress or Authority must use HTTPS unless disabled for development by setting RequireHttpsMetadata=false` — a **500 on every request that reaches the auth middleware**. Observed on both `GET /api/accounts` (bank-api) and `POST /api/governance/break-glass` (governance).
- **Not the token.** The decoded `teller1` access token is correct: `iss=http://localhost:8088/realms/authz-bank`, `aud=bank-api`, `tenant=CONTOSO`, `branch=NM01`, `roles=Teller`, `scope=openid bank.read`. bank-web's "fail-closed" empty-state message is misleading — the API returned **500**, not a 401/403 auth denial.
- **Data is present.** `BankSeeder.SeedAsync` runs on **every** boot (not env-gated; `Bank.Api/Program.cs:43-48`), so the **3 CONTOSO accounts + 3 transactions** are in the DB — only the 500 blocks the read.
- **Why CS56/CS57 missed it.** CS56 validated Keycloak discovery + a token round-trip + bank-web's home page (200); CS57's e2e asserts bank-web serves 200 — **neither performs an authenticated read *through* bank-api**. So no test did "log in → read accounts". That is exactly the gap this CS closes.
- **Fix verified live.** With `ASPNETCORE_ENVIRONMENT=Development` forced on the five internal services (rebuild + `aspire run`), the services log `Development` and the full flow works: `teller1`/`manager1` `GET /api/accounts` → **200, 3**; `GET /api/transactions` → **200, 3**; break-glass → **201**; `manager1` create-account → **201**; `teller1` create-account → **403** (Teller is not BranchManager — correct); both create-transaction → **201**.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Fix mechanism | In `AppHost.cs`, force `ASPNETCORE_ENVIRONMENT=Development` on the five internal project services **in run mode only** (`builder.ExecutionContext.IsRunMode`), matching `bank-web`/`edge-gateway` | Addresses the missing-launchSettings root directly; run-mode guard keeps `aspire publish`/deploy environment-neutral; minimal + localized — no change to the security-sensitive `AuthenticationSetup`/`GatewayAuthenticationSetup` logic, no per-service launchSettings that could interact with CS56's `WithHttpEndpoint` ports |
| 2 | Rejected alternatives | Do **not** (a) weaken `RequireHttpsMetadata` in the two auth setups, or (b) add `launchSettings.json` to the five services | (a) changes a security-sensitive gate in two places and would let a real prod misconfig accept HTTP metadata; (b) scatters config + risks a port interaction with CS56's dynamic `WithHttpEndpoint` |
| 3 | E2e expansion | Add authenticated **teller1 + manager1** scenarios to the CS57 e2e (a new `[AspireStackE2EFact]`, same opt-in + `StartAsync` boot). Both log in (password grant) → assert the **seeded CONTOSO accounts + transactions are visible** (Decision #6); **manager1 create-account → 201**; **teller1 create-account → 403**; **both create-transaction → 201**; **break-glass → 201** with the bearer token attached (as bank-web's `AccessTokenHandler` does), so governance's JWT middleware exercises the `RequireHttpsMetadata` path (500 pre-fix / 201 post-fix) | Exercises AuthN → edge gateway → tenant-scoped read + role-gated write through the *real* stack — the coverage that would have caught this Production-env regression, plus the maker/role contract |
| 4 | teller create-account = **403**, asserted | The e2e asserts `teller1` create-account returns **403** (deny), not 201 | `POST /api/accounts` is `RequireAuthorization(BranchManager)` — a teller cannot create accounts by design. Asserting the deny makes the authz contract a verified guard (and documents the "teller can't create accounts" behaviour the user asked about) |
| 5 | Issuer/JWKS alignment + endpoint/token resolution | **Boot the authenticated e2e with fixed (non-randomized) host ports** so Keycloak binds its declared port **8088**, matching the `http://localhost:8088` `Keycloak__Authority` the AppHost injects into `edge-gateway`/`bank-api`/`governance-service` (`AppHost.cs`). Under default `Aspire.Hosting.Testing` port-randomization, Keycloak is on a dynamic port (LRN-088), so the services cannot reach JWKS at `:8088` and the token issuer (dynamic host) will not match their fixed authority → **401** (not the 200/403 the scenarios assert). Disable randomization (the DCP `RandomizePorts` knob — exact key confirmed in implementation) so the whole auth chain works exactly as `aspire run`. Resolve the gateway/governance HttpClients via `app.CreateHttpClient(name,"http")`; tokens via the realm password grant (`scope=openid bank.transactions.write bank.approvals.write`, matching bank-web). **Fallback:** if fixed ports are unavailable in 13.4.6, override each service's `Keycloak__Authority` to the resolved dynamic Keycloak endpoint before `StartAsync` | The authenticated flow requires the token issuer, the services' Keycloak authority, and the reachable JWKS to all agree; CS57's dynamic resolution alone (LRN-088) is insufficient once the services *validate* the token |
| 6 | Repeatable data assertions | Postgres uses a **persistent data volume** (`AppHost.cs`) and the seed is guarded on existing tenants (`BankSeeder.SeedAsync`), so the DB accumulates across `aspire run` / e2e runs. Assert the **three seeded CONTOSO accounts are present** (by their known numbers `CONTOSO-CHK-0001` / `CONTOSO-SAV-0001` / `CONTOSO-LON-0001`) and **count ≥ 3** — **not** an exact `== 3` total — and that each created account/transaction is **retrievable by its unique number**. Use unique (per-run GUID) account numbers; below-threshold transaction amounts | Exact totals are brittle on a persistent, shared DB; asserting the seeded records + a lower bound + create-then-read is repeatable and still proves the read/write path and the regression |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck | 4ae1f6607cd6 | 2026-07-06T07:37:22Z | Go | Amendments resolve prior blockers; fixed-port issuer alignment viable, data assertions repeatable, break-glass auth covered, launchSettings wording corrected. |

## Deliverables

- **D1 — `AppHost.cs` fix.** In run mode, set `ASPNETCORE_ENVIRONMENT=Development` on `entitlements-service`, `bank-api`, `audit-service`, `authz-pdp`, `governance-service` (Decision #1), with a comment citing the root cause.
- **D2 — E2e expansion** (`tests/AuthzEntitlements.E2E.Tests`). A new `[AspireStackE2EFact]` `[Trait("Category","e2e")]` test that boots with **fixed ports** (Decision #5) and asserts the Decision #3/#4/#6 teller1 + manager1 scenarios; a small helper to fetch a realm token + a gateway/governance `HttpClient`. Reuse the CS57 boot/health pattern.
- **D3 — `docs/testing/e2e-smoke.md`.** Add the authenticated teller1/manager1 scenarios to the "What it checks" list; note the role contract (teller create-account denied).
- **D4 — `LEARNINGS.md`.** New LRN: profile-less Aspire projects default to `ASPNETCORE_ENVIRONMENT=Production` under `aspire run` (only launchSettings-bearing projects get Development), which makes `RequireHttpsMetadata=true` reject the HTTP dev Keycloak → 500 on authenticated requests; force Development in run mode, and the e2e must exercise an authenticated read to catch it.

## User-approval gates

- **Bug + scope reported by the user** (accounts/transactions + break-glass fail; expand the e2e with teller1/manager1). No further gate to implement.
- **Contract clarification:** the e2e asserts `teller1` create-account = **403** (BranchManager-only) — surfaced to the user in the response.
- **Exit gate:** the expanded e2e passing (Docker up) recorded in the CS file before close-out.

## Exit criteria

- `RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests` (Docker up): the existing CS57 checks **and** the new teller1/manager1 authenticated scenarios pass (the 3 seeded CONTOSO accounts + transactions are visible to both — count ≥ 3, seeded records present; manager1 create-account 201; teller1 create-account 403; both create-transaction 201; break-glass 201 with a bearer).
- **Regression proof:** the new e2e **fails** if the D1 fix is reverted (services Production → 500), demonstrating it guards the regression.
- `dotnet build AuthzEntitlements.sln` 0/0; default `dotnet test AuthzEntitlements.sln` (no flag) still **skips** the e2e (existing suites green, Docker-free); `harness lint` 0 failed; LF/no-BOM.

## Risks + open questions

- **R1 — more moving parts / timing.** The new e2e adds authenticated HTTP calls through the gateway; use readiness retries + generous timeouts (as CS57 does).
- **R2 — creates mutate the seeded DB.** Assert the seeded 3/3 counts **before** creating; use unique account numbers; below-threshold transaction amounts.
- **R3 — write scope.** The e2e token must request `bank.transactions.write` (create-transaction needs it); request it in the password grant.
- **R4 — publish neutrality.** The `IsRunMode` guard must ensure `aspire publish`/deploy does not bake `Development` into the manifest; confirm during implementation.
- **R5 — fixed-port knob (Decision #5).** The authenticated e2e depends on disabling `Aspire.Hosting.Testing` port randomization so Keycloak binds 8088. If the 13.4.6 knob differs or is unavailable, use the fallback (override each service's `Keycloak__Authority` to the resolved dynamic Keycloak endpoint). Implementation must confirm the auth chain (issuer + JWKS + authority) actually agrees, or the scenarios 401 for the wrong reason.
- **R6 — persistent-DB mutation.** The e2e creates accounts/transactions in the shared persistent dev DB. Use clearly-test account numbers + below-threshold amounts; assertions are lower-bound + seeded-present (Decision #6) so the mutation is harmless and repeatable.

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per OPERATIONS.md § Claim) | planned | — | — |

## Notes / Learnings

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
