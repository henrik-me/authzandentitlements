# Local demo / lab runbook

How to run the authz-and-entitlements lab **locally**. Validated 2026-07-05 (see
[`../validation/local-stack-validation.md`](../validation/local-stack-validation.md)). For the
full feature tour see [`../../README.md`](../../README.md) and
[`../../ARCHITECTURE.md`](../../ARCHITECTURE.md).

## Prerequisites

- **.NET 10 SDK**, **Docker** (running), and the **`aspire` CLI**
  (`dotnet tool install -g Aspire.Cli`).
- The default path is **container-backed**: `aspire run` starts Postgres, Keycloak (OIDC), and the
  `grafana/otel-lgtm` observability backend as always-on containers, so **Docker must be running**.

## Run the default stack

```bash
dotnet build AuthzEntitlements.sln
aspire run                                   # or: dotnet run --project src/AuthzEntitlements.AppHost
```

- Open the **Aspire dashboard** at the `https://localhost:<port>` URL printed on startup (it logs
  `Login to the dashboard at …`).
- Wait for `postgres`, `keycloak`, and `observability` to report **healthy**, then the .NET
  services (`bank-api`, `edge-gateway`, `entitlements-service`, `audit-service`, `authz-pdp`,
  `governance-service`, `bank-web`) to start. Each service exposes `/alive` + `/health`.
- Open **Bank.Web** (linked from the dashboard) to drive the product UI: seeded-user login via
  Keycloak, the maker-checker transaction walkthrough, the **AuthZ Playground** (per-engine
  comparison + fan-out), the **Audit Explorer** (+ hash-chain verify), and governance
  break-glass / delegation.

## Default vs opt-in engines

- **Default critical path** is deterministic and needs no external PDP engine: the in-process
  `reference`/`aspnet`/`casbin`/`cedar` providers (selected by `Pdp:Provider`, default
  `reference`).
- **Opt-in** out-of-process engines and the flag backend are `.WithExplicitStart()` — they do
  **not** auto-start. Start them from the Aspire dashboard when you want them: `opa`,
  `openfga-server` (with `openfga-migrate`), `spicedb`, `cerbos`, and `unleash-server`. To route
  the PDP at one, set `Pdp:Provider` (e.g. `openfga`, `spicedb`, `cerbos`) and start the matching
  container; to use real flags, start `unleash-server` and point the entitlements service at it.

> **Note:** the Aspire *resource* names are `unleash-server` and `openfga-server` (distinct from
> the `unleash` / `openfga` shared-Postgres databases). See the validation report §3 for why.

## Observability

Traces, metrics, and logs flow via OTLP to the `grafana/otel-lgtm` container; browse them in
Grafana (linked from the dashboard) and in the Aspire dashboard's live trace/console views.

## Shut down

Stop the `aspire run` process (Ctrl+C). Persistent dev containers (Postgres/Keycloak/observability)
may keep running by design; remove them with `docker rm -f <name>` if you want a clean slate.
