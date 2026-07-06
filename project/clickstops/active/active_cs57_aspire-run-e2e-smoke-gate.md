# CS57 — First e2e smoke gate: `aspire run` stack boots + basics work

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs57/content
**Started:** 2026-07-06
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-05 — user request: "create a test that needs to be run locally part of every change made before sending off to PR review, include the test in the harness startup process as well. The test needs to verify the steps with aspire run and all the necessary services. This is the start of the e2e test gate, ensuring the basics of the system works." Design approach **Option A** confirmed by the user (Aspire.Hosting.Testing `StartAsync` .NET e2e + a node `harness startup` wrapper).
**Depends on:** none (extends the CS50 app-model smoke test + the CS56 `aspire run` acceptance checks into an automated e2e gate)

## Goal

Stand up the **first end-to-end smoke gate** for the system: an automated test that boots the **real** `aspire run` stack (Keycloak + Postgres + observability + all seven project services, via Docker) and verifies the system's basics work end-to-end — the exact surface validated by hand at CS56 close-out. It runs as a **mandatory local gate before every PR** and is **wired into `harness startup`** (the only startup extension point is a `tests/*.test.mjs` node test), soft-skipping when Docker or the fixed Keycloak port is unavailable so Docker-less startups stay green. This CS establishes the gate + one smoke test; broadening the e2e coverage (authenticated UI drive-through, per-engine, CI-required) is deliberately future work.

## Background

