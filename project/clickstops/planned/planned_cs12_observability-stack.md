# CS12 — Persistent observability stack

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 4 — Observability + audit
**Lane:** Observability
**Depends on:** CS02

## Goal

Provide persistent observability beyond the dev-time Aspire dashboard.

## Deliverables

- OTel Collector + Prometheus + Loki + Tempo + Grafana in the AppHost.
- ServiceDefaults OTel wiring fanned out to the collector.
- Baseline Grafana dashboards (service health, request rates).

## Exit criteria

- Traces/metrics/logs flow to the stack; Grafana shows baseline dashboards.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add collector + backends | pending | — | |
| Wire ServiceDefaults OTel | pending | — | |
| Build baseline dashboards | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
