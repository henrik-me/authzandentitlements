# E2E smoke gate — `aspire run` stack boots + basics work

The **e2e smoke gate** is the first end-to-end test of the system: it boots the **real**
`aspire run` stack (Keycloak + Postgres + observability + all seven project services, via
Docker) and asserts that the basics work end-to-end. It is the automated form of the manual
acceptance done by hand at CS56 close-out, and it is a **mandatory local gate before every
PR** as well as a `harness startup` check.

Unlike the CS50 app-model smoke test (`tests/AuthzEntitlements.AppHost.Tests`), which stops
at `BuildAsync` and never starts Docker, this gate calls `StartAsync` and exercises the
running app model — the only thing that catches runtime boot/OIDC/port regressions.

## What it checks

The gate has two opt-in tests (both guarded by `AspireStackE2EFactAttribute`), each booting the
stack once.

### Smoke — `AspireStackSmokeE2ETests`

The smoke test asserts:

1. **All 7 project services reach Healthy** — `entitlements-service`, `bank-api`,
   `edge-gateway`, `audit-service`, `authz-pdp`, `governance-service`, `bank-web` (via
   `ResourceNotifications.WaitForResourceHealthyAsync`, fast-failing on an unavailable
   resource).
2. **Keycloak OIDC discovery** — the realm's `.well-known/openid-configuration` returns
   **200** with a coherent http realm `issuer` (`http://…/realms/authz-bank`). The Keycloak
   endpoint is resolved from the app model (`app.GetEndpoint("keycloak", "http")`) rather than
   hard-coded: under `Aspire.Hosting.Testing` the fixed host port 8088 is proxied to a
   dynamically-allocated port (unlike `aspire run`, which binds 8088 directly), and Keycloak
   stamps the issuer from the request host — so the test asserts the issuer's **http scheme +
   `/realms/authz-bank` path shape**, not the exact `http://localhost:8088` authority (LRN-088).
3. **OIDC token round-trip** — a `bank-web` client + `teller1` / `Passw0rd!` password grant
   against the realm token endpoint returns a non-empty `access_token`.
4. **`bank-web` root** — `GET /` (explicit `http` endpoint) returns **200**.

The stack is torn down deterministically by the `await using` on the built application.

### Authenticated flow — `AuthenticatedFlowE2ETests` (CS58)

