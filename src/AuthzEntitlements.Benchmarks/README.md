# AuthzEntitlements.Benchmarks

A reproducible performance benchmark harness for the authorization PDP. It runs the **identical**
shared fintech scenario catalog (`FintechScenarioCatalog.Scenarios`) through each authorization
engine, measures per-evaluation latency and throughput, persists the run as JSON, and can compare a
run against a committed baseline to alert on regressions.

> **Absolute latencies are environment-specific.** The numbers you get depend on your CPU, load,
> and runtime build. The durable signal is the **trend** over time (the Grafana dashboard) and the
> **relative** regression check — not any single machine's raw milliseconds.

## Running

```
dotnet run --project src/AuthzEntitlements.Benchmarks -- --iterations 20000
```

### Flags

| Flag | Default | Meaning |
|------|---------|---------|
| `--iterations <n>` | `10000` | Measured evaluations per engine. |
| `--warmup <n>` | `1000` | Discarded warmup evaluations per engine (JIT/allocation settling). |
| `--engines <csv>` | the 4 in-process engines | `reference,aspnet,casbin,cedar,opa,openfga` or `all`. |
| `--out <dir>` | `benchmarks/results` | Directory the timestamped result JSON is written to. |
| `--baseline <path>` | the committed baseline | Baseline run used by `--check`. |
| `--check` | off | Compare the run to the baseline; **exit non-zero on a regression**. |
| `--help`, `-h` | — | Print usage and exit 0. |

Every value-taking flag rejects a missing value or a following `-flag` with a clear stderr message
and exit code `2` (fail closed).

## Cold vs warm

Each engine runs a discarded **warmup** phase, then a **measured** phase. Within the measured phase:

- **Cold** — the latency of the *first* measured evaluation (records the not-yet-fully-hot path).
- **Warm** — the steady-state distribution over the remaining measured evaluations (p50/p95/p99,
  min/max/mean, throughput).

Both phases cycle through the scenario catalog round-robin, so with `--iterations` ≥ the scenario
count every scenario is exercised.

## Percentile method

Latency percentiles use **nearest-rank** on the ascending-sorted sample: for percentile `p` over `N`
samples the rank is `ceil((p/100) * N)` and the value is the element at `rank - 1` (clamped). This
always returns an actually-observed value — a good fit for sub-millisecond in-process latencies where
interpolation adds noise. Throughput is `count / totalElapsedSeconds` (ops/sec). Empty and
single-element samples are handled without throwing.

## Regression detection (`--check`)

`--check` loads the baseline run and compares each engine's **warm p95** against it. An engine is
flagged as regressed only when **both** hold:

1. **Relative:** current p95 exceeds baseline p95 by more than the tolerance (default **25%**).
2. **Absolute:** the increase exceeds the floor (default **0.10 ms**).

The absolute floor suppresses noise on sub-millisecond in-process engines. Any regression makes the
process exit non-zero — that is the "alert". Engines present in only one run (e.g. an offline live
engine, or a newly added engine absent from the baseline) are **not comparable** and are ignored.

The committed baseline lives at `baseline/pdp-latency-baseline.json`. Regenerate it by running the
harness locally and normalizing the machine-specific metadata; its absolute numbers are
environment-specific and exist only to give `--check` something to compare against.

## Live engines self-skip offline

The out-of-process engines `opa` and `openfga` require a running server. They are **only** attempted
when explicitly requested (`--engines all` or by name) and even then **self-skip** (recording a
`skipped` status + reason) when the endpoint is unreachable. The default run never touches them, and
a live engine never fails the run — matching the repo's "live-engine tests self-skip offline"
convention.

## Live dashboard metric

Beyond the offline harness, the PDP now emits a `pdp.evaluate.duration` histogram (unit `ms`) per
decision, tagged `provider`/`action`/`decision`/`reason`. It exports to Prometheus as
`pdp_evaluate_duration_milliseconds_bucket` and powers the **PDP Performance** Grafana dashboard
(`infra/observability/grafana/dashboards/pdp-performance.json`). See
[`docs/eval/performance-benchmarks.md`](../../docs/eval/performance-benchmarks.md) for the full
methodology, JSON schema, PromQL, and regression policy.
