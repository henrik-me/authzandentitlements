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

## Troubleshooting

### `aspire run` Postgres fails to start after a hard kill (WAL corruption)

`aspire run` mounts a **persistent** Postgres data volume by design, so local bank data survives
dev restarts. A normal Ctrl+C shutdown is graceful, but a **hard kill** of the AppHost (an OS
crash, a force-stop, or a `kill -9` mid-write) can leave a corrupted write-ahead log in that
volume. On the **next** `aspire run`, Postgres then PANICs on startup and never reaches healthy —
so every Postgres-dependent service stays unhealthy and the stack hangs while booting.

**Symptom.** `postgres` never reports healthy; `docker logs` for the Postgres container shows a
PANIC such as:

```
PANIC:  could not locate a valid checkpoint record
... startup process (PID ...) was terminated by signal 6: Aborted
```

**Recovery.** Remove the corrupted persistent volume and re-run — Postgres recreates it and the
deterministic seeder repopulates it on the next boot:

```bash
# 1. Stop `aspire run` (Ctrl+C) if it is still running.
# 2. Find the Postgres data volume (its name ends in `-postgres-data`):
docker volume ls --filter name=postgres-data      # e.g. authzentitlements.apphost-<hash>-postgres-data
# 3. Remove it (destroys local dev data — see the warning below):
docker volume rm <the-postgres-data-volume-name>
# 4. Re-run — Postgres recreates the volume and the seeder repopulates it:
aspire run
```

> ⚠️ **Data loss.** `docker volume rm` **permanently discards all local dev bank data** in that
> volume (tenants, accounts, transactions, approvals). This is safe for a demo/lab — the seeder
> recreates the full seed dataset on the next boot — but do not run it if you have local data you
> need to keep.

> **Note:** the **e2e** suite is immune to this failure mode: it boots against an **ephemeral**
> Postgres (the data volume is stripped by `tests/AuthzEntitlements.E2E.Tests/E2EStack.cs`), so a
> force-killed test run can never corrupt a reused volume. This runbook applies only to the
> persistent `aspire run` dev volume.
