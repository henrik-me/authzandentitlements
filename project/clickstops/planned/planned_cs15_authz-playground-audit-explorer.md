# CS15 — AuthZ playground + audit explorer

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 5 — Product + playground
**Lane:** Product
**Depends on:** CS06, CS07, CS08, CS09, CS13

## Goal

Provide the side-by-side engine comparison surface plus an audit explorer.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 9f18bd88ec92 | 2026-07-02T19:47:54Z | Go-with-amendments | Fan-out deps are right; add CS12 if trace links must target Tempo/Grafana, otherwise mark trace link best-effort. |

## Deliverables

- AuthZ Playground: run one decision across all engines (result, latency, reason/explanation, trace link).
- Audit Explorer: filter/search events, replay a decision, show chain-verification status.

## Exit criteria

- A single decision fans out to all engines and renders comparable results; audit events are explorable + replayable.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Playground over PDP fan-out | pending | — | |
| Per-engine result rendering | pending | — | |
| Audit explorer | pending | — | |
| Replay + verify | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