This test logs in as **`teller1`** and **`manager1`** (realm password grant, requesting the same
`openid bank.read bank.transactions.write bank.approvals.write` scopes `bank-web` does) and drives
the **authenticated** flow through the real stack — the coverage that catches the CS58 regression
(the five internal services defaulting to `ASPNETCORE_ENVIRONMENT=Production`, whose
`RequireHttpsMetadata=true` made the JWT handler reject the HTTP dev Keycloak authority → **HTTP 500
on every authenticated request**; a `bank-web` home-page 200 never exercises this path). API calls
go **through the edge gateway** (as `bank-web`'s `BankApiClient` does); break-glass is called
**directly on `governance-service`** (as `bank-web`'s `GovernanceClient` does — the gateway has no
governance route). It asserts:

1. **Tenant-scoped read** — `GET /api/accounts` → **200** for both users, with the three seeded
   CONTOSO accounts present (`CONTOSO-CHK-0001`, `CONTOSO-SAV-0001`, `CONTOSO-LON-0001`; count ≥ 3)
   and `FABRIKAM-CHK-0001` **absent** (tenant isolation); `GET /api/transactions` → **200** with
   ≥ 3 seeded transactions.
2. **Role contract (Decision #4)** — `teller1` `POST /api/accounts` → **403**. Creating accounts is
   `BranchManager`-only, so a teller is denied by design; the e2e asserts the deny.
3. **Manager create** — `manager1` `POST /api/accounts` → **201** (unique per-run account number),
   then the account is retrievable by id.
4. **Maker create** — both users `POST /api/transactions` (a below-threshold `Debit`, maker = the
   caller) → **201**, then retrievable by id.
5. **Break-glass** — `POST /api/governance/break-glass` **with the bearer attached** → **201**
   (pre-fix this 500s: governance runs the same `RequireHttpsMetadata` JWT path, and the forwarded
   bearer is what triggers it).

**Fixed-port issuer alignment (Decision #5).** Unlike the smoke test, this test **disables DCP port
randomization** — `appHost.Configuration["DcpPublisher:RandomizePorts"] = "false"` before
`BuildAsync` — so **Keycloak binds its declared host port 8088**, matching the
`http://localhost:8088` `Keycloak__Authority` the AppHost injects into `edge-gateway` / `bank-api` /
`governance-service`. Under the default testing proxy Keycloak lands on a dynamic port (LRN-088), so
the services cannot reach JWKS at `:8088` and the token issuer (dynamic host) would not match their
fixed authority → every authenticated call 401s. With the port pinned, the token issuer + the
services' authority + the reachable JWKS all agree, exactly as under `aspire run`. Because both e2e
tests boot the full stack (and this one needs a free 8088), assembly-level test parallelization is
disabled (`E2ECollectionBehavior.cs`) so the two never boot concurrently.

Data assertions are **lower-bound + seeded-present** (Decision #6): the e2e boots against an
**ephemeral** Postgres — its persistent data volume is stripped for the test run (see
`tests/AuthzEntitlements.E2E.Tests/E2EStack.cs`), so each run starts clean from the seeded
baseline and a force-killed prior run can never corrupt a reused volume. The test still uses
unique per-run account numbers and below-threshold amounts and never asserts an exact total.

The stack is torn down deterministically by the `await using` on the built application.

## Prerequisites

- **Docker running.**
- **No active `aspire run` on port 8088.** The smoke test uses a *proxied* Keycloak endpoint (so it
  does not itself bind 8088), but the CS58 authenticated test **pins Keycloak to 8088 directly**
  (`DcpPublisher:RandomizePorts=false`, for issuer/JWKS alignment) — so a live `aspire run` on 8088
  is a hard conflict, and running two full stacks at once is wasteful/flaky regardless. A busy 8088
  is the "a live stack is already up" signal: both tests fail-fast with a clear message if 8088 is
  already in use, and the node wrapper skips.
- **.NET 10 SDK** and **Node ≥ 20** (for the wrapper).

## How to run

Node wrapper (the `harness startup` entry point):

```bash
node --test tests/e2e-aspire-smoke.test.mjs
```

Or run the .NET e2e directly:

```bash
RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests
```

On PowerShell:

```powershell
$env:RUN_ASPIRE_E2E = '1'; dotnet test tests/AuthzEntitlements.E2E.Tests
```

Expect a first run to take roughly 2–3 minutes (real container boot + Keycloak realm
import).

## Skip conditions (and why a skip is NOT a pass)

The gate is designed to stay out of the fast/Docker-free loops:

- **Default `dotnet test AuthzEntitlements.sln` skips the e2e.** The e2e `[Fact]` is guarded
  by `AspireStackE2EFactAttribute`, which sets `Skip` unless `RUN_ASPIRE_E2E=1`. So the fast
  local loop and the Docker-free CI `build-test` job report the e2e as **Skipped** (it still
  **compiles** in CI, catching build breaks).
- **The node wrapper skips green** when Docker is unavailable or port 8088 is already in use,
  so Docker-less `harness startup` runs stay green and a live `aspire run` is never clobbered.

> **A *skipped* wrapper run does NOT satisfy the mandatory pre-PR gate.** The pre-PR gate
> requires Docker up, port 8088 free, and the e2e **actually running green**. A skip is a
> convenience for startup/CI — it is not a pass. Before opening a PR you must see the e2e
> run (not skip) and pass — both the CS57 smoke basics **and** the CS58 authenticated
> `teller1`/`manager1` flow (`AuthenticatedFlowE2ETests`).

## Roadmap

This is the **start** of the e2e gate — deliberately one smoke test in v1:

- **v1 (CS57):** local mandatory pre-PR gate + `harness startup` hook, boots the stack and
  asserts the four smoke basics above.
- **v2 (CS58):** the authenticated `teller1`/`manager1` API drive-through (`AuthenticatedFlowE2ETests`)
  — log in, read the seeded accounts/transactions through the edge gateway, create an account
  (manager) / transaction (both), assert the teller create-account deny, and exercise governance
  break-glass with the bearer.
- **Future:** an authenticated **UI** drive-through (Blazor pages, not just the API);
  per-engine (per-PDP-adapter) e2e coverage; a Docker-in-CI **required** e2e check (v1/v2 are a
  local honor-system gate, mirroring the local-review gate — the Docker-free required CI
  checks cannot enforce a Docker gate).
