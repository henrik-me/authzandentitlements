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

## Deliverables

- Entitlements.Service; plans + modules (wire/FX/treasury), seat limits, feature gates via OpenFeature + Unleash container.
- Usage quotas with lightweight Postgres+OTel metering (full OpenMeter deferred to CS27).
- Enforcement hooks in Bank.Api.

## Exit criteria

- Feature/module/seat/quota checks gate behavior per tenant plan; decisions audited.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Model plans + entitlements | pending | — | |
| Add OpenFeature + Unleash | pending | — | |
| Implement metering + quotas | pending | — | |
| Enforce in API | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
