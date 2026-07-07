# `aspire run` empty-body 500 — triage (LRN-014)

> **Scope:** triage of the LRN-014 empty-body HTTP 500 that `Bank.Api` returned on
> **every** request (including `/alive`) under a full `aspire run`, blocking the edge
> gateway's `WaitFor(bank-api)`. Filed by CS04, carried through CS12, dispositioned in
> **CS32**. See [LEARNINGS.md](../../LEARNINGS.md) `### LRN-014`, the
> [observability stack](./observability-stack.md) note, and
> [`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs).

## Symptom

Under `aspire run` (AppHost orchestration), `Bank.Api` answered **HTTP 500 with an empty
body** on every request — including the dependency-free `/alive` liveness probe. Because
the gateway `WaitFor(bank-api)` gates on that probe, the gateway never started. Run
**standalone** (`dotnet run` of the service, without the Aspire-injected
`OTEL_EXPORTER_OTLP_ENDPOINT`), the same build served `200` and enforced authorization
correctly. LRN-014 therefore *suspected* an Aspire/OTLP-export interaction, but never
isolated it — the controlled difference was "under `aspire run`" vs. "standalone without
OTLP", which conflates many injected-environment differences, not just OTLP.

## What CS32 verified (offline)

The salient LRN-014 hypothesis — *"the OTLP exporter, pointed at a collector that is not
ready, faults the request path"* — was tested directly and **rejected**.

A probe booted a real ASP.NET Core host through the **actual** ServiceDefaults wiring
(`AddServiceDefaults()` → `ConfigureOpenTelemetry` → `AddOpenTelemetryExporters` →
`UseOtlpExporter()`), with `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at a **dead port**
(`http://127.0.0.1:59991`, connection-refused), and drove `/alive` and an `/api/*`
endpoint repeatedly across the batch-export interval:

| Request | OTLP endpoint | Result |
|---|---|---|
| `GET /alive` ×5 | unreachable (dead port) | **200** every time |
| `GET /api/ping` ×5 | unreachable (dead port) | **200** every time |

**Conclusion:** the OpenTelemetry OTLP exporter is **request-path isolated**. Export runs
on a background batch processor; a connection-refused / unready / unreachable collector
produces background exporter log noise but **cannot** turn a request into a 500. An unready
collector is therefore *not* a sufficient cause for the LRN-014 empty-body 500.

## Root-cause assessment

Given the exporter is proven request-path isolated, the empty-body 500 is **not** the
OTLP export itself. The most-likely root cause is an **early release-candidate
environmental interaction** in the AppHost-injected telemetry configuration at CS04 time:

- At CS04 (pre-CS12) there was **no collector**; Aspire auto-injected
  `OTEL_EXPORTER_OTLP_ENDPOINT` pointing at the **Aspire dashboard's** OTLP ingest, with an
  auto-injected auth header (`OTEL_EXPORTER_OTLP_HEADERS`). The failure surfaced only with
  the full Aspire-injected environment on the **.NET 10 RC1 / Aspire preview** toolchain of
  that moment — an environment/version-bound defect, not a defect in service code (the
  service is correct standalone).
- An empty-body 500 on `/alive` (no auth, no database, no outbound call) points at a
  failure *before or around* the endpoint that also defeats the error-page writer — the
  signature of a host/instrumentation-layer interaction, not application logic.

This class of RC-era orchestration defect is expected to be resolved by the **current**
toolchain (Aspire `13.1.0`, OpenTelemetry `1.16.x`) combined with the CS12 rewiring below.

## Mitigation state (already in place — CS12, re-confirmed by CS32)

No further `AppHost.cs` change is applied by CS32: the relevant mitigations already exist
and the exporter's request-path isolation is now proven, so additional changes would be
speculative and risk destabilizing the default `aspire run` path.

1. **Real, health-gated collector.** Every OTLP-exporting service points at the
   `grafana/otel-lgtm` collector and declares `WaitFor(observability)`, so it starts only
   after the collector container is up. Coverage is **uniform — 7/7 services**
   (`bank-api`, `edge-gateway`, `entitlements-service`, `audit-service`, `authz-pdp`,
   `governance-service`, `bank-web`). There is **no missing `WaitFor`** to add.
2. **Exporter resilience (defense in depth).** Even without the `WaitFor`, an unready
   collector cannot 500 a request (verified above). The two mitigations are independent.
3. **Isolation seam preserved.** ServiceDefaults gates `UseOtlpExporter()` on
   `OTEL_EXPORTER_OTLP_ENDPOINT` being non-empty, so the LRN-014 debugging technique —
   run a service standalone **without** the OTLP env to separate service logic from the
   AppHost OTLP layer — remains available (see [runbook](#isolation-runbook)).

## Remaining verification

A definitive check requires a **full clean `aspire run`** (Docker + Keycloak + Postgres +
the `grafana/otel-lgtm` collector + all projects) confirming `Bank.Api` serves `/alive`
`200` and the gateway starts. CS32 deliberately did **not** execute a full `aspire run`
from its environment: a parallel `aspire run` may be active (the AppHost pins Keycloak to
the fixed host port `8088`, and Keycloak/Postgres dev containers were already up), and the
CS32 constraint is to **not destabilize the default `aspire run` path**. The full-run
confirmation is a low-risk follow-up for the orchestrator/human on a clean machine.

**Update (CS60):** the telemetry-arrival half of this confirmation — that telemetry actually
reaches the collector under a full `aspire run` — is now covered by an automated guard,
[`TelemetryArrivalE2ETests`](../../tests/AuthzEntitlements.E2E.Tests/TelemetryArrivalE2ETests.cs)
(opt-in `RUN_ASPIRE_E2E=1`), which boots the full stack, drives inbound traffic, and asserts
`http_server_request_duration_seconds_count > 0` in the `grafana/otel-lgtm` collector's
Prometheus. The `Bank.Api` `/alive` `200` + gateway-start check remains the manual runbook
below.

### Full-run runbook

```powershell
# from a clean machine (no other aspire run active), repo root:
cd src/AuthzEntitlements.AppHost
aspire run
# In the Aspire dashboard: confirm `bank-api` reaches Running (its /alive probe is 200),
# then `edge-gateway` starts (its WaitFor(bank-api) clears). Open the `observability`
# resource's grafana endpoint and confirm telemetry is arriving.
```

### Isolation runbook

If a service misbehaves under `aspire run` and an OTLP interaction is suspected, isolate it
from the AppHost OTLP layer by running it standalone **without** the OTLP env:

```powershell
# Bank.Api standalone: no OTEL_EXPORTER_OTLP_ENDPOINT -> ServiceDefaults leaves the OTLP
# exporter OFF, so any remaining 500 is service logic, not the telemetry layer.
cd src/AuthzEntitlements.Bank.Api
$env:OTEL_EXPORTER_OTLP_ENDPOINT = $null
dotnet run
# curl http://localhost:<port>/alive   # expect 200
```

## Optional future hardening (not applied)

`WaitFor(observability)` currently gates on the **container reaching Running**, not on the
**OTLP ingest port accepting** — the `observability` container declares no health check.
Adding a container health check (e.g. Grafana's `/api/health`) would make `WaitFor`
gate on ingest readiness. This is **not** applied because (a) the exporter is already
request-path isolated, so the extra gate does not prevent any 500, and (b) a wrong health
probe would itself risk destabilizing the default `aspire run` path. Recorded here as a
deliberate, low-value-vs-risk deferral.

## CS56 — `aspire run` broken by the .NET 10 GA + Aspire 13.4.6 bump (2026-07-06)

> **Scope:** a *distinct* `aspire run` breakage from the LRN-014 empty-body 500 above. Filed
> and fixed in **CS56** after the .NET 10 GA + Aspire 13.4.6 lockstep bump (PR #189). Two
> independent regressions rode that bump; both are now fixed in
> [`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs) and guarded by the AppHost
> app-model smoke test. See [LEARNINGS.md](../../LEARNINGS.md) `### LRN-087`.

### Symptom

Opening `bank-web` under `aspire run` failed OIDC discovery with *"The response ended
prematurely"*, and the Aspire dashboard showed several project resources **Finished** or
**Failed to start** — none of which happened on the pre-bump toolchain.

### Root cause 1 — Keycloak's fixed 8088 endpoint flipped to HTTPS

`Aspire.Hosting.Keycloak` was bumped `13.1.0-preview` → `13.4.6-preview.1.26319.6`. The
integration declares the fixed host endpoint as **HTTP on 8088 → container 8080**, but in run
mode it also subscribes to a `BeforeStart` HTTPS-endpoint update that, **when a developer
certificate is available**, rewrites that same `http` endpoint to `UriScheme = "https"` /
`TargetPort = 8443`. So host `8088` bound the container's **HTTPS (8443)** listener:
`curl http://localhost:8088/realms/authz-bank/.well-known/openid-configuration` returned an
empty reply while the `https://…` form returned `200`. Because every service uses a stable
`http://localhost:8088/realms/authz-bank` OIDC authority (the dev realm is `sslRequired: none`),
the login round-trip 500'd.

**Fix:** call `.WithoutHttpsCertificate()` on the Keycloak resource. It records an
`HttpsCertificateAnnotation` with `UseDeveloperCertificate = false`, which gates the run-mode
HTTPS-endpoint update off, so the fixed 8088 endpoint stays **HTTP → container 8080** and the
`http://localhost:8088` issuer is unchanged (no `KC_HOSTNAME`/issuer drift).

### Root cause 2 — endpoint-less project services collided on Kestrel `:5000`

Under Aspire 13.4.6, an `AddProject` resource that declares **no** HTTP endpoint (no
`launchSettings.json`, no `WithHttpEndpoint()`) is no longer assigned an endpoint or
`ASPNETCORE_URLS`, so it falls back to Kestrel's default `http://127.0.0.1:5000`. The five
internal services (`bank-api`, `audit-service`, `entitlements-service`, `governance-service`,
`authz-pdp`) all fell back to `:5000`, collided (`address already in use`), and either exited
(**Finished**) or left the existing `.GetEndpoint("http")` references — `edge-gateway` →
`bank-api`, `authz-pdp` → `audit-service` — unresolved (**Failed to start**).

**Fix:** add an explicit `.WithHttpEndpoint()` (endpoint name `http`, Aspire-assigned dynamic
port) to each of the five internal services, so every service binds a unique port and the
`.GetEndpoint("http")` references resolve.

### Regression guard

The AppHost app-model smoke test
([`AppHostApplicationModelSmokeTests`](../../tests/AuthzEntitlements.AppHost.Tests/AppHostApplicationModelSmokeTests.cs))
adds two Docker-free (`BuildAsync`, never `StartAsync`) assertions: every `AddProject` resource
exposes an `http`-scheme endpoint named `http`; and the Keycloak resource keeps its `http`
endpoint on host 8088 → container 8080 **and** carries the `HttpsCertificateAnnotation`
(`UseDeveloperCertificate = false`) that suppresses the run-mode HTTPS flip. (The flip fires
only at `BeforeStart`, never during `BuildAsync`, so asserting that anti-flip annotation is what
mechanically catches removal of the fix.)

## References

- [LEARNINGS.md](../../LEARNINGS.md) — `### LRN-014` (problem, evidence, CS12 update).
- [observability stack](./observability-stack.md) — the CS12 collector + per-service OTLP
  wiring; the "Known issue — LRN-014" pointer.
- [`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs) — the `observability`
  container, the `otlpEndpoint` expression, and the per-service
  `OTEL_EXPORTER_OTLP_ENDPOINT` + `WaitFor(observability)` wiring.
- [`ServiceDefaults/Extensions.cs`](../../src/AuthzEntitlements.ServiceDefaults/Extensions.cs) —
  `AddOpenTelemetryExporters`, which gates `UseOtlpExporter()` on
  `OTEL_EXPORTER_OTLP_ENDPOINT`.
