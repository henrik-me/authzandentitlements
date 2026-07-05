# CS43 — Full OpenMeter metering (local / Docker via Aspire)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Filed by:** yoga-ae-c2 on 2026-07-04 — split from the original CS27 ("Full OpenMeter metering + Azure deployment") per user decision (2026-07-04). This CS covers full OpenMeter metering running **locally** in Docker via Aspire opt-in containers; Azure deployment of OpenMeter is CS44, and Azure deployment of the app (with lightweight metering) is the rescoped CS27.
**Depends on:** CS10, CS12
**Hold:** ⛔ HELD — do NOT apply a claim (`harness claim CS43 --apply`) or open a claim PR without explicit user confirmation. Held pending detailed local validation of the current stack + demo/lab observability justification. See the **Hold / claim gate** section below.

## Goal

Add an **opt-in** OpenMeter metering provider (Kafka + ClickHouse + Postgres + Redis) behind the existing entitlements/quota seam, wired into the Aspire AppHost as opt-in containers that stay off the default deterministic, Docker-free path. The lightweight Postgres + OTel counter **remains the default**; OpenMeter is a config-gated alternative.

## Hold / claim gate

⛔ **This CS is HELD. Do not apply a claim (`harness claim CS43 --apply`) or open a claim PR until every precondition below is satisfied and a maintainer has explicitly lifted the hold.** (A default dry-run `harness claim` preflight/harvest scan is harmless.) This CS is local/Docker only (no cloud), but it adds heavy net-new infra (Kafka/ClickHouse/Redis), so it stays held until the current stack is validated and the demo/lab genuinely needs full metering. Cloud deployment of this stack is a separate CS (CS44) and is **out of scope at this time**.

**Preconditions — all must be true before claiming:**

1. **Local validation first.** The current Aspire stack has been validated in detail locally (build + full test suite + `aspire run` smoke of the real scenarios) and that validation is documented. No new infra work starts before this.
2. **Explicit user go-ahead.** A maintainer has explicitly confirmed this work is now in scope and lifted this hold (record who + when here when lifting).
3. **Observability warranted.** The additional metering/observability detail this work would introduce is confirmed as actually needed for the demo/lab — not speculative.
4. **Elevated review.** Registered HIGH-RISK in `harness.config.json` (`reviews.high_risk_clickstops`) → GPT-5.5-only reviews, no Sonnet fallback, 5–8 rounds ([REVIEWS.md](../../../REVIEWS.md) § 2.3).

**Guard / enforcement (layered):** (1) this `## Hold / claim gate` is the always-on contract — claiming CS43 requires reading this planned file, so the hold is unavoidable at claim time; (2) CS43 is registered HIGH-RISK in `harness.config.json` (`reviews.high_risk_clickstops`), mechanically raising the review bar; (3) `LEARNINGS.md` **LRN-070** (`status: open`, `claim_area: cs43`) is a before-claim backstop — it shows at the weekly `harness harvest` immediately and at `harness claim CS43` once ≥14 days stale (harvest v0.16.0 staleness-gates `claim_area` matches), and per the bounded-before-claim invariant a claim PR must not open while it is undispositioned. **To lift:** satisfy the preconditions, record the user confirmation above, flip LRN-070 (`status` + a `**Disposition:**`), and remove this ⛔ block.

## Background

