# CS10 — Commercial / product entitlements

**Status:** active
**Owner:** yoga-ae
**Branch:** cs10/content
**Started:** 2026-07-03
**Closed:** —
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
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

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

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
