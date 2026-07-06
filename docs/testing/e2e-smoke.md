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

The single e2e test (`tests/AuthzEntitlements.E2E.Tests/AspireStackSmokeE2ETests.cs`) boots
the stack once and asserts:

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

## Prerequisites

- **Docker running.**
- **No active `aspire run` on port 8088.** The e2e uses a *proxied* Keycloak endpoint (so it
  does not itself bind 8088), but a live `aspire run` does — and running two full stacks at once
  is wasteful/flaky. A busy 8088 is the "a live stack is already up" signal: the test fail-fasts
  with a clear message if 8088 is already in use, and the node wrapper skips.
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
> run (not skip) and pass all four checks.

## Roadmap

This is the **start** of the e2e gate — deliberately one smoke test in v1:

- **v1 (this CS):** local mandatory pre-PR gate + `harness startup` hook, boots the stack and
  asserts the four basics above.
- **Future:** an authenticated UI drive-through (log in as `teller1`, exercise a transaction);
  per-engine (per-PDP-adapter) e2e coverage; a Docker-in-CI **required** e2e check (v1 is a
  local honor-system gate, mirroring the local-review gate — the Docker-free required CI
  checks cannot enforce a Docker gate).
