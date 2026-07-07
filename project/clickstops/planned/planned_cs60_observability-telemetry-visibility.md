# CS60 — Observability telemetry visibility: fix empty Grafana + dual-export to the Aspire dashboard

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** omni-ae on 2026-07-06 — surfaced by the maintainer: the Aspire dashboard shows only console logs (no structured logs / traces / metrics) AND the Grafana dashboards are also empty. A live `aspire run` reproduction (this session) proved OTLP delivery works but the persistent-collector wiring is unstable and unverified.
**Depends on:** none

## Goal

Make operational telemetry (structured logs, traces, metrics) reliably **visible to a developer** during `aspire run`, and add an automated guard so it cannot silently regress again. Two visible outcomes:

1. **Fix empty Grafana** — the CS12 `grafana/otel-lgtm` dashboards must show data after driving traffic, deterministically, on every clean run.
2. **Dual-export to the Aspire dashboard** (maintainer request) — telemetry must ALSO appear in the Aspire dashboard's Structured logs / Traces / Metrics tabs, not only console logs, so a developer always has a reliable view even if the persistent Grafana stack is not up.

Scope is **operational telemetry only**. The tamper-evident authorization **audit** pipeline (CS13) is a separate concern and is out of scope.

## Background

Reported symptom: the Aspire dashboard shows only console logs (no structured logs, traces, or metrics), and the Grafana (CS12 `otel-lgtm`) dashboards are also empty.

**Why the Aspire dashboard shows only console logs (intended, CS12).** Aspire normally auto-injects `OTEL_EXPORTER_OTLP_ENDPOINT` into every project pointing at the **dashboard's** in-memory OTLP receiver. CS12 **overrides** that env var on all 7 services (`AppHost.cs` per-service `.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)`) to repoint telemetry at the persistent `grafana/otel-lgtm` collector (`AppHost.cs:41-43`). The .NET OTLP exporter targets exactly ONE endpoint (`ServiceDefaults/Extensions.cs:83-88`, gated on that env var), so structured logs/traces/metrics all leave for the collector and none reach the dashboard; console logs still show because Aspire captures stdout/stderr directly. This is documented (`docs/observability/observability-stack.md:109-114`: "the home of telemetry moves to Grafana … dual-export … is not implemented here").

**But Grafana is ALSO empty — a real, never-verified delivery/visibility bug.** Live reproduction findings (this session, a clean `dotnet run` of the AppHost with Docker up):

- **OTLP delivery works.** All 7 services exported telemetry and appeared as Prometheus `job` labels in the collector (`audit-service, authz-pdp, bank-api, bank-web, edge-gateway, entitlements-service, governance-service`). So the OTLP-endpoint resolution (`ReferenceExpression.Create($"http://{host}:{port}")` over a dynamic proxied container endpoint) is **NOT** broken — consistent with CS58, where the equivalent dynamic `bankApi.GetEndpoint("http")` reference resolves and routes correctly.
- **Collector-instance instability from `ContainerLifetime.Persistent` + dynamic ports.** The `observability` container uses `.WithLifetime(ContainerLifetime.Persistent)` (`AppHost.cs:13`) with dynamic host ports for OTLP (`.WithEndpoint(targetPort:4317,name:"otlp-grpc")`) and Grafana. Observed live: **multiple `otel-lgtm` containers accumulate** (two leftover with different config hashes before the run; a second appeared mid-run; **both survived AppHost shutdown**). Multiple collector instances on random ports create **split-brain**: services export to the instance Aspire wired *this* run, but the Grafana a developer opens (or a stale persistent instance from a prior run/checkout) can be a **different** instance → empty dashboards. This is the leading root cause.
- **Metric names look correct.** The dashboards query `http_server_request_duration_seconds_count` etc.; the standard OTLP→Prometheus translation of `http.server.request.duration` (unit `s`) with `add_metric_suffixes` yields exactly that name, so a name mismatch is unlikely (confirm in task 1, not assumed).
- **No automated verification.** CS12 verified the stack "standalone"; CS32/LRN-014 only proved OTLP export does not *cause 500s* and explicitly left "definitive confirmation via a full `aspire run`" as an open follow-up (`docs/observability/aspire-run-500-triage.md` §"Remaining verification"). Nobody ever asserted telemetry *arrives*, so this regressed silently. The CS57/CS58 e2e assert service health/200s only.

Related wiring facts: OTLP export is gated on `OTEL_EXPORTER_OTLP_ENDPOINT` (`ServiceDefaults/Extensions.cs:83`); custom PDP/governance/gateway/entitlements meters are `AddMeter`'d per service, and baseline HTTP metrics come from `AddAspNetCoreInstrumentation()` — so ALL metrics depend on this single delivery path.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Confirm before fixing | **Task 1 = a clean, single-collector `aspire run` reproduction**: remove all leftover `otel-lgtm` containers, boot once, drive authenticated + health traffic, and capture (a) the injected `OTEL_EXPORTER_OTLP_ENDPOINT`, (b) the collector's actual `job`/`http_server_*` series, (c) whether the Grafana endpoint the resource exposes is the same instance receiving telemetry, (d) whether the Aspire dashboard OTLP endpoint is knowable in-AppHost **and whether a deterministic/programmatic path exists to verify the dashboard actually received/stored telemetry** (knowing the ingest endpoint is not the same as verifying stored data). Do not fix blind. | Live digging proved delivery works and the symptom is instance/visibility, not export. The exact user-facing trigger (split-brain vs. stale instance vs. no-traffic) must be pinned on a clean run before choosing the fix, so we fix the real cause and not a guess; the dashboard-verification path must be known before we can assert on it (Decision 4). |
| 2 | Collector determinism | **Make the collector a single, deterministic instance the services and Grafana always share.** Preferred: pin the observability OTLP + Grafana to **fixed host ports** (the Keycloak `localhost:8088` pattern, `AppHost.cs:96`) and reconsider `ContainerLifetime.Persistent` (or make reuse robust) so runs/checkouts do not accumulate colliding instances. Exact mechanism finalized from task-1 evidence. | The observed split-brain/accumulation of persistent `otel-lgtm` containers on random ports is the leading root cause of empty Grafana. A single deterministic collector guarantees services and the viewed Grafana share one instance. |
| 3 | Dual-export | **Export telemetry to BOTH the Aspire dashboard and the lgtm collector.** Recommended shape: stop overriding `OTEL_EXPORTER_OTLP_ENDPOINT` (let Aspire keep it pointed at the dashboard, restoring dashboard telemetry) and drive the lgtm collector via a **separate** exporter — either a second OTLP exporter registered in `ServiceDefaults` reading a distinct env var (e.g. `OTLP_LGTM_ENDPOINT`), or a collector-side fan-out. Final mechanism chosen in the plan/review after task 1, since the .NET OTLP exporter env var targets a single endpoint. | This is the maintainer's explicit request and also **mitigates** the Grafana instability: with the dashboard always receiving telemetry, a developer has a reliable view regardless of the persistent-stack state. Keeps the persistent Grafana for cross-session history. |
| 4 | Regression guard | **Extend the CS57/CS58 `RUN_ASPIRE_E2E` e2e to assert telemetry actually lands.** The **automated** assertion queries the **collector's** Prometheus for a service `job` / `http_server_*` series after driving traffic (deterministic) and fails if absent. Aspire-dashboard visibility is verified via a supported programmatic path **only if one exists** (identified in task 1); otherwise it is a **documented manual exit check**, not an automated assertion. | The missing guard is exactly why this regressed silently. A collector-arrival assertion (not just service 200s) closes LRN-014's outstanding "full-run confirmation" and prevents recurrence; the dashboard tab has no guaranteed programmatic read path, so it must not be over-asserted. |
| 5 | Scope boundary | **Operational-telemetry visibility only.** No authz-decision, PDP-engine, or Audit.Service hash-chain changes. Docs (`observability-stack.md`) updated to match. Splitting into two CSs (fix-delivery vs. dual-export) is allowed if review prefers, but they share the same OTLP wiring surface so one CS avoids overlapping edits. | Keeps the change focused and low-risk; the audit pipeline (CS13) is a separate concern. Documents the one-vs-two-CS tradeoff for the reviewer. |

## Deliverables