- **Why now.** CS56 fixed two `aspire run` regressions (Keycloak HTTPS-scheme flip; internal-service `:5000` collision) that **neither `dotnet build` nor `dotnet test` caught** — they never evaluate the *running* app model. The CS50 app-model smoke test (`tests/AuthzEntitlements.AppHost.Tests`) constructs the model **Docker-free** via `Aspire.Hosting.Testing` `CreateAsync` → `BuildAsync` (never `StartAsync`), so it catches wiring/name collisions but **cannot** catch a runtime boot/OIDC/port failure. An e2e gate that actually **starts** the stack is the missing safety net.
- **The CS56 manual acceptance = the automation target.** CS56's Decision #6 acceptance (recorded in `done_cs56_…`) was: all 7 project services Running/healthy on unique ports (no `:5000`); `http://localhost:8088/realms/authz-bank/.well-known/openid-configuration` → 200 (issuer `http://localhost:8088/realms/authz-bank`); a `teller1`/`Passw0rd!` token round-trip over http; `bank-web` → HTTP 200. This CS automates exactly that.
- **`harness startup` hook.** `npx …agent-harness lint`-adjacent `harness startup` runs, as a hard-fail "broken tree" check, **`node --test tests/*.test.mjs`** (verified via `harness startup --help`). There are **no** node tests in `tests/` today (startup reports `0 pass / 0 fail`). So a `tests/*.test.mjs` file is the **only** consumer-side way to add a check to `harness startup` without modifying the harness itself (which is a separate repo, off-limits per Hard Rule § 6).
- **Toolchain in place.** `tests/AuthzEntitlements.AppHost.Tests` already references `Aspire.Hosting.Testing 13.4.6` (CPM), `Microsoft.AspNetCore.App` framework ref, and the AppHost project — the exact shape an e2e project needs. `Aspire.Hosting.Testing` supports `StartAsync()` (real containers) plus `ResourceNotifications.WaitForResourceHealthyAsync(name)` and `CreateHttpClient(name)` (dynamic-port resolution). xUnit is **2.9.3 (v2)** — no dynamic `Assert.Skip`, so conditional skip uses a `FactAttribute` subclass (Decision #3).
- **Lab credentials (for the token round-trip assertion).** All human realm users share `Passw0rd!`; the `bank-web` client (secret `bank-web-secret`) has the direct-access (password) grant enabled (`infra/keycloak/README.md`). Keycloak is pinned to the fixed host port **8088** (stable issuer).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Test technology | New `.NET` xUnit project `tests/AuthzEntitlements.E2E.Tests` using `Aspire.Hosting.Testing` `DistributedApplicationTestingBuilder.CreateAsync<Projects.AuthzEntitlements_AppHost>()` → `BuildAsync()` → **`StartAsync()`** (real Docker stack), with `ResourceNotifications.WaitForResourceHealthyAsync` + `CreateHttpClient(name)` | Idiomatic Aspire e2e; boots the **same** app model `aspire run` uses; robust programmatic health-waiting + dynamic-port resolution + clean `await using` teardown (no brittle PID/container cleanup). User-confirmed Option A |
| 2 | What "the basics" assert | One e2e `[Fact]` boots the stack once and asserts: **(a)** each of the 7 project services (`bank-api`, `edge-gateway`, `entitlements-service`, `audit-service`, `authz-pdp`, `governance-service`, `bank-web`) reaches **Healthy**; **(b)** Keycloak OIDC discovery `GET http://localhost:8088/realms/authz-bank/.well-known/openid-configuration` → **200** with `issuer` == `http://localhost:8088/realms/authz-bank`; **(c)** OIDC **token round-trip** — `bank-web` client + `teller1`/`Passw0rd!` password grant against the realm token endpoint → a non-empty `access_token`; **(d)** `bank-web` root (`CreateHttpClient("bank-web", "http")` `GET /`) → **HTTP 200** | Exactly the CS56 acceptance surface — the minimal "AuthN + edge + all services up" proof. Health + OIDC + a real token + the UI serving is the smallest set that proves the system basically works end-to-end. The `"http"` endpoint name is explicit because `bank-web` also has an https launch profile — the bare `CreateHttpClient("bank-web")` overload may resolve https/dev-cert |
| 3 | Opt-in skip (keep the fast/CI loop Docker-free) | Guard the e2e `[Fact]` with a custom `AspireStackE2EFactAttribute : FactAttribute` that sets `Skip = "…"` unless env `RUN_ASPIRE_E2E == "1"` — a **true xUnit skip**, **no new package** (xUnit 2.9.3 has no runtime `Assert.Skip`). So the default `dotnet test AuthzEntitlements.sln` (CI `build-test` + the fast inner loop) **skips** the e2e and stays Docker-free/fast | The e2e is heavy (~2–3 min, needs Docker); it must not run in the Docker-less CI `build-test` or the fast local loop. A true skip (vs an early-return no-op) reports honestly as *skipped* |
| 4 | `harness startup` hook | Add `tests/e2e-aspire-smoke.test.mjs` (Node ≥20 stdlib, ESM, zero deps) — picked up by `harness startup`'s `node --test tests/*.test.mjs`. It **skips** (green) when **Docker is unavailable** (`docker info` fails) **or host port 8088 is already in use** (an `aspire run` is active); otherwise it runs `dotnet test tests/AuthzEntitlements.E2E.Tests` with `RUN_ASPIRE_E2E=1` and asserts exit 0 | The only consumer hook into `harness startup`; the two skips keep Docker-less startups green and prevent clobbering a live `aspire run` (fixed port 8088 can't be double-bound). When Docker is up + 8088 free, startup runs the real e2e |
| 4b | Test-time environment guard | The e2e `[Fact]` itself fail-fasts with a clear message if host port 8088 is already bound before `StartAsync` (an `aspire run`/stale container holds it) | Turns an opaque bind failure into an actionable "another aspire run is active / stop it first" message; complements the wrapper's skip |
| 5 | Pre-PR gate = local, honor-system (v1) | Document the e2e as a **mandatory local gate before every PR** in REVIEWS.md `reviews.project-gates` + the INSTRUCTIONS.md `instructions.harness` pre-PR routine; **not** a CI-required check in v1. A Docker-in-CI **required** e2e gate is a deliberate, filed **future** extension | "Start of the e2e gate": v1 delivers the local gate + startup hook. The Docker-less required CI checks (`build-test` etc.) cannot enforce a Docker gate; this mirrors the existing local-review honor-system gate (discipline + review, not mechanization) |
| 6 | Solution registration | Add `AuthzEntitlements.E2E.Tests` to `AuthzEntitlements.sln` (so `dotnet build`/CI **compile** it and catch build breaks), while its only test **skips** without `RUN_ASPIRE_E2E` (Decision #3). Re-normalize `AuthzEntitlements.sln` to **LF / no-BOM** after `dotnet sln add` (LRN-079) | Compile-time coverage in CI without running the Docker e2e; keeps a broken e2e from silently rotting |
| 7 | No new dependencies | E2E.Tests csproj mirrors `AppHost.Tests` (version-less CPM `Microsoft.NET.Test.Sdk` / `xunit` / `xunit.runner.visualstudio` / `Aspire.Hosting.Testing`; `FrameworkReference Microsoft.AspNetCore.App`; `ProjectReference` AppHost). Node wrapper uses Node 20 stdlib only (`node:test`, `node:child_process`, `node:net`) | Follows repo conventions (CPM, zero-runtime-dep, zero-dep node); no `package.json` is introduced (repo has none) |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck (cs57-plan-review) | 8fe1511fa478 | 2026-07-06T05:37:23Z | Go-with-amendments | All F1–F6 claims verified; sound plan. Amended: bank-web CreateHttpClient uses the `http` endpoint; docs state a skipped wrapper ≠ pre-PR pass; added StopOnResourceUnavailable fast-fail |

## Deliverables

- **D1 — `tests/AuthzEntitlements.E2E.Tests/`.** New xUnit project: `AuthzEntitlements.E2E.Tests.csproj` (mirrors `AppHost.Tests`, Decision #7) + `AspireStackSmokeE2ETests.cs` (Decision #2 boot + assertions; `[Trait("Category","e2e")]`) + `AspireStackE2EFactAttribute.cs` (Decision #3 opt-in skip) + the port-8088 pre-check (Decision #4b). Resolve `bank-web` via `CreateHttpClient("bank-web", "http")` (explicit `http` endpoint — bank-web also has an https profile). Use generous timeouts (stack boot ~2–3 min) via `WaitForResourceHealthyAsync` with a `CancellationToken` (e.g. 5-minute cap), preferring `WaitBehavior.StopOnResourceUnavailable` so a **failed** resource fails fast rather than blocking to the timeout cap.
- **D2 — `tests/e2e-aspire-smoke.test.mjs`.** Node wrapper (Decision #4): Docker-availability + port-8088 skip checks; else spawn `dotnet test tests/AuthzEntitlements.E2E.Tests -c Debug` with `RUN_ASPIRE_E2E=1`, stream/capture output, assert exit 0. `node -c` clean; picked up by `harness startup`.
- **D3 — `AuthzEntitlements.sln`.** Add the E2E project (Decision #6); re-normalize `.sln` to LF/no-BOM.
- **D4 — `docs/testing/e2e-smoke.md`.** What the gate covers (the four checks), how to run it (`node --test tests/e2e-aspire-smoke.test.mjs`, or `RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests` with Docker up), the skip conditions, prerequisites (Docker + no active `aspire run` on 8088), and the **e2e-gate roadmap** (v1 local + startup; future: authenticated UI drive-through, per-engine, CI-required). **State explicitly that a *skipped* wrapper run does NOT satisfy the mandatory pre-PR gate** — the pre-PR gate requires Docker up, port 8088 free, and the e2e actually running **green** (a skip is a convenience for startup/CI, not a pass).
- **D5 — Docs / process wiring.** REVIEWS.md `reviews.project-gates` local block: add an "**E2E smoke gate — mandatory before every PR**" subsection — run the wrapper with **Docker up and no active `aspire run` on 8088 so the e2e actually runs green; a *skipped* run is NOT a pass** (honor-system like local review). INSTRUCTIONS.md `instructions.harness` local block: reference the e2e gate in the pre-PR routine (and note `harness startup` now runs it when Docker is up + 8088 free). Both edits **only** between the `harness:local-start/end` markers.

## User-approval gates

- **Approach approved.** User selected **Option A** (Aspire.Hosting.Testing `StartAsync` .NET e2e + node startup wrapper) 2026-07-05.
- **Exit gate.** A local run of the gate (Docker up) passing end-to-end, recorded in the CS file, before close-out.

## Exit criteria

- **e2e runs green (Docker up):** `RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests` boots the stack and all four checks (Decision #2) pass; containers are torn down afterward (no leftovers).
- **wrapper behaves:** `node --test tests/e2e-aspire-smoke.test.mjs` **runs** the e2e when Docker is up + 8088 free, and **skips green** when Docker is down or 8088 is busy.
- **startup wired:** `harness startup` picks up the node test and stays green (runs it, or skips it, per Decision #4).
- **fast loop unaffected:** `dotnet build AuthzEntitlements.sln` 0/0; default `dotnet test AuthzEntitlements.sln` (no env flag) reports the e2e **skipped** (not run) — CI `build-test` stays Docker-free and fast; `harness lint` 0 failed; all new text is LF/no-BOM.

## Risks + open questions

- **R1 — boot flakiness / timeouts.** Real container boot is slow/variable; use `WaitForResourceHealthyAsync` with a generous cap (~5 min) and clear failure messages. Keycloak realm import adds ~20–40 s before OIDC is live — poll the discovery endpoint with a bounded retry.
- **R2 — fixed port 8088 contention.** The e2e cannot run alongside an active `aspire run` (both bind host 8088). Mitigated by the wrapper's 8088 skip (Decision #4) + the test's pre-check (Decision #4b).
- **R3 — CI must not run the Docker e2e.** Confirm `dotnet test AuthzEntitlements.sln` (CI `build-test`, no `RUN_ASPIRE_E2E`) **skips** the e2e `[Fact]` (Decision #3). Verify the custom `FactAttribute` skip fires with the env unset.
- **R4 — startup cost for other agents.** With Docker up + 8088 free, `harness startup` will run the ~2–3 min e2e for any agent — an explicit consequence of "include it in startup"; the two skips bound the blast radius. (If this proves too heavy, a follow-up can add an opt-out env — deferred, not v1.)
- **R5 — `dotnet sln add` CRLF+BOM (LRN-079).** Re-normalize `.sln` to LF/no-BOM after the add.
- **R6 — Aspire.Hosting.Testing `StartAsync` API surface.** Confirm the `13.4.6` package exposes `StartAsync` + `ResourceNotifications.WaitForResourceHealthyAsync` + `CreateHttpClient(name)` as assumed; adjust to the actual API during implementation (record any deviation in `## Notes`).

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| T1 (D1) — `tests/AuthzEntitlements.E2E.Tests` project: csproj (mirror AppHost.Tests), `AspireStackSmokeE2ETests` (StartAsync + 4 basic assertions), `AspireStackE2EFactAttribute` (RUN_ASPIRE_E2E skip), 8088 pre-check | pending | yoga-ae-c4 | `CreateHttpClient("bank-web","http")`; `WaitForResourceHealthyAsync` StopOnResourceUnavailable |
| T2 (D2) — `tests/e2e-aspire-smoke.test.mjs` node wrapper (Docker + 8088 skip → `dotnet test` with RUN_ASPIRE_E2E=1) | pending | yoga-ae-c4 | zero-dep Node 20 stdlib; hooked by `harness startup` |
| T3 (D3) — add E2E.Tests to `AuthzEntitlements.sln` (re-normalize `.sln` LF/no-BOM, LRN-079) | pending | yoga-ae-c4 | tests skip without RUN_ASPIRE_E2E → CI stays Docker-free |
| T4 (D4) — `docs/testing/e2e-smoke.md` (coverage, how-to-run, skip conditions, roadmap; skipped ≠ pre-PR pass) | pending | yoga-ae-c4 | — |
| T5 (D5) — REVIEWS.md `reviews.project-gates` + INSTRUCTIONS.md `instructions.harness` local blocks (pre-PR e2e gate) | pending | yoga-ae-c4 | edit only between harness:local-start/end markers |
| T6 (Exit gate) — local e2e acceptance: `RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests` (Docker up) green; wrapper skips w/o Docker; default `dotnet test <sln>` skips e2e | done | yoga-ae-c4 | ✅ 2026-07-06: e2e booted the stack + 4/4 checks (~39s); wrapper ran green (~46s); default sln test skips e2e (1799 pass, Docker-free). See Notes |
| Close-out: docs + restart state | pending | yoga-ae-c4 | Update `WORKBOARD.md` + `CONTEXT.md` so a fresh agent can restart from the e2e-gate state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | File learnings in `LEARNINGS.md`; planned follow-up CS for the CI-required e2e (Decision #5) |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

- **2026-07-06 — Decision #2 deviation: Keycloak endpoint resolution under `Aspire.Hosting.Testing` (recorded per the plan-review hash-immutability rule; hashed sections unchanged).** Decision #2b/#2c specified OIDC discovery/token over the literal `http://localhost:8088/...` fixed authority with `issuer == http://localhost:8088/realms/authz-bank`. Under the testing builder the fixed host port 8088 is **proxied to a dynamically-allocated host port** (unlike `aspire run`, which binds 8088 directly), so a hardcoded `localhost:8088` is connection-refused. Implementation resolves Keycloak's base via `app.GetEndpoint("keycloak","http")` and builds the discovery/token URLs from it; and because Keycloak dev-mode stamps the issuer from the request host, the issuer assertion was adapted from the exact 8088 authority to a **realm-path + http-scheme shape** (`issuer` non-empty, `StartsWith("http://")`, `EndsWith("/realms/authz-bank")`). This preserves the plan's intent (OIDC discovery works + coherent http realm issuer + real token round-trip) and still catches both CS56 regressions (a Keycloak HTTPS-flip breaks the http discovery/issuer; a service-port collision fails `WaitForResourceHealthyAsync`). Filed as **LRN-088**.
- **2026-07-06 — T6 exit gate PASS.** The real e2e was run and passed: `RUN_ASPIRE_E2E=1 dotnet test tests/AuthzEntitlements.E2E.Tests` booted the full stack and passed all 4 checks (~39s); `node --test tests/e2e-aspire-smoke.test.mjs` ran the e2e green (~46s). Default `dotnet test AuthzEntitlements.sln` (no flag) reports the e2e **Skipped** (existing 1799 pass, Docker-free); `dotnet build` 0/0; `harness lint` 23/0; clean teardown (only the by-design `ContainerLifetime.Persistent` observability container remained, then stopped).

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
