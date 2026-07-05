# Local stack validation — 2026-07-05

Validation of the current .NET Aspire stack run **locally**, per **CS48** (the prerequisite
gating the held CS27/CS43/CS44). Environment: Windows, **.NET 10.0.100-rc.1**, **Docker 29.2.0**,
**Aspire CLI 13.1.0**.

> **Headline:** build + full test suite are green (0/0 build, **1648** tests pass), and the
> `aspire run` smoke **found and fixed a demo-blocking defect** — the AppHost could not start due
> to two Aspire resource-name collisions. After the fix the AppHost boots, the dashboard serves,
> and the infra containers come up healthy.

## 1. Build

`dotnet build AuthzEntitlements.sln -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s).**
(The `AuthzEntitlements.AppHost` project rebuilds 0/0 after the fix in §3.)

## 2. Full test suite

`dotnet test AuthzEntitlements.sln -c Debug` → **1648 passed, 0 failed, 0 skipped** across the nine
test projects:

| Test project | Passed |
|---|---|
| AuthzEntitlements.Authz.Pdp.Tests | 893 |
| AuthzEntitlements.Governance.Service.Tests | 174 |
| AuthzEntitlements.Bank.Web.Tests | 172 |
| AuthzEntitlements.Bank.Api.Tests | 96 |
| AuthzEntitlements.Edge.Gateway.Tests | 82 |
| AuthzEntitlements.Audit.Service.Tests | 72 |
| AuthzEntitlements.Compliance.Tests | 64 |
| AuthzEntitlements.Benchmarks.Tests | 52 |
| AuthzEntitlements.Entitlements.Service.Tests | 43 |
| **Total** | **1648** |

## 3. `aspire run` smoke — defect found + fixed

**Finding (demo-blocking):** `aspire run` (`dotnet run --project src/AuthzEntitlements.AppHost`)
**crashed on startup** with:

```
Aspire.Hosting.DistributedApplicationException: Cannot add resource of type 'ContainerResource'
with name 'unleash' because resource of type 'PostgresDatabaseResource' with that name already
exists. Resource names are case-insensitive.
  at Program.<Main>$(...) in AppHost.cs   (the unleash container registration; pre-fix)
```

and, after fixing that, the same failure for the `openfga` container registration.

**Root cause:** two container resources were named identically to a shared-Postgres logical
database: the `unleash` container vs the `unleash` DB, and the `openfga` container vs the `openfga`
DB. Aspire resource names are case-insensitive and must be unique across resource types, so the
AppHost threw before any resource started. This was latent because **no CI job runs the AppHost**
(`dotnet-ci.yml` only builds + tests), and the unit tests never exercise `Program.Main`.

**Fix (in-band per CS48 Decision #8):** renamed the two container **resources** (Aspire identity
only) to `unleash-server` and `openfga-server`; their `DATABASE_NAME` / datastore URIs still target
the `unleash` / `openfga` databases, and all references use the C# variables (`unleash`, `openfga`),
so behaviour is unchanged. See `src/AuthzEntitlements.AppHost/AppHost.cs`.

**Post-fix smoke result:** `aspire run` boots — the Aspire dashboard serves
(`https://localhost:17254`), the default-path infra containers come up **healthy**
(`postgres`, `keycloak`, `observability` = `grafana/otel-lgtm`), the .NET services launch (multiple
`dotnet` service processes; at least one `/alive` returns `Healthy`, and app HTTP endpoints
respond), and the AppHost log shows no exceptions.

**Scope note:** this bounded smoke validated that the previously-broken stack now boots and the
default critical path comes up. Exhaustively driving every authenticated end-to-end scenario
(seeded-user login → maker-checker → per-engine playground comparison → audit verify → governance
break-glass) through the UI is a manual pass — see the demo runbook — and is not automated here.

## 4. Opt-in engines

The out-of-process engines and the flag backend are opt-in (`.WithExplicitStart()`), so
`aspire run` does **not** auto-start them: `opa`, `openfga-server` (+ `openfga-migrate`),
`spicedb`, `cerbos`, and `unleash-server`. They were not individually driven in this bounded
smoke; start them from the dashboard (or set the matching `Pdp:Provider`) and follow the runbook
to exercise the swappable-engine comparison. Recorded as a known not-yet-exhaustively-validated
area.

## 5. Observability assessment (feeds CS43/CS44)

**Today:** the app exports OTLP to the always-on `grafana/otel-lgtm` container (per-service export
is gated on a non-empty `OTEL_EXPORTER_OTLP_ENDPOINT`, wired by `ServiceDefaults`); the container
came up **healthy** in the smoke. Traces, metrics, and logs for the default path flow to Grafana,
and the Aspire dashboard provides live resource/console/trace views. Entitlements quota metering is
the lightweight Postgres `UsageCounter` + OTel meter.

**Recommendation on the held metering work (CS43/CS44):** for a **demo/lab**, the current
observability (Aspire dashboard + Grafana OTLP + the lightweight OTel quota meter) is **sufficient
to tell the story**; the full OpenMeter stack (Kafka + ClickHouse + Redis + Postgres) is **not
warranted for the demo** on observability grounds alone. Treat CS43/CS44 as **not warranted yet** —
revisit only if the demo/lab specifically needs OpenMeter's usage-aggregation/billing surface.
This is an input to the CS43/CS44 hold precondition #3, not a decision to lift the holds.

## 6. Known gaps / follow-ups

- The AppHost had **no CI coverage**, which is why the boot-blocking collision went unnoticed.
  Recommend a follow-up CS to add a minimal "AppHost builds the application model" smoke test that
  constructs the `DistributedApplicationBuilder` and asserts it doesn't throw, so resource-name
  collisions fail CI.
- Exhaustive authenticated end-to-end scenario drive-through + per-opt-in-engine validation remain
  a manual pass (runbook).
