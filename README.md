# AuthZ & Entitlements Lab

A **.NET Aspire** lab for evaluating **fine-grained authorization** and **entitlements**
side by side — and a reusable reference architecture. It models a four-layer authorization
stack and demonstrates it through a **fintech / banking back-office** product (accounts,
approvals, segregation-of-duties, maker-checker) with full observability and a tamper-evident
audit log.

## What''s inside

Four-layer authorization stack (see [ARCHITECTURE.md](ARCHITECTURE.md)):

0. **AuthN** — OIDC/OAuth2 identity (Keycloak; Microsoft Entra ID as the real-world option).
1. **Coarse-grained authz** — token scopes/claims enforced at a YARP edge gateway (cheap, stateless first gate).
2. **Fine-grained authz (FGA)** — a unified, AuthZEN-aligned PDP with pluggable engines: ASP.NET Core native + Casbin.NET, OpenFGA (ReBAC), OPA/Rego, Cedar (expansion: SpiceDB, Cerbos, Ory Keto, Oso, Topaz).
3. **Entitlements** — commercial (plans/seats/features/quotas) and access-governance (access packages, JIT elevation, access reviews).

Plus: an evaluation **comparison matrix + market survey + benchmarks + ADRs**, an interactive
authorization **playground**, observability via Aspire + OpenTelemetry to Grafana/Prometheus/
Loki/Tempo, and a hash-chained **audit explorer**.

## Prerequisites

Per machine, one time. Everything below is standard tooling; the agent-harness itself
needs **no install** — it runs via `npx` (see Getting started). This repo has **no
`package.json`**, so there is **no `npm ci`** step.

| Tool | Why | Install (Windows / winget) |
|---|---|---|
| **.NET 10 SDK** (>= 10.0.100) | build/run the Aspire app | `winget install Microsoft.DotNet.SDK.10` |
| **Node.js >= 20** | runs the harness CLI via `npx` | `winget install OpenJS.NodeJS.LTS` |
| **Docker** (running) | container engines + infra (Postgres, OpenFGA, OPA, Keycloak, Grafana) | `winget install Docker.DockerDesktop` then start it |
| **`aspire` CLI** | orchestrates the app | `dotnet tool install -g Aspire.Cli` |
| **GitHub CLI (`gh`)** | PR / review-gate workflow + private-repo clone | `winget install GitHub.cli` then `gh auth login` |

macOS/Linux: install the [.NET 10 SDK](https://dotnet.microsoft.com/download),
[Node >= 20](https://nodejs.org), [Docker](https://docs.docker.com/get-docker/), and
[`gh`](https://cli.github.com); then `dotnet tool install -g Aspire.Cli`.

## Getting started

```sh
git clone https://github.com/henrik-me/authzandentitlements.git
cd authzandentitlements
```

This project is built with the **[agent-harness](https://github.com/henrik-me/agent-harness)**
process (clickstops, review gates, CI). To start a working session, open an agent at the repo
root, follow [INSTRUCTIONS.md](INSTRUCTIONS.md) (Session Start checklist), then claim the next
ready clickstop. Session sanity check + queue listing:

```sh
npx -y github:henrik-me/agent-harness#v0.15.0 startup
```

- **What to build and in what order:** [CONTEXT.md](CONTEXT.md) — the clickstop dependency + lane map and parallelization waves. The queue lives in `project/clickstops/planned/` (27 clickstops; **CS01** first).
- **How the process works:** [INSTRUCTIONS.md](INSTRUCTIONS.md), [OPERATIONS.md](OPERATIONS.md), [REVIEWS.md](REVIEWS.md).
- **Architecture and decisions:** [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

See [CONTEXT.md](CONTEXT.md) for current state, the active clickstop, and blockers. Nothing is
implemented yet — **CS01 (Aspire foundations)** is the first claimable clickstop.

## License

To be determined.