- ADR 0005 (`docs/adr/0005-entitlements-via-openfeature-and-usage-metering.md`) chose commercial entitlements via OpenFeature plus a **lightweight Postgres `UsageCounter` + OTel meter** (`AuthzEntitlements.Entitlements`), and explicitly states "Full OpenMeter integration is explicitly deferred to CS27." This CS is the OpenMeter slice of the CS27 split and inherits that deferral.
- The quota arithmetic lives in `src/AuthzEntitlements.Entitlements.Service/Domain/QuotaDecision.cs` (pure allow/deny/remaining with `xmin` optimistic concurrency in the persistence layer); this CS swaps the metering store behind that seam, not the entitlement/quota contract.
- **OpenMeter's self-hosted OSS stack** (per `openmeterio/openmeter` `quickstart/docker-compose.yaml` + `quickstart/config.yaml`, verified 2026-07-04) runs several processes (`openmeter` API, `sink-worker`, `balance-worker`, `notification-service`, `billing-worker`, `openmeter-jobs`). The **minimal metering core** is the `openmeter` API + a `sink-worker`, and in the stock quickstart that core depends on **four** infra services: **Kafka + ClickHouse** (event stream + analytics store) **plus Postgres** (the `openmeter` API's metadata store — `depends_on: postgres`) **and Redis** (the `sink-worker`'s dedup store — `depends_on: redis`, config uses Redis dedupe). Only **Svix** and the `balance-worker`/`billing-worker`/`notification-service` are billing/notification extras this CS excludes. So the metering-only footprint is Kafka + ClickHouse + Postgres + Redis, not just Kafka + ClickHouse.
- The repo already has the opt-in external-service pattern in `src/AuthzEntitlements.AppHost/AppHost.cs`: `opa`, `openfga`, and `unleash` are `builder.AddContainer(...).WithExplicitStart()` with pinned image tags, kept off the default `aspire run` path, reusing the shared `postgres` server via per-service logical DBs. OpenMeter follows the same discipline — its Postgres metadata store can reuse the shared `postgres` server via an `openmeter` logical DB — but it adds **net-new** Kafka, ClickHouse, and Redis infra (the AppHost currently runs only `postgres` + `grafana/otel-lgtm`).
- **State-of-world probe (2026-07-04, F6):** `project/clickstops/{planned,active,done}/` contain CS ids up to CS40 are in use on `origin/main` (with sibling-held gaps at 35/38/39 being actively renumbered by concurrent orchestrators); CS43/CS44 were chosen with margin above the current max to avoid the live filing race; deps CS10 and CS12 are both in `project/clickstops/done/`.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Metering backend | Full OpenMeter (Kafka + ClickHouse) behind the existing metering seam | ADR 0005 deferred exactly this; keeps the entitlements/quota API stable while swapping the meter store. |
| 2 | Wiring | Aspire AppHost opt-in containers: `.WithExplicitStart()`, pinned tags, no hard `WaitFor` on the deterministic default | Mirrors `opa`/`openfga`/`unleash` (ADR 0003 Docker-free-default discipline); `aspire run` stays deterministic + Docker-free. |
| 3 | OpenMeter scope | Metering core only (`openmeter` API + `sink-worker`; ingest usage events + query aggregated usage) — not billing/notifications | Core infra footprint is Kafka + ClickHouse + Postgres (API metadata) + Redis (sink-worker dedup); Svix + `balance-worker`/`billing-worker`/`notification-service` are out of scope. A Postgres/Redis-free config would have to be verified before dropping either. |
| 4 | Default provider | In-process lightweight metering stays the default; OpenMeter is config-gated opt-in | Determinism + no Docker on the default path; parity with the flag-provider pattern (in-memory default, Unleash opt-in). |
| 5 | Cloud deploy | Out of scope — see the OpenMeter-on-Azure CS | This CS is local/Docker only. |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 244b2106fce4 | 2026-07-04T22:44:00Z | Needs-Fix | OpenMeter metering core also needs Postgres (API) and Redis (sink-worker dedup); Goal said replace but default stays lightweight. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 4d9395b90b92 | 2026-07-04T22:55:00Z | Go-with-amendments | Postgres and Redis now scoped as core infra; Goal reworded to opt-in. Decision 1 Kafka+ClickHouse shorthand left as noted. |
| R3 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 6d6e971f8071 | 2026-07-04T23:35:00Z | Go | Final renumber to CS43 (2nd sibling collision) + Decisions genericized (sibling by role); design unchanged; deps CS10/CS12; free at HEAD. |

## Deliverables

- OpenMeter (`openmeter` API + `sink-worker`) + its required infra — Kafka, ClickHouse, Redis, and a Postgres metadata DB (reusing the shared `postgres` server via an `openmeter` logical DB) — wired into the Aspire AppHost as opt-in `.WithExplicitStart()` containers with pinned image tags, off the default `aspire run` path.
- A metering provider that emits usage events to OpenMeter behind the existing entitlements/quota seam, config-gated so the in-process lightweight counter remains the default.
- Usage flows end-to-end locally: entitlement/quota enforcement hook → OpenMeter ingest (Kafka) → ClickHouse aggregation → queryable usage.
- A developer doc covering how to enable and run OpenMeter locally and the in-process-vs-OpenMeter differences.
- Tests: at least the config-gating/provider-selection path (default stays in-process) covered; the deterministic default suite stays green with no Docker requirement.

## User-approval gates

- None beyond normal review — local/Docker only; no billable cloud resources are provisioned by this CS.

## Exit criteria

- With OpenMeter enabled (its Kafka + ClickHouse + Redis + Postgres stack up), usage is metered end-to-end through OpenMeter (Kafka → ClickHouse) and is queryable; with it disabled (the default), the in-process path stays deterministic + Docker-free and the test suite is green.

## Risks + open questions

- **Footprint.** Kafka + ClickHouse + Redis are net-new infra (plus an OpenMeter Postgres DB on the shared server), heavier than the existing opt-ins; they must stay strictly off the default path (no `WaitFor` on default resources).
- **Postgres/Redis necessity.** The stock quickstart `sink-worker` requires Redis (dedup) and the `openmeter` API requires Postgres; dropping either needs a verified alternate OpenMeter config, otherwise both are in-scope.
- **Seam choice.** Where to intercept metering (usage-event emission vs. quota read) and how OpenMeter's aggregation maps onto the current `QuotaDecision` arithmetic and unlimited/bounded semantics.
- **Startup ordering.** Kafka/ClickHouse health must gate `openmeter`/`sink-worker` startup; image tags pinned for determinism.
- **Query surface.** Whether the entitlements service reads remaining quota from OpenMeter live or keeps a local projection.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
