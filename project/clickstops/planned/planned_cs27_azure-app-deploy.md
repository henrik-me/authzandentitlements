# CS27 — Azure deployment of the Aspire app (azd → Container Apps)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Filed by:** yoga-ae-c2 on 2026-07-04 — rescoped from the original "Full OpenMeter metering + Azure deployment" CS27. Per user decision (2026-07-04) the metering and cloud-deploy concerns are decoupled: full OpenMeter metering (local) moves to CS43 and Azure deployment of OpenMeter moves to CS44; this CS now covers only Azure deployment of the app as it ships today (lightweight metering).
**Depends on:** CS10, CS12, CS14, CS15, CS25

## Goal

Deploy the whole Aspire application to Azure via `azd` → Azure Container Apps, and document the local-vs-cloud differences. Metering stays as the shipped lightweight Postgres + OTel counter; OpenMeter is out of scope (CS43/CS44).

## Background

- The original CS27 bundled two loosely-coupled deliverables — full OpenMeter metering **and** Azure deployment. They have disjoint dependencies (OpenMeter's driver is CS10's metering seam; Azure deploy's driver is CS25's cloud-move analysis), so bundling them coupled the cloud-deploy milestone to heavier metering infra (Kafka/ClickHouse). This CS is the Azure-deploy slice of that split.
- The **CS25** TCO doc's Azure cloud-move section (`docs/eval/managed-vs-selfhost-tco.md`) is the direct design input: ACA vs AKS, Azure Database for PostgreSQL Flexible Server, OTLP re-point, and the AVP-is-AWS-only constraint.
- The app runs under the Aspire AppHost (`src/AuthzEntitlements.AppHost/AppHost.cs`): a shared Postgres server (six logical DBs — `bank`, `openfga`, `entitlements`, `governance`, `audit`, `unleash`), `grafana/otel-lgtm` for observability, an in-process deterministic PDP default, and opt-in engines (`opa`/`openfga`/`unleash`) wired via `.WithExplicitStart()` off the default `aspire run` path.
- Metering today is a lightweight Postgres `UsageCounter` + OTel meter (`AuthzEntitlements.Entitlements`) per ADR 0005 (`docs/adr/0005-entitlements-via-openfeature-and-usage-metering.md`); this CS does not change it.
- `ServiceDefaults` already gates OTLP export on a non-empty `OTEL_EXPORTER_OTLP_ENDPOINT` (`src/AuthzEntitlements.ServiceDefaults/Extensions.cs`), so re-pointing telemetry to Azure is config, not code.
- **Identity is always-on, not opt-in.** `AppHost.cs` wires Keycloak via `builder.AddKeycloak("keycloak", port: 8088)` (no `.WithExplicitStart()`), stamping issuer `http://localhost:8088/realms/{realm}`; Bank.Api, Edge Gateway, Governance, and Bank.Web all `.WithReference(keycloak).WaitFor(keycloak)` and read `Keycloak__Authority`/`Keycloak__Audience` (Bank.Web also carries a dev `Keycloak__ClientSecret`). A straight ACA deploy therefore needs a cloud identity story — a hosted Keycloak with a stable external issuer + non-dev secrets, or Entra ID per `docs/identity/entra-id.md` — or authenticated core scenarios will not work.
- **State-of-world probe (2026-07-04, F6):** `project/clickstops/{planned,active,done}/` contain CS ids up to CS40 are in use on `origin/main` (with sibling-held gaps at 35/38/39 being actively renumbered by concurrent orchestrators); CS43/CS44 were chosen with margin above the current max to avoid the live filing race; deps CS10, CS12, CS14, CS15, CS25 are all in `project/clickstops/done/`.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Scope | Deploy the current app only; OpenMeter excluded | Decouples the cloud-deploy milestone from heavier metering infra (the two dedicated OpenMeter CSs); smaller blast radius and review. |
| 2 | Target compute | Azure Container Apps via `azd` | CS25 cloud-move favours ACA over AKS at this scale; `azd` generates ACA infra from the Aspire app model. |
| 3 | Managed data | Azure Database for PostgreSQL Flexible Server | Replaces the container Postgres per the CS25 cloud-move section. |
| 4 | Observability | Re-point OTLP to Azure Monitor / Azure Managed Grafana | `ServiceDefaults` already gates OTLP on `OTEL_EXPORTER_OTLP_ENDPOINT`, so it is a config re-point. |
| 5 | Metering | Keep the shipped lightweight Postgres + OTel counter | OpenMeter has its own dedicated CSs (local + Azure); this CS deploys what already ships. |
| 6 | Opt-in engines in cloud | Keep `opa`/`openfga`/`unleash` opt-in (not on the default deploy path) | Preserves the deterministic Docker-free default; cloud parity with local. |
| 7 | Cloud identity (IdP) | Provide a cloud identity path: hosted Keycloak with a stable external issuer + non-dev secrets, or Entra ID (authority/audience/claims) per `docs/identity/entra-id.md` | Keycloak is an always-on OIDC dependency of Bank.Api/Edge/Governance/Bank.Web; the dev `localhost:8088` issuer + dev client secret don't survive a cloud deploy. |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | d63cb820940a | 2026-07-04T22:44:00Z | Needs-Fix | Azure app-deploy plan omitted the always-on Keycloak/Entra cloud identity path needed for authenticated core scenarios. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 517d97a89ec8 | 2026-07-04T22:55:00Z | Go-with-amendments | Identity path (hosted Keycloak stable issuer or Entra) now scoped; non-blocking Background DB-count wording nit, fixed post-review. |
| R3 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 43cf4f81af46 | 2026-07-04T23:35:00Z | Go | Final renumber to CS43/CS44 (2nd sibling collision) + Decisions genericized to reference OpenMeter CSs by role; no design change; CS43/CS44 free at HEAD. |