- **Task-1 reproduction note** — a short findings record (in the CS file `## Notes` and/or `docs/observability/`) capturing the confirmed root cause from a clean single-collector `aspire run`: injected endpoint value, collector series present after traffic, the container-instance/Grafana mapping, and whether metric names match. This gates decisions 2–3.
- **`src/AuthzEntitlements.AppHost/AppHost.cs`** — collector-determinism wiring (fixed OTLP/Grafana host ports and/or persistent-lifetime reconsideration per Decision 2) + dual-export wiring (Decision 3), keeping the deterministic no-Docker default path intact (opt-in engines untouched).
- **`src/AuthzEntitlements.ServiceDefaults/Extensions.cs`** — only if dual-export is implemented as a second OTLP exporter; the primary `OTEL_EXPORTER_OTLP_ENDPOINT` gate is preserved.
- **e2e test** — extend `tests/AuthzEntitlements.E2E.Tests` (opt-in `RUN_ASPIRE_E2E=1`) with a telemetry-arrival assertion (collector series present + dashboard OTLP received), plus the Node wrapper skip-green behavior when Docker is down.
- **Stale-collector cleanup + runbook** — a documented step (and, where feasible, a preflight/test helper) to remove or ignore pre-CS60 leftover/duplicate `otel-lgtm` containers so existing developer machines converge to a single visible collector. Without it, already-accumulated persistent containers keep causing split-brain even after the wiring fix.
- **Docs** — update `docs/observability/observability-stack.md` (dual-export now implemented; fixed ports; the verification step) and cross-link the resolved LRN-014 follow-up.
- **Learning** — a new `LEARNINGS.md` entry recording: (a) OTLP delivery was proven working (endpoint resolution fine); (b) `ContainerLifetime.Persistent` + dynamic ports caused collector accumulation/split-brain; (c) the missing telemetry-arrival e2e assertion.

## User-approval gates

- **Dual-export mechanism (Decision 3)** and **whether to drop `ContainerLifetime.Persistent` (Decision 2)** — surface the chosen approach for confirmation before implementing, since both change the default `aspire run` observability topology the maintainer interacts with.
- No secrets, no cloud/deploy surface (that stays with CS27/CS44).

## Exit criteria

- On a **clean** `aspire run` (single collector, no leftover containers), after driving a little traffic: the CS12 **Service Health + Request Rates Grafana dashboards show data**, AND the **Aspire dashboard** Structured-logs / Traces / Metrics tabs show data (dual-export) — both verified live.
- The `RUN_ASPIRE_E2E` e2e **asserts telemetry arrival** and fails when it is absent (verified both directions: passes with wiring, fails when export is broken).
- `dotnet build` 0/0; default `dotnet test` green with the e2e skipped; `harness lint` green; LF/no-BOM.
- `docs/observability/observability-stack.md` matches the shipped behavior; the LRN-014 "full-run confirmation" follow-up is closed; a new learning is filed.
- No authz/PDP/audit behavior change; the no-Docker deterministic default path (opt-in engines off the critical path) is unchanged.

## Risks + open questions

- **Fixed-port collision.** Pinning the collector's OTLP/Grafana host ports (Decision 2) risks a port clash if two `aspire run` instances run concurrently (the same tradeoff Keycloak's fixed 8088 already accepts). Document it; it is a dev-loop backend.
- **Dual-export overhead.** Exporting every signal twice doubles export work; negligible for a dev loop but note it. The dashboard OTLP endpoint carries an auth header (`OTEL_EXPORTER_OTLP_HEADERS`) Aspire injects — the second exporter/collector fan-out must carry it.
- **Persistent-lifetime removal.** Dropping `ContainerLifetime.Persistent` loses cross-session telemetry history (a CS12 goal). Weigh determinism vs. persistence in task 1 + review; a middle path (persistent but fixed-port, single instance) may satisfy both.
- **One CS vs. two.** Fix-delivery (bug) and dual-export (enhancement) could split; kept together here because they edit the same OTLP wiring. Reviewer may request a split.
- **Environmental confound.** Multiple repo checkouts share Docker; deterministic persistent-container names can collide across checkouts. Task 1 must reproduce in isolation to avoid mis-attributing cross-checkout interference to the code.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs60-plan-review (rubber-duck) | b7802f0011b1 | 2026-07-07T00:18:22Z | Go-with-amendments | Code claims verified vs repo; amended Decision 1/4 (collector-arrival = automated e2e, dashboard visibility = manual/if-supported) + added stale-container cleanup deliverable. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
