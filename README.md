# AuthZ & Entitlements Lab

A **runnable .NET Aspire lab** — and a reusable **reference architecture** — for evaluating
**fine-grained authorization** and **entitlements** side by side. It models a four-layer
authorization stack and exercises it through a **fintech / banking back-office** product
(accounts, transactions, approvals, maker-checker, segregation-of-duties), with full
observability and a tamper-evident audit log.

Everything runs locally with one command (`aspire run`). The default path is deterministic and
needs no third-party authorization engine; you opt into container-backed engines (OPA, OpenFGA,
SpiceDB, Cerbos) when you want to compare them.

> **Status:** the lab is substantially built out — 35 clickstops are merged (the four-layer stack,
> eight PDP engines, entitlements + governance, product UI, observability, hash-chained audit, and
> the evaluation lab). Expansion engines (Ory Keto / Oso / Topaz) are still in progress. See
> [CONTEXT.md](CONTEXT.md) for the live clickstop state and [ARCHITECTURE.md](ARCHITECTURE.md) for
> the design and key data flows.

## What's inside

**The four-layer authorization stack** (detail in [ARCHITECTURE.md](ARCHITECTURE.md)):

0. **AuthN** — OIDC/OAuth2 identity (Keycloak locally; Microsoft Entra ID as the real-world option).
1. **Coarse-grained authz** — token scope / claim / audience / tenant checks enforced at a YARP
   **edge gateway** (a cheap, stateless first gate).
2. **Fine-grained authz (FGA)** — a unified, **AuthZEN-aligned PDP** with a pluggable engine seam.
   Eight engines are integrated. Six answer the full fintech decision and its **22-scenario parity
   catalog** — `reference`, `aspnet`, `casbin`, `cedar` (in-process) plus `opa`, `cerbos`
   (container). Two are **ReBAC (Zanzibar)** engines — `openfga` and `spicedb` (container) — that
   model access as relationship tuples and run as selectable PDP providers; OpenFGA additionally
   exposes relationship-native reverse-index queries at `/api/authz/rebac/*`. All container engines
   are opt-in.
3. **Entitlements** — commercial (plans / modules / seats / features / quotas via OpenFeature,
   optional Unleash, usage metering) and access-governance (access packages, JIT elevation, access
   reviews, break-glass, on-behalf-of delegation).

Cross-cutting: a **tamper-evident, hash-chained audit log**; **explainability**
("why allowed / why denied") on every decision; OpenTelemetry → a bundled
**Grafana / Prometheus / Loki / Tempo** stack; an interactive **AuthZ Playground** +
**Audit Explorer**; and an **evaluation lab** (comparison matrix, market survey, ADRs, benchmarks,
TCO, compliance mapping).

## Prerequisites

Per machine, once. The agent-harness process tooling runs via `npx` (no install; this repo has no
`package.json`, so there is no `npm ci` step).

| Tool | Why | Install (Windows / winget) |
|---|---|---|
| **.NET 10 SDK** (>= 10.0.100) | build & run the Aspire app | `winget install Microsoft.DotNet.SDK.10` |
| **Docker** (running) | Postgres, Keycloak, the observability stack, and the opt-in engines run as containers | `winget install Docker.DockerDesktop` then start it |
| **`aspire` CLI** | orchestrates the app | `dotnet tool install -g Aspire.Cli` |
| **Node.js >= 20** | runs the harness process CLI via `npx` (contributors only) | `winget install OpenJS.NodeJS.LTS` |
| **GitHub CLI (`gh`)** | PR / review-gate workflow (contributors only) | `winget install GitHub.cli` then `gh auth login` |

