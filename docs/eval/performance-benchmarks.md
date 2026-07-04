# Performance benchmarks (CS24)

This document describes how authorization performance is measured and tracked across engines over
time: the offline benchmark harness (`src/AuthzEntitlements.Benchmarks`), the persisted result
schema, the regression policy, and the live Grafana dashboard fed by a new PDP latency histogram.

## Goal

Measure and track authorization performance across every engine on an **apples-to-apples** basis,
and alert when a change regresses latency.

## Methodology

### Identical scenarios per engine

Every engine answers the **same** questions: the shared, engine-agnostic
`FintechScenarioCatalog.Scenarios` (the 22 fintech permit/deny cases used by the parity tests). A
scenario is authored once as an `AccessRequest` and dispatched unchanged to each provider, so
differences in the numbers reflect the engine, not the workload.

The default engine set is the four deterministic **in-process** engines — `reference`, `aspnet`,
`casbin`, `cedar` — constructed exactly as the Pdp test suite constructs them (parameterless
constructors, no Docker, no servers).

### Cold vs warm

Each engine runs a discarded **warmup** phase followed by a **measured** phase. The first measured
evaluation is recorded as the **cold** latency; the steady-state remainder forms the **warm**
distribution. Each evaluation is timed allocation-free with
`Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()`.

### Percentiles and throughput

Warm latency is summarised as count, min, max, mean, **p50/p95/p99**, and throughput. Percentiles use
**nearest-rank** on the ascending-sorted sample: rank = `ceil((p/100) * N)`, value at `rank - 1`
(clamped). Nearest-rank always returns an observed value — appropriate for the sub-millisecond
in-process latencies where interpolation would add noise. Throughput is `count / totalElapsedSeconds`
(ops/sec). Empty and single-element samples are handled without throwing.

> Absolute latencies are **environment-specific**. The durable signals are the dashboard trend and
> the relative regression check, not any one machine's raw milliseconds.

## How to run

```
dotnet run --project src/AuthzEntitlements.Benchmarks -- --iterations 20000
```

Key flags (see the project README for the full list): `--iterations`, `--warmup`, `--engines`
(`reference,aspnet,casbin,cedar,opa,openfga` or `all`), `--out`, `--baseline`, `--check`, `--help`.

## Result JSON schema

A run is persisted (camelCase, indented) to a timestamped file under `--out`
(default `benchmarks/results/`):

```jsonc
{
  "schemaVersion": 1,
  "timestampUtc": "2026-07-04T00:00:00.0000000+00:00", // ISO-8601 round-trip
  "gitSha": "abc1234",         // `git rev-parse --short HEAD`, or "unknown"
  "machineName": "…",
  "runtimeVersion": "…",       // RuntimeInformation.FrameworkDescription
  "iterations": 20000,
  "warmup": 1000,
  "engines": [
    {
      "engineName": "reference",
      "status": "measured",     // "measured" | "skipped"
      "skipReason": null,        // set when status = "skipped"
      "scenarioCount": 22,
      "coldMs": 0.0011,
      "warm": {
        "count": 19999,
        "minMs": 0.0001, "maxMs": 0.0039, "meanMs": 0.0004,
        "p50Ms": 0.0002, "p95Ms": 0.0023, "p99Ms": 0.0032,
        "throughputPerSec": 2544551.9
      }
    }
  ]
}
```

Loading fails **closed**: a missing file, empty content, malformed JSON, or a null document raises a
clear error and a non-zero exit rather than proceeding with a default.

## Regression policy

`--check` compares each engine's **warm p95** to the committed baseline
(`src/AuthzEntitlements.Benchmarks/baseline/pdp-latency-baseline.json`). An engine regresses only
when **both**:

1. **Relative:** current p95 > baseline p95 × (1 + tolerance), tolerance default **25%**.
2. **Absolute:** the increase > floor, default **0.10 ms**.

The absolute floor prevents a large *relative* swing on a sub-millisecond engine (a fraction of a
microsecond) from tripping the alert. Any regression exits the process non-zero — the CI "alert".
Engines present in only one run (an offline live engine, or an engine newly added since the baseline)
are not comparable and are ignored. Regenerate the baseline periodically by running the harness and
normalizing the machine-specific metadata fields.

## Live dashboard + the `pdp.evaluate.duration` metric

The PDP records a histogram `pdp.evaluate.duration` (unit `ms`) around the provider `Evaluate` call
on every decision, tagged with the same low-cardinality vocabulary as the decision counter:
`provider`, `action` (metric-normalized), `decision`, `reason`. Following the OpenTelemetry →
Prometheus exporter convention (dots → underscores, unit suffix, `_bucket` for histograms), it is
exported as `pdp_evaluate_duration_milliseconds_bucket`.

The **PDP Performance** dashboard
(`infra/observability/grafana/dashboards/pdp-performance.json`) trends latency percentiles and
decision throughput:

```promql
# p50 / p95 / p99 evaluate latency by provider
histogram_quantile(0.50, sum by (le, provider) (rate(pdp_evaluate_duration_milliseconds_bucket[$__rate_interval])))
histogram_quantile(0.95, sum by (le, provider) (rate(pdp_evaluate_duration_milliseconds_bucket[$__rate_interval])))
histogram_quantile(0.99, sum by (le, provider) (rate(pdp_evaluate_duration_milliseconds_bucket[$__rate_interval])))

# decision throughput by provider
sum by (provider) (rate(pdp_decisions_total[$__rate_interval]))
```

The dashboard uses the shared `${datasource}` Prometheus templating variable, matching the other
dashboards in `infra/observability/grafana/dashboards/`.

## Offline / self-skip behavior for live engines

The out-of-process engines `opa` and `openfga` require a running server. The harness attempts them
**only** when explicitly requested (`--engines all` or by name) and probes reachability first; when
the endpoint is unreachable it records a `skipped` status with a reason instead of failing. The
default run never requires Docker or a live server, and a live engine never fails the run — matching
the repo convention that live-engine tests self-skip offline.
