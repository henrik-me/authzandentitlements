# CS12 — Persistent observability stack

**Status:** active
**Owner:** yoga-ae
**Branch:** cs12/content
**Started:** 2026-07-03
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