macOS/Linux: install the [.NET 10 SDK](https://dotnet.microsoft.com/download),
[Docker](https://docs.docker.com/get-docker/), (and, for contributors, [Node >= 20](https://nodejs.org)
and [`gh`](https://cli.github.com)); then `dotnet tool install -g Aspire.Cli`.

## Quickstart — run the lab

```sh
git clone https://github.com/henrik-me/authzandentitlements.git
cd authzandentitlements
dotnet build AuthzEntitlements.sln
cd src/AuthzEntitlements.AppHost
aspire run
```

`aspire run` starts the **Aspire dashboard** (its URL is printed to the console) plus the
**default stack**: PostgreSQL, Keycloak, the observability container, and the app services —
`bank-api`, `edge-gateway`, `authz-pdp`, `entitlements-service`, `governance-service`,
`audit-service`, and the `bank-web` UI. Open the dashboard, then click the **`bank-web`** endpoint
to reach the product UI.

The default path requires **Docker** (for Postgres, Keycloak, and the observability container) but
**no third-party authorization engine** — the fine-grained PDP runs the deterministic in-process
`reference` engine. The engine containers (OPA, OpenFGA, SpiceDB, Cerbos) and Unleash are
**opt-in** and stay stopped until you start them (see
[Compare authorization engines](#compare-authorization-engines)).

Fixed local endpoint: **Keycloak** at `http://localhost:8088` (realm `authz-bank`). Grafana
(observability container port `3000`) and the app services get endpoints shown in the dashboard.

## Using the lab

### Log in

Sign in to `bank-web` with a seeded Keycloak user (all passwords `Passw0rd!`):

| Username | Role | Tenant |
|---|---|---|
| `teller1` | Teller | CONTOSO |
| `manager1` | BranchManager | CONTOSO |
| `compliance1` | ComplianceOfficer | CONTOSO |
| `auditor1` | Auditor | CONTOSO |
| `teller1-fabrikam` | Teller | FABRIKAM |

The realm is imported fresh on every start, so these users always exist and the lab is
deterministic.

### Walk a maker-checker flow

1. As `teller1`, open **Accounts** (`/accounts`) and **New Transaction** (`/transactions/new`).
   Transactions below the threshold post directly.
2. Submit a transaction of **10,000** or more (the approval threshold) — it becomes `Pending` and
   creates an approval (maker-checker).
3. Sign in as `manager1`, open **Approvals** (`/approvals`), and approve/reject. Segregation-of-duties
   blocks the maker from approving their own work, the checker must be tenant-scoped and
   checker-eligible, and approvals are decide-once (optimistic concurrency).

Transaction creation passes AuthN -> the coarse edge gateway -> Bank.Api (subject/account/tenant
prechecks, then commercial entitlements, then the maker-checker threshold); approvals enforce
segregation-of-duties and reads are tenant-scoped (neither calls Entitlements.Service). Each gate
emits a structured, audit-ready decision event to OpenTelemetry. (The unified PDP additionally
forwards its own decisions to the tamper-evident Audit.Service; broader ingestion of the
edge/Bank.Api events is planned.)

### Compare authorization engines

The fine-grained PDP (`authz-pdp`) is engine-agnostic. Two ways to compare engines:

- **AuthZ Playground** (`/playground` in `bank-web`): submit one request and fan it out across
  **every registered** engine at once, with per-engine decision, reasons, obligations, explanation,
  and latency, plus an "all agree?" check. Opt-in container engines appear as *Unavailable* rows
  unless their container is running.
- **Switch the active engine:** set `Pdp__Provider=<name>` on `authz-pdp` (default `reference`).
  Names: `reference`, `aspnet`, `casbin`, `cedar` (in-process, always available); `opa`, `openfga`,
  `spicedb`, `cerbos` (start the matching container first — use the **Start** action on the
  resource in the Aspire dashboard — then set the provider). An unknown (non-blank) name **fails
  closed**; a blank value falls back to the default (`reference`).

Verify engine parity against the shared catalog (get the `authz-pdp` base URL from the dashboard):

```sh
curl -X POST http://<authz-pdp>/api/authz/scenarios/verify
```

The six full-fintech engines must return the same decision + primary reason code across the
22-scenario `FintechScenarioCatalog`; the ReBAC engines (`openfga`, `spicedb`) model the same
domain as relationship tuples and run as selectable PDP providers, with OpenFGA additionally
exposing reverse-index queries (who-can-access / what-can-user-access) at `/api/authz/rebac/*`.
Related PDP surfaces: `/api/authz/evaluate`, `/api/authz/whatif`,
`/api/authz/shadow`, `/api/authz/policy/version`, and `/api/authz/authzen/evaluation`.

### Inspect the tamper-evident audit log

Open the **Audit Explorer** (`/audit` in `bank-web`) to browse recorded decisions and run a live
**hash-chain verify** badge, or call the API directly:

```sh
curl http://<audit-service>/api/audit/verify     # recompute + verify the chain
curl http://<audit-service>/api/audit/entries    # filtered / paged entries
```

The audit store is append-only and hash-chained — each row binds the previous row's hash, so
tampering with any hashed field, the sequence, or a chain link breaks verification. (The request
snapshot is persisted for replay but is not hash-bound.) The PDP forwards its decisions here; other
services emit audit-ready events via OpenTelemetry.

### Governance: JIT, break-glass, delegation

The **Access Requests** (`/access-requests`), break-glass, and delegation pages drive time-bound
JIT elevation, emergency break-glass access, and manager->delegate on-behalf-of grants. Approvals
run a **segregation-of-duties** check through the PDP and are fail-closed.

### Observability

Open **Grafana** from the dashboard — an anonymous **Editor** kiosk (the login form and HTTP basic
auth are both disabled, so there is no interactive or programmatic path to admin) with provisioned
dashboards
(Service Health, Request Rates, PDP Performance, Compliance). All services export OpenTelemetry
traces/metrics/logs to the bundled `grafana/otel-lgtm` collector. The Aspire dashboard shows live
resource health; Grafana persists telemetry across `aspire run` restarts.

## Build & test

```sh
dotnet build AuthzEntitlements.sln
dotnet test AuthzEntitlements.sln --no-build --no-restore
```

Build and test need **no Docker** — the OPA adapter tests are stubbed (deterministic), and the
OpenFGA / SpiceDB / Cerbos integration tests are env-gated, soft-skipping unless their
`*_TEST_*` endpoint variable is set.

## Evaluation lab deliverables

This repo doubles as an evaluation lab. Key outputs:

| Deliverable | Where |
|---|---|
| Engine comparison matrix (12 dimensions) | [docs/eval/comparison-matrix.md](docs/eval/comparison-matrix.md) |
| Market survey (ReBAC / policy engines / entitlements / AuthZEN) | [docs/eval/market-survey.md](docs/eval/market-survey.md) |
| Performance benchmarks | [docs/eval/performance-benchmarks.md](docs/eval/performance-benchmarks.md) |
| Managed vs. self-host TCO + cloud move | [docs/eval/managed-vs-selfhost-tco.md](docs/eval/managed-vs-selfhost-tco.md) |
| Architecture decision records | [docs/adr/README.md](docs/adr/README.md) |
| Compliance mapping (SOX / PCI-DSS / GDPR) | [docs/compliance/control-mapping.md](docs/compliance/control-mapping.md) |

## Documentation map

| Topic | Doc |
|---|---|
| System architecture + data flows | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Coarse vs. fine boundary | [docs/architecture/coarse-vs-fine-boundary.md](docs/architecture/coarse-vs-fine-boundary.md) |
| PDP contract + adding an engine | [docs/authz/pdp-contract.md](docs/authz/pdp-contract.md), [docs/authz/adding-an-engine-adapter.md](docs/authz/adding-an-engine-adapter.md) |
| Engine adapters | [aspnet + casbin](docs/authz/adapters-aspnet-casbin.md), [opa](docs/authz/opa-adapter.md), [cedar](docs/authz/cedar-adapter.md), [spicedb](docs/authz/spicedb-adapter.md), [cerbos](docs/authz/cerbos-adapter.md) |
| Explainability / policy lifecycle | [docs/authz/explainability.md](docs/authz/explainability.md), [docs/authz/policy-lifecycle.md](docs/authz/policy-lifecycle.md) |
| Audit pipeline | [docs/authz/audit-pipeline.md](docs/authz/audit-pipeline.md) |
| Entitlements & governance | [access-governance](docs/governance/access-governance.md), [break-glass runbook](docs/governance/break-glass-and-delegation-runbook.md) |
| Agent / non-human access | [docs/authz/agent-and-nonhuman-access.md](docs/authz/agent-and-nonhuman-access.md) |
| Identity (Entra ID mapping) | [docs/identity/entra-id.md](docs/identity/entra-id.md) |
| Observability | [docs/observability/observability-stack.md](docs/observability/observability-stack.md) |
| Security (threat model, secrets) | [docs/security/threat-model.md](docs/security/threat-model.md) |
| Product UI | [docs/product/bank-web.md](docs/product/bank-web.md) |

## Project status & process

This project is built with the **[agent-harness](https://github.com/henrik-me/agent-harness)**
process (clickstops, review gates, CI), pinned to `v0.16.0`. Contributors: follow
[INSTRUCTIONS.md](INSTRUCTIONS.md) (Session Start), then claim the next ready clickstop from
`project/clickstops/planned/`. Current state, the clickstop dependency map, and blockers live in
[CONTEXT.md](CONTEXT.md).

```sh
npx -y github:henrik-me/agent-harness#v0.16.0 startup
```

## License

To be determined.
