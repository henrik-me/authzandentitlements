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

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 277890a23397 | 2026-07-02T19:47:54Z | Go | Sound as-is: CS02 gives services to instrument; stack can land early and future services can adopt it. |

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
