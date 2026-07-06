# CS56 — Repair `aspire run`: Keycloak HTTP scheme + internal-service endpoints

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs56/content
**Started:** 2026-07-06
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-05 — user opened `bank-web` under `aspire run` and got an OIDC discovery failure ("The response ended prematurely"). Live investigation this session traced it (plus several "Finished"/"Failed to start" resources) to the **.NET 10 GA + Aspire 13.4.6 lockstep bump (PR #190→#189, `357b08d`)**. User directive: "File a bug-fix CS to repair aspire run properly (Keycloak scheme + internal-service ports), then implement."
**Depends on:** none

## Goal

Restore a clean default `aspire run` local stack, broken by the .NET 10 GA + Aspire 13.4.6 lockstep bump. Two independent regressions must be fixed: (1) Keycloak's fixed host endpoint flipped from HTTP to **HTTPS** while every service still uses an `http://localhost:8088` OIDC authority, so the login round-trip 500s; (2) the five **internal** project services declare no HTTP endpoint, so under Aspire 13.4.6 they fall back to Kestrel's default `http://127.0.0.1:5000`, collide, and either crash ("Finished") or fail to resolve `.GetEndpoint("http")` references ("Failed to start"). After this CS, all seven project services reach Running/healthy on `aspire run` and the bank-web login completes, with a mechanical regression guard. No auth/domain logic changes.

## Background

Evidence gathered live this session (HEAD `3b57392`):

- **Keycloak scheme flip.** `AppHost.cs:96` pins `keycloakAuthority = http://localhost:8088/realms/authz-bank`. At runtime `curl http://localhost:8088/realms/authz-bank/.well-known/openid-configuration` → *Empty reply from server* (curl 52 = the .NET `HttpIOException: response ended prematurely (ResponseEnded)` the user saw); `curl https://localhost:8088/...` → **200**. The Keycloak container runs `start-dev --import-realm`, **exposes 8080/8443/9000**, but Aspire binds host **8088 → container 8443 (HTTPS)** and publishes only 8443+9000 to the host (8080 is exposed-but-unmapped). `Aspire.Hosting.Keycloak` was bumped `13.1.0-preview.1.25616.3` → `13.4.6-preview.1.26319.6`; the endpoint-scheme change rides that bump.
- **Design intent is HTTP.** `AppHost.cs:89-96` pins Keycloak to a fixed host port precisely so the OIDC **issuer is a stable `http://localhost:8088/...`** across the browser, bank-web, and bank-api. The realm (`infra/keycloak/authz-bank-realm.json`) is dev-only with `sslRequired: none`. The HTTPS flip is an accidental regression, not a design decision.
- **Port-5000 collision.** `bank-api`, `entitlements-service`, `governance-service` crashed at startup — stderr: `System.IO.IOException: Failed to bind to address http://127.0.0.1:5000: address already in use` (SocketException 10048) — so the Aspire dashboard shows them **Finished** (launched then exited). `audit-service` holds `127.0.0.1:5000` (won the race → survives). `bank-web` has a `launchSettings.json` (`applicationUrl` 5270/7238) so Aspire assigned it real proxy ports (50596/50597) and it survived (and served the error page).
- **"Failed to start" pair.** `authz-pdp` and `edge-gateway` produced **no DCP executable log at all** (never launched) → dashboard **Failed to start**. Both also inject `.GetEndpoint("http")` of an endpoint-less internal service — `authz-pdp` → `auditService.GetEndpoint("http")` (`AppHost.cs:325`), `edge-gateway` → `bankApi.GetEndpoint("http")` (`AppHost.cs:143`) — which cannot resolve when the target declares no `http` endpoint; `edge-gateway` additionally `WaitFor(bank-api)`, a now-terminal resource.
- **Root config gap.** Only `Bank.Web` and `Edge.Gateway` have a committed `Properties/launchSettings.json` (verified via `git ls-files`; not gitignored). The five internal services (`Bank.Api`, `Audit.Service`, `Entitlements.Service`, `Governance.Service`, `Authz.Pdp`) have **neither** a `launchSettings.json` **nor** an explicit `WithHttpEndpoint()` in `AppHost.cs`, so Aspire 13.4.6 gives them no endpoint and no `ASPNETCORE_URLS`.
- **Not previously caught.** `docs/observability/aspire-run-500-triage.md` records that CS32 deliberately never executed a full `aspire run`; both regressions stayed latent until this run.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Keycloak scheme | Restore the fixed host endpoint to **HTTP on 8088 → container 8080**, keeping `start-dev` (its HTTP listener is on by default) and the realm's `sslRequired: none`; services keep the `http://localhost:8088/realms/authz-bank` authority unchanged | Matches the documented dev-lab design (a stable `http://` issuer shared by browser + all services, `AppHost.cs:89-96`); avoids the self-signed-cert trust and browser TLS-warning friction an `https://` authority would force into a deterministic offline lab |
| 2 | Internal-service endpoints | Add an explicit `.WithHttpEndpoint()` (default endpoint name `http`, **Aspire-assigned dynamic port**) to each of the five internal project services: `bank-api`, `audit-service`, `entitlements-service`, `governance-service`, `authz-pdp` | They declare no endpoint today, so Aspire 13.4.6 leaves them on Kestrel default `:5000` (collision → "Finished") and leaves the existing `.GetEndpoint("http")` references (`AppHost.cs:143,325`) + service discovery unresolved (→ "Failed to start"). One named `http` endpoint fixes both failure modes at once |
| 3 | No fixed application ports | Use dynamic Aspire-assigned ports for the **five internal** endpoints (do **not** pin host ports); Keycloak (`8088`) stays the only intentionally fixed port. `Bank.Web`/`Edge.Gateway` keep their existing `launchSettings.json` fixed app URLs (5270/7238, 5100) — out of scope here (see R5) | Pinned app ports reintroduce collisions for the internal services; only Keycloak needs a fixed port (stable issuer). Scope note: the two services with committed `launchSettings` are a pre-existing, separate concern not addressed by this CS |
| 4 | Regression guard | Extend the AppHost app-model smoke test (CS50/CS55 surface) to assert **every `AddProject` resource declares an `http` endpoint** the **Keycloak resource's fixed host endpoint is HTTP on 8088 targeting container 8080** (assert the endpoint scheme + binding, not merely the already-`http` authority string) **and** the authority/issuer is `http://localhost:8088` | Makes both regressions mechanically catchable so the next Aspire/.NET bump cannot silently re-break the default `aspire run`; asserting the endpoint binding (not just the config string) is what catches the scheme flip |
| 5 | Change safety | **Endpoints + scheme only** — no change to auth logic, PDP providers, domain, or the deterministic default path; opt-in engines (OPA/OpenFGA/SpiceDB/Cerbos/Keto/Topaz/Unleash) stay `WithExplicitStart()` with no new `aspire run` Docker dependency | Keeps the fix surgical and low-risk; preserves the "runs with no extra Docker" invariant (LRN aspire opt-in engines) |
| 6 | Acceptance validation | A **manual full `aspire run`** on a clean machine is the exit gate: all 7 project services reach Running/healthy, `bank-web` login round-trips through Keycloak, and no `:5000` bind error appears — recorded in this CS file | The defect only manifests under a full run; unit + app-model tests alone cannot prove the browser→Keycloak→bank-web login round-trip |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck (cs56-plan-review) | 58ff81237965 | 2026-07-06T04:02:34Z | Go-with-amendments | All F1–F6 citations verified; fix sound. Amended: guard asserts Keycloak endpoint HTTP 8088→8080 (not authority str); https fallback covers all consumers; dynamic-port scope=5 internal svcs |

## Deliverables

- **D1 — `AppHost.cs` Keycloak endpoint.** Bind Keycloak's fixed host port 8088 to the container **HTTP** endpoint (target 8080) instead of HTTPS (8443), restoring the `http://localhost:8088` authority. Preserve the realm import and `KC_FEATURES_DISABLED=organization`. Verify the emitted OIDC issuer stays `http://localhost:8088/realms/authz-bank` (no `KC_HOSTNAME`/issuer drift).
- **D2 — `AppHost.cs` internal endpoints.** Add `.WithHttpEndpoint()` (name `http`, dynamic port) to `bank-api`, `audit-service`, `entitlements-service`, `governance-service`, `authz-pdp`. Confirm the existing `bankApi.GetEndpoint("http")` (`edge-gateway`) and `auditService.GetEndpoint("http")` (`authz-pdp`) references now resolve.
- **D3 — App-model smoke-test guard.** In the AppHost test project, add assertions that every `AddProject` resource exposes an `http` endpoint and that the **Keycloak resource endpoint is HTTP bound on host 8088 → container 8080** (scheme + target, not just the authority config string), plus the `http://localhost:8088` authority/issuer. Keep it offline/deterministic (no container start).
- **D4 — Docs.** Update `docs/observability/aspire-run-500-triage.md` (or a sibling note) with the Keycloak-scheme + port-5000 root cause and fix; sweep `docs/demo/local-demo-runbook.md` and `docs/validation/local-stack-validation.md` for any HTTP/port claims that need correcting.
- **D5 — `LEARNINGS.md`.** File one LRN: ".NET 10 GA + Aspire 13.4.6 flipped the Keycloak host endpoint to HTTPS and stopped assigning ports/`ASPNETCORE_URLS` to endpoint-less `AddProject` services — declare project HTTP endpoints explicitly and guard both in the app-model smoke test."

## User-approval gates

- **Scope approved.** Maintainer directive 2026-07-05: "File a bug-fix CS to repair aspire run properly (Keycloak scheme + internal-service ports), then implement." No further gate to implement.
- **Exit gate.** A full-run acceptance (Decision #6) recorded in this file before close-out.

## Exit criteria

- `aspire run` from a clean machine (no sibling run active): `keycloak`, `postgres`, `observability`, and all **7** project services (`bank-api`, `edge-gateway`, `entitlements-service`, `audit-service`, `authz-pdp`, `governance-service`, `bank-web`) reach **Running/healthy**; the Aspire dashboard shows **no** "Finished"/"Failed to start" project resource and **no** `address already in use :5000`.
- `bank-web` `/` redirects to Keycloak, a lab user (e.g. `teller1`/`Passw0rd!`) logs in, and the app renders authenticated (OIDC discovery + token acquired over `http://localhost:8088`).
- `dotnet build AuthzEntitlements.sln` + `dotnet test AuthzEntitlements.sln` green; the new app-model smoke assertions pass; `harness lint` → 0 failed.
- Deterministic default preserved: no new Docker beyond keycloak/postgres/observability; opt-in engines remain `WithExplicitStart()`.

## Risks + open questions

- **R1 — exact Keycloak API.** The `Aspire.Hosting.Keycloak 13.4.6-preview.1.26319.6` mechanism to serve the primary/fixed port over HTTP may differ from 13.1.0 (explicit `WithHttpEndpoint(port:8088, targetPort:8080, name:"http")` vs an integration option). Implementation confirms the exact call against a real `aspire run`. **Fallback K2:** if the integration cannot serve the fixed endpoint over HTTP, switch the authority to `https://localhost:8088` with `RequireHttpsMetadata=false` and a dev-cert-tolerant OIDC backchannel on **every Keycloak consumer** — `bank-api`, `edge-gateway`, `governance-service`, and `bank-web` (all services that validate tokens or run the OIDC flow), not only `bank-web` — plus document the browser cert caveat; recorded as an explicit fallback, not the default choice.
- **R2 — issuer stability.** The token `iss` must stay `http://localhost:8088/realms/authz-bank` for browser and services alike; verify no issuer drift after the endpoint change (JWT issuer validation fails otherwise).
- **R3 — `ASPNETCORE_URLS` injection.** `.WithHttpEndpoint()` must cause each app to bind the Aspire-assigned port (not `:5000`); confirm via the run (each service logs a distinct listen port; `:5000` no longer bound by a project).
- **R4 — preview churn.** The pinned Aspire packages are `-preview`; a future bump could shift endpoint behavior again — the D3 smoke-test guard is the mitigation.
- **R5 — `Bank.Web`/`Edge.Gateway` fixed ports (out of scope).** These two services keep committed `launchSettings.json` with fixed app URLs (`bank-web` 5270/7238, `edge-gateway` 5100); they work today (Aspire derives proxy ports from them, e.g. bank-web bound 50596/50597 this session). This CS deliberately does not change them. Converting them to dynamic ports for parallel-checkout `aspire run`s is a separate follow-up, not required here.

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| T1 (D1) — `AppHost.cs`: bind Keycloak fixed host port 8088 → container HTTP 8080 (restore `http://localhost:8088` authority) | pending | yoga-ae-c4 | Verify issuer stays `http://localhost:8088/realms/authz-bank` (R2) |
| T2 (D2) — `AppHost.cs`: add `.WithHttpEndpoint()` (name `http`, dynamic port) to bank-api, audit-service, entitlements-service, governance-service, authz-pdp | pending | yoga-ae-c4 | Fixes `:5000` collision + resolves existing `.GetEndpoint("http")` refs (L143/L325) |
| T3 (D3) — App-model smoke-test guard: every `AddProject` has an `http` endpoint + Keycloak endpoint HTTP on 8088→8080 | pending | yoga-ae-c4 | Offline/deterministic (no container start) |
| T4 (D4) — Docs: update `docs/observability/aspire-run-500-triage.md`; sweep demo/local-stack docs | pending | yoga-ae-c4 | — |
| T5 (D5) — `LEARNINGS.md`: file the .NET 10 / Aspire 13.4.6 endpoint-regression LRN | pending | yoga-ae-c4 | — |
| T6 (Decision #6) — Full `aspire run` acceptance: 7 project services Running/healthy, bank-web login round-trips, no `:5000` bind error | pending | yoga-ae-c4 | Exit gate; result recorded in this file |
| Close-out: docs + restart state | pending | yoga-ae-c4 | Update `WORKBOARD.md` + `CONTEXT.md` so a fresh agent can restart from the actual `aspire run` state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | File learnings in `LEARNINGS.md`; planned follow-up CS for R5 (Bank.Web/Edge dynamic ports) if pursued |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