## Deliverables

- `azd`-based Azure Container Apps deployment for the whole Aspire app (`azure.yaml` / infra manifests via Aspire's azd integration).
- Azure Database for PostgreSQL Flexible Server provisioning + connection wiring (replacing the container Postgres).
- OTLP telemetry re-pointed to Azure Monitor / Azure Managed Grafana via `OTEL_EXPORTER_OTLP_ENDPOINT`.
- Secrets (DB passwords, OIDC client secrets, tokens) sourced from Azure Key Vault rather than inline parameters.
- A cloud identity path wired for the deployed services: either a hosted Keycloak with a stable external issuer or Entra ID (`Keycloak__Authority`/audience or the Entra equivalent per `docs/identity/entra-id.md`), so the authenticated core scenarios work in Azure.
- A deployment doc (`docs/deploy/azure.md`) covering `azd up`, the provisioned topology, the cloud identity setup, and local-vs-cloud differences.

## User-approval gates

- Explicit user approval is required before any live `azd up` that provisions billable Azure resources (Container Apps, PostgreSQL Flexible Server, Key Vault, Managed Grafana). Prefer authoring manifests + a `what-if`/dry-run over standing infra.

## Exit criteria

- `azd up` provisions the app to Azure Container Apps; the deployed app answers the **authenticated** core scenarios against the cloud identity provider (hosted Keycloak or Entra); local-vs-cloud differences (incl. identity) are documented.

## Risks + open questions

- **Cost.** Live Azure resources are billable — gate behind the user-approval gate; prefer manifests + dry-run over always-on infra.
- **Aspire→azd maturity** for this app's container set (the opt-in `opa`/`openfga`/`unleash` containers) generating clean ACA manifests.
- **Secrets management** — Key Vault wiring for the Postgres password parameter, OIDC client secrets, and engine tokens.
- **Cloud identity / issuer stability** — Keycloak stamps the issuer from its externally-reachable host; a hosted Keycloak needs a stable issuer URL (+ realm import), or the app switches to Entra ID. Getting this wrong breaks token validation for every authenticated service.
- **ClickHouse/Kafka not present** — intentionally, since OpenMeter is out of scope here; CS44 layers those on Azure later.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
