# CS44 — Azure deployment of OpenMeter metering

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Filed by:** yoga-ae-c2 on 2026-07-04 — third slice of the original CS27 split per user decision (2026-07-04). Deploys the CS43 OpenMeter stack to Azure on top of the CS27 `azd`/ACA deployment foundation.
**Depends on:** CS25, CS27, CS43
**Hold:** ⛔ HELD — do NOT apply a claim (`harness claim CS44 --apply`) or open a claim PR without explicit user confirmation. Cloud/Azure deployment is out of scope at this time. See the **Hold / claim gate** section below.

## Goal

Deploy the OpenMeter metering stack (from CS43) to Azure, provisioning managed equivalents for **all** its required infra (Kafka, ClickHouse, Redis, and a Postgres metadata store) and wiring them into the CS27 `azd`/Azure Container Apps deployment.

## Hold / claim gate

⛔ **This CS is HELD. Do not apply a claim (`harness claim CS44 --apply`) or open a claim PR until every precondition below is satisfied and a maintainer has explicitly lifted the hold.** (A default dry-run `harness claim` preflight/harvest scan is harmless.) Cloud / Azure (or any other cloud) deployment is **out of scope at this time**. This CS also depends on CS27 + CS43 being implemented first.

**Preconditions — all must be true before claiming:**

1. **Local validation first.** The current Aspire stack and the local OpenMeter stack (CS43) have been validated in detail locally and documented. No cloud/deployment work starts before this.
2. **Explicit user go-ahead.** A maintainer has explicitly confirmed cloud deployment is now in scope and lifted this hold (record who + when here when lifting).
3. **Observability warranted.** The additional observability/metering detail this work would introduce is confirmed as actually needed for the demo/lab — not speculative.
4. **Elevated review.** Registered HIGH-RISK in `harness.config.json` (`reviews.high_risk_clickstops`) → GPT-5.5-only reviews, no Sonnet fallback, 5–8 rounds ([REVIEWS.md](../../../REVIEWS.md) § 2.3).

**Guard / enforcement (layered):** (1) this `## Hold / claim gate` is the always-on contract — claiming CS44 requires reading this planned file, so the hold is unavoidable at claim time; (2) CS44 is registered HIGH-RISK in `harness.config.json` (`reviews.high_risk_clickstops`), mechanically raising the review bar; (3) `LEARNINGS.md` **LRN-071** (`status: open`, `claim_area: cs44`) is a before-claim backstop — it shows at the weekly `harness harvest` immediately and at `harness claim CS44` once ≥14 days stale (harvest v0.16.0 staleness-gates `claim_area` matches), and per the bounded-before-claim invariant a claim PR must not open while it is undispositioned. **To lift:** satisfy the preconditions, record the user confirmation above, flip LRN-071 (`status` + a `**Disposition:**`), and remove this ⛔ block.

## Background

