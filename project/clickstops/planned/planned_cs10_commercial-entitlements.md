# CS10 — Commercial / product entitlements

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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
| Model plans + entitlements | pending | — | |
| Add OpenFeature + Unleash | pending | — | |
| Implement metering + quotas | pending | — | |
| Enforce in API | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
