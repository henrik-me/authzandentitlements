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

- **.NET 10 SDK** (`dotnet --version` >= 10.0.100)
- **Node.js >= 20** (the process harness runs via `npx`)
- **Docker** (Desktop or engine) running — for the container-based engines and infrastructure
- **`aspire` CLI** — `dotnet tool install -g Aspire.Cli` (see https://aspire.dev)
- **GitHub CLI (`gh`)** — for the pull-request / review-gate workflow

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
npx -y github:henrik-me/agent-harness#v0.12.0 startup
```

- **What to build and in what order:** [CONTEXT.md](CONTEXT.md) — the clickstop dependency + lane map and parallelization waves. The queue lives in `project/clickstops/planned/` (27 clickstops; **CS01** first).
- **How the process works:** [INSTRUCTIONS.md](INSTRUCTIONS.md), [OPERATIONS.md](OPERATIONS.md), [REVIEWS.md](REVIEWS.md).
- **Architecture and decisions:** [ARCHITECTURE.md](ARCHITECTURE.md).

## Status

See [CONTEXT.md](CONTEXT.md) for current state, the active clickstop, and blockers. Nothing is
implemented yet — **CS01 (Aspire foundations)** is the first claimable clickstop.

## License

To be determined.