- **CS43** brings full OpenMeter up **locally** (Kafka + ClickHouse via Aspire opt-in containers). **CS27** deploys the app to **Azure Container Apps** via `azd` (with the shipped lightweight metering). This CS is the intersection: OpenMeter running **on Azure**. It is intentionally sequenced last so the app cloud-deploy milestone (CS27) never blocks on metering infra.
- OpenMeter's metering core (per CS43, verified against the OpenMeter quickstart compose/config) needs Kafka + ClickHouse + **Redis** (sink-worker dedup) + **Postgres** (API metadata). These map onto Azure managed services: **Kafka → Azure Event Hubs** (Kafka-compatible protocol) or self-hosted Kafka on ACA; **ClickHouse →** ClickHouse Cloud or self-hosted on ACA (no first-party Azure ClickHouse); **Redis → Azure Cache for Redis**; **Postgres →** the CS27 Azure Database for PostgreSQL Flexible Server (add an `openmeter` logical DB), avoiding a second Postgres.
- The **CS25** TCO doc's Azure cloud-move section (`docs/eval/managed-vs-selfhost-tco.md`) is the sizing/cost input.
- **State-of-world probe (2026-07-04, F6):** `project/clickstops/{planned,active,done}/` contain CS ids up to CS40 are in use on `origin/main` (with sibling-held gaps at 35/38/39 being actively renumbered by concurrent orchestrators); CS43/CS44 were chosen with margin above the current max to avoid the live filing race; dep CS25 is in `project/clickstops/done/`; deps CS27 (rescoped) and CS43 are planned prerequisites filed in the same change and must land + be implemented before this CS.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Sequencing | Depends on the OpenMeter-local CS (OpenMeter exists locally) + CS27 (`azd`/ACA deploy exists) | Avoids coupling the app cloud-deploy milestone to metering infra; each lands independently first. |
| 2 | Kafka on Azure | Prefer Azure Event Hubs (Kafka protocol); fall back to self-hosted Kafka on ACA | Managed, lower ops; validate OpenMeter's Kafka-client/consumer-group compatibility, else self-host. |
| 3 | ClickHouse on Azure | Evaluate ClickHouse Cloud vs self-hosted-on-ACA; record the decision | No first-party Azure ClickHouse; document the cost/ops trade-off per CS25. |
| 4 | Scope | Metering-core infra only (matches the OpenMeter-local CS scope): Kafka + ClickHouse + Redis + Postgres | Redis (sink-worker dedup) and Postgres (API metadata) are required by the metering core, not optional; Svix + balance/billing/notification workers remain out of scope. |
| 5 | Redis + Postgres on Azure | Redis → Azure Cache for Redis; Postgres → reuse the CS27 Flexible Server via an `openmeter` logical DB | Avoids a second Postgres and treats Redis as a first-class dependency rather than an optional add-on. |
| 6 | Cost gating | Live provisioning gated behind explicit user approval | Event Hubs / ClickHouse / Redis are billable, potentially always-on. |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 040532abec1b | 2026-07-04T22:44:00Z | Needs-Fix | Inherited CS43 under-scope: Redis (sink-worker) and Postgres (API) are required metering-core infra on Azure, not optional. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 6ee91c30b351 | 2026-07-04T22:55:00Z | Go | Azure OpenMeter now requires Redis (Azure Cache) and Postgres (reuse CS27 Flexible Server) alongside Kafka and ClickHouse. |
| R3 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 930e8d65fe86 | 2026-07-04T23:35:00Z | Go | Final renumber to CS44 (2nd sibling collision) + Decisions genericized; design unchanged; deps CS25/CS27/CS43 acyclic; free at HEAD. |

## Deliverables

- `azd`/ACA (or bicep) manifests provisioning OpenMeter + its Azure-managed infra — Event Hubs / Kafka, ClickHouse, Azure Cache for Redis, and an `openmeter` logical DB on the CS27 Flexible Server — extending the CS27 deployment.
- OpenMeter configured for the Azure endpoints (managed Kafka/ClickHouse) with connection strings/secrets sourced from Azure Key Vault.
- A documented hosting decision for Kafka (= Event Hubs) and ClickHouse (Cloud vs ACA), with cost/ops notes fed back to the CS25 eval doc.
- Local-vs-Azure OpenMeter differences documented.

## User-approval gates

- Explicit user approval is required before any live `azd up` that provisions billable Azure metering infra (Event Hubs, ClickHouse, Redis). Prefer manifests + a `what-if`/dry-run over standing infra.

## Exit criteria

- OpenMeter meters usage end-to-end on Azure (managed Kafka + ClickHouse + Redis + Postgres), provisioned via `azd` on top of the CS27 deployment; the Kafka/ClickHouse/Redis hosting decisions and their costs are documented.

## Risks + open questions

- **Event Hubs compatibility.** OpenMeter's Kafka client + consumer-group usage against the Event Hubs Kafka surface — validate early; self-hosted Kafka on ACA is the fallback.
- **ClickHouse hosting.** No managed Azure ClickHouse → ClickHouse Cloud (third-party billing) vs a self-hosted stateful container on ACA (ops + storage burden).
- **Cost.** Always-on streaming infra is expensive; consider scale-to-zero / dev-sized SKUs and the user-approval gate.
- **Ordering dependency.** Requires both CS27 and CS43 to be implemented (not merely filed) first.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
