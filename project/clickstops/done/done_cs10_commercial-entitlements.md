# CS10 — Commercial / product entitlements

**Status:** done
**Owner:** yoga-ae
**Branch:** cs10/content
**Started:** 2026-07-03
**Closed:** 2026-07-03
**Phase:** 3 — Entitlements
**Lane:** Entitlements
**Depends on:** CS02

## Goal

Model commercial entitlements: plan tiers, module licensing, seats, feature gates, and usage quotas.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 43910442fa94 | 2026-07-02T19:47:54Z | Go | Blocker resolved; exit requires audit-ready events only and defers ingestion to CS13. |

## Deliverables

- Entitlements.Service; plans + modules (wire/FX/treasury), seat limits, feature gates via OpenFeature + Unleash container.
- Usage quotas with lightweight Postgres+OTel metering (full OpenMeter deferred to CS27).
- Enforcement hooks in Bank.Api.

## Exit criteria

- Feature/module/seat/quota checks gate behavior per tenant plan; decisions emit audit-ready events (Audit.Service ingests them in CS13).

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Model plans + entitlements | done | yoga-ae | agent-id=cs10-entitlements-service \| role=service-impl \| report-status=complete \| learnings=3 |
| Add OpenFeature + Unleash | done | yoga-ae | agent-id=cs10-entitlements-service \| role=service-impl \| report-status=complete \| learnings=3 (OpenFeature 2.14.0 in-memory default + config-gated real Unleash.Client 6.2.1 provider; Unleash container in AppHost via WithExplicitStart) |
| Implement metering + quotas | done | yoga-ae | agent-id=cs10-entitlements-service \| role=service-impl \| report-status=complete \| learnings=3 (OTel Meter `AuthzEntitlements.Entitlements` + Postgres UsageCounter, xmin-concurrency consume) |
| Enforce in API | done | yoga-ae | agent-id=cs10-bankapi-enforcement \| role=enforcement-impl \| report-status=complete \| learnings=0 (wire-module/high-value-feature/monthly-quota gates, fail-closed) |
| Close-out: docs + restart state | done | yoga-ae | Updated CONTEXT.md (CS10 completion paragraph + next-claimable); WORKBOARD row removed at close-out |
| Close-out: learnings + follow-ups | done | yoga-ae | Filed LRN-015 (advisory-lock capacity enforcement), LRN-016 (Aspire AppHost.csproj sub-agent ownership), LRN-017 (transient-503 vs business-deny + JsonIgnore sentinels); deeper integration/persistence coverage → CS17 |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck, cs10-pvi)
**Date:** 2026-07-03T19:35:00Z
**Outcome:** GO

Per-deliverable + exit-criteria outcome (all `match`):

| Plan item | Outcome |
|---|---|
| Entitlements.Service; plans + modules (wire/fx/treasury), seat limits, feature gates via OpenFeature + Unleash container | match — in-memory-default / config-gated-Unleash is an intentional operational divergence; deliverable satisfied |
| Usage quotas with lightweight Postgres + OTel metering | match — `UsageCounter` + `AuthzEntitlements.Entitlements` OTel meter |
| Enforcement hooks in Bank.Api | match — `POST /api/transactions` → wire-module / high-value-feature / monthly-quota; 402/403/429; 503 fail-closed |
| Exit criteria: feature/module/seat/quota gate behavior per tenant plan + audit-ready events | match — all four gates enforce (seat gates seat-assignment in Entitlements.Service); structured `EntitlementDecision` events emitted for all four |

**Test coverage:** `gaps` (140/140 pass). Noted gaps — no Entitlements.Service HTTP/EF integration tests, no persistence/concurrency tests for quota-consume retries + advisory-locked seat assignment, no audit-sink field assertions, no config-gated Unleash-provider test, no full `POST /api/transactions` DI+HTTP integration path. Mitigated by orchestrator runtime verification against Postgres 17 (all endpoints per-plan; quota limit enforcement; 30-way concurrent seat enforcement with 0 errors / no over-allocation; audit-log lower-case casing). Deeper automated integration/persistence coverage is a candidate for **CS17 (policy lifecycle + testing)**.
