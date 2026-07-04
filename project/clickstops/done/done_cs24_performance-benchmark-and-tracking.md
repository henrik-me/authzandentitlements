# CS24 — Performance benchmark + tracking

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs24/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
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
| Close-out: docs + restart state | done | yoga-ae-c3 | WORKBOARD row removed; CONTEXT.md updated with CS24-done + next-claimable; benchmark README + docs/eval/performance-benchmarks.md are the restart surface |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | LEARNINGS.md updated (multi-round Copilot on new .NET CLI; FrameworkReference non-propagation); D2 caching + test-coverage gaps recorded as CS24-scoped follow-ups |

## Notes / Learnings

Implemented as a new zero-dependency `AuthzEntitlements.Benchmarks` console + test project (52 tests) plus an append-only `pdp.evaluate.duration` PDP histogram, a Grafana `pdp-performance` dashboard, and `docs/eval/performance-benchmarks.md`. Content PR #75 (squash `b8c2720`). Review: full GPT-5.5 rubber-duck Go + **6 Copilot rounds**, each a real fail-closed/robustness hardening (git-sha hang → dup-engine → dup-baseline → schemaVersion → probe-cancel → frozen JSON options + non-negative tolerance validation), all threads resolved, Copilot clean at head. Full solution `dotnet build` 0/0, `dotnet test` 868/0.

Follow-ups (CS24-scoped, non-blocking per plan-vs-impl GO): D2 "caching" was scoped to the cold-vs-warm steady-state split rather than explicit cache hit/miss scenarios; and coverage gaps remain for an end-to-end `--check`-exits-nonzero CLI test and automated Grafana JSON/PromQL validation.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T06:57:43Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — benchmark harness, identical scenarios per engine | match | The measured in-process engines all dispatch the shared `FintechScenarioCatalog.Scenarios` through the same runner path; live OPA/OpenFGA are documented self-skips rather than incomparable offline measurements. |
| D2 — p50/p95/p99 + throughput + cold/warm + caching | diverged | p50/p95/p99, throughput, and cold/warm are implemented, but caching is only represented implicitly by the cold-vs-warm steady-state split rather than explicit cache hit/miss scenarios. |
| D3 — persisted results + Grafana trend dashboard + regression alerts | match | Runs persist as JSON, live PDP latency trends are covered by the new `pdp.evaluate.duration` Grafana dashboard, and `RegressionDetector` plus `--check` provides a non-zero regression alert path. |
| Exit criteria — reproducible per engine; trends tracked; regressions alert | match | The harness is reproducible for the deterministic engines, trends are tracked via persisted runs plus live Grafana metrics, and warm-p95 regressions fail closed via `--check`. |

**Test coverage:** gaps — no end-to-end CLI test proving `--check` exits non-zero on a regression; no automated validation of Grafana JSON/PromQL; no cache-specific benchmark/test because caching is only implicit in cold/warm behavior.

**Outcome GO:** CS24 satisfies the core close-out criteria: identical measured scenarios for deterministic engines, persisted benchmark results, live trend dashboarding, and a regression-alert exit path are all present. The only substantive divergence is that "caching" was scoped to cold/warm steady-state behavior rather than explicit cache hit/miss measurement; this is worth recording but is not a blocking deliverable gap.
