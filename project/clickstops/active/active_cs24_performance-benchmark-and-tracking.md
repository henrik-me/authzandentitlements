# CS24 — Performance benchmark + tracking

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs24/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 6 — Evaluation lab
**Lane:** Eval
**Depends on:** CS06, CS07, CS08, CS09, CS12

## Goal

Measure and track authorization performance across engines over time.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 840eca0aae97 | 2026-07-02T19:47:54Z | Go | Declared deps are sufficient; engine deps transitively cover CS05 and CS12 covers metrics and Grafana tracking. |

## Deliverables

- Benchmark harness running identical scenarios per engine.
- p50/p95/p99 latency + throughput; cold/warm + caching.
- Results persisted + Grafana trend dashboards; regression alerts.

## Exit criteria

- Reproducible benchmarks per engine; trends tracked; regressions alert.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Build benchmark harness | done | yoga-ae-c3 | `AuthzEntitlements.Benchmarks` console runs `FintechScenarioCatalog` per engine; in-process reference/aspnet/casbin/cedar, live opa/openfga probe-and-self-skip. agent-id=cs24-benchmark \| role=implementer \| report-status=complete \| learnings=2 |
| Capture latency/throughput | done | yoga-ae-c3 | Allocation-free `Stopwatch` timing; cold + warm p50/p95/p99 + throughput (nearest-rank) via `LatencyStatistics` |
| Persist + dashboard | done | yoga-ae-c3 | Runs persisted as camelCase JSON (`ResultStore`); `pdp.evaluate.duration` histogram + Grafana `pdp-performance.json` (p50/p95/p99 + throughput) |
| Regression alerts | done | yoga-ae-c3 | `RegressionDetector` warm-p95 vs committed baseline (25% rel + 0.10ms abs floor); `--check` exits non-zero on regression |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
