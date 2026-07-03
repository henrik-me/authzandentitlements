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
| Add collector + backends (grafana/otel-lgtm) | done | yoga-ae | Single bundled container in AppHost; OTLP 4317/4318 + Grafana 3000 |
| Wire ServiceDefaults OTel to the collector | done | yoga-ae | AppHost injects OTEL_EXPORTER_OTLP_ENDPOINT → lgtm OTLP into each instrumented service |
| Build baseline dashboards | done | yoga-ae | Grafana provisioning + service-health.json + request-rates.json; provisioning verified on Grafana 13.0.1 |
| Author observability docs | done | obs-docs | agent-id=obs-docs \| role=doc-author \| report-status=complete \| learnings=2 |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

### Design decisions (implementation, 2026-07-03)

- **D1 — Single `grafana/otel-lgtm:0.28.0` container, not five hand-wired containers.** The
  `grafana/otel-lgtm` image bundles the OTel Collector + Prometheus + Tempo + Loki + Grafana
  (all five deliverable components) pre-wired with datasources. It is the de-facto standard
  Aspire persistent-observability backend (Microsoft/community examples). Rationale: satisfies
  the deliverable's component list, is robust and runtime-verifiable, and avoids the fragility of
  five hand-wired containers + config files that cannot be fully verified in this dev-loop AppHost.
- **D2 — Pinned tag `0.28.0`** (latest stable on Docker Hub) for determinism, matching the repo's
  version-pinning convention. Persistent container lifetime + a `/data` volume so telemetry
  survives `aspire run` restarts.
- **D3 — Endpoints:** OTLP gRPC 4317 (`otlp-grpc`), OTLP HTTP 4318 (`otlp-http`), Grafana UI 3000
  (`grafana`, external). Grafana anonymous access enabled with org role Editor
  (`GF_AUTH_ANONYMOUS_ENABLED=true` + `GF_AUTH_ANONYMOUS_ORG_ROLE=Editor`) so the lab needs no login and
  Explore (Loki/Tempo) works out of the box; the default admin/admin cannot escalate because both the
  UI login form (`GF_AUTH_DISABLE_LOGIN_FORM=true`) and HTTP Basic Auth (`GF_AUTH_BASIC_ENABLED=false`)
  are disabled.
- **D4 — Service fan-out:** the AppHost injects `OTEL_EXPORTER_OTLP_ENDPOINT` = the lgtm OTLP
  endpoint into each instrumented service (bank-api, entitlements-service, edge-gateway, bank-web).
  ServiceDefaults already gates its OTLP exporter on that env var (no ServiceDefaults code change
  needed — the existing wiring now fans out to the persistent collector). Services `WaitFor` the
  stack. Telemetry home moves to Grafana ("beyond the dev-time Aspire dashboard", per the Goal);
  the Aspire dashboard retains resource/console view. Dual-export (dashboard + lgtm) is a possible
  follow-up.
- **D5 — Baseline dashboards** provisioned via bind-mount to
  `/otel-lgtm/grafana/conf/provisioning/dashboards/custom/` (otel-lgtm's documented custom-dashboard
  path): `service-health` (RED: rate/errors/latency + .NET runtime) and `request-rates` (by
  service/route/status). Queries use the OTel→Prometheus-normalised `http_server_request_duration_seconds_*`
  histogram and runtime metrics; Prometheus is otel-lgtm's default datasource.
- **LRN-014 triage:** CS12 introduces a real OTLP collector, the candidate triage home flagged by
  LRN-014 (empty-body 500 under `aspire run` — OTLP-export interaction). Verify whether routing
  service OTLP at the lgtm collector changes that behaviour; record findings at close-out.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
