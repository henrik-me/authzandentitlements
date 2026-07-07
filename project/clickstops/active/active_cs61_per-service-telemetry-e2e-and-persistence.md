# CS61 — Per-service telemetry e2e + verified persistent telemetry disk

**Status:** active
**Owner:** omni-ae
**Branch:** cs61/content
**Started:** 2026-07-07
**Closed:** —
**Filed by:** omni-ae on 2026-07-06 — maintainer follow-up to CS60: (1) the telemetry-arrival e2e must verify telemetry is pushed for **each** service/app, not just an aggregate; (2) ensure the telemetry disk is persistent. A live two-run reproduction (this session) confirmed all 7 services deliver and that the `/data` volume persists telemetry across container recreation.
**Depends on:** none (CS60 shipped the dual-export + single-collector wiring this hardens)

## Goal

Close two gaps the maintainer identified in the CS60 observability work:

1. **Per-service telemetry verification.** The CS60 `TelemetryArrivalE2ETests` only asserts an aggregate `sum(http_server_request_duration_seconds_count) > 0` and that `>= 2` services appear. Strengthen it so it verifies telemetry is actually pushed to the collector for **every** instrumented service/app (all 7 project services).
2. **Persistent telemetry disk — verified + guarded.** Confirm (and keep confirmed) that the telemetry disk survives across `aspire run` restarts even though CS60 dropped `ContainerLifetime.Persistent`. A live two-run test proved it already works via the named `/data` volume; add a regression guard so the `/data` mount can't be silently removed, and document the verified behavior.

Tests + docs only. No change to the OTLP wiring or the collector's run-scoped lifetime (both shipped + verified in CS60).

## Background

CS60 fixed empty-Grafana/empty-dashboard by dropping `ContainerLifetime.Persistent` (single collector per run → no split-brain), dual-exporting to the Aspire dashboard + the lgtm collector, and adding `TelemetryArrivalE2ETests`. Maintainer follow-up flagged two things.

**Live findings (this session, two-run reproduction):**

- **Per-service delivery works.** After driving traffic, the collector's Prometheus `job` label held **all 7** project services (`audit-service, authz-pdp, bank-api, bank-web, edge-gateway, entitlements-service, governance-service`). But the shipped e2e asserts only `serviceJobs.Length >= 2` (`TelemetryArrivalE2ETests.cs`), so a service that silently stops exporting would not fail it.
- **The telemetry disk IS persistent.** The `observability` container's `/data` mount holds every store (`prometheus/`, `loki/`, `tempo/`, `grafana/`, `pyroscope/`, ~205 MB). A definitive two-run test: run A drove traffic → `sum(http_server_request_duration_seconds_count)` = **287**; the AppHost was stopped and the container **removed** (id `abcf8a4c43eb`); run B booted a **fresh** container (id `9823fa2be3d2`) reusing the named volume and, **before any new traffic**, reported the same **287**. So run A's telemetry survived container recreation — the named `authz-observability-data` volume at `/data` is the persistent disk, and dropping the persistent container **lifetime** did not break persistence. (Aside: a *hard kill* of the AppHost orphans the run-scoped container instead of removing it — normal Ctrl+C removal is assumed; the CS60 stale-collector cleanup runbook covers orphans.)

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Per-service assertion | Strengthen `TelemetryArrivalE2ETests` to drive `/alive` traffic to **all 7** project services (asserting each `/alive` returns success, so a broken target fails rather than silently generating error telemetry) and **poll `sum by (job)(http_server_request_duration_seconds_count)` until every one of the 7 services has a `job` series with count > 0** — replacing the aggregate `> 0` early-break + `>= 2` check. | The maintainer wants telemetry verified "for each service/sub-service/app". Polling for all 7 per-`job` non-zero samples is the definitive per-service check, fails closed if any one service stops exporting, and absorbs OTLP export/scrape lag (no early-break-on-aggregate flakiness). |
| 2 | Persistence mechanism | **Keep the named `/data` volume as the persistent disk; do NOT restore `ContainerLifetime.Persistent`.** Persistence is already provided by the volume (verified cross-run: 287 survived container recreation); restoring the persistent container **lifetime** would reintroduce the CS60 split-brain. | Verified empirically that the volume — not the container lifetime — is what persists telemetry, so the CS60 split-brain fix and durable telemetry coexist. "Persistent disk" = the volume. |
| 3 | Persistence guard | Add a Docker-free **AppHost app-model smoke test** asserting the `observability` container declares a **`ContainerMountAnnotation` with `Source == "authz-observability-data"`, `Target == "/data"`, `Type == Volume`, and not read-only** — not merely "some mount at `/data`" (which could pass after renaming/replacing the volume). | The `du`/two-run proof is manual; a fast app-model assertion (à la CS50) makes the *specific named, writable* `/data` telemetry disk a mechanical invariant, so a future edit can't silently drop or swap it. Aspire exposes `ContainerMountAnnotation.Source/Target/Type/IsReadOnly`. |
| 4 | Docs | Document the verified cross-run persistence (volume-based, survives container recreation) in `observability-stack.md`, and that the per-service e2e now guards each service. | Make the verified behavior and its guard discoverable; correct any impression that dropping the persistent lifetime lost history. |
| 5 | Scope | **Tests + docs only.** No OTLP-wiring, exporter, or container-lifetime change. | The CS60 wiring is shipped + verified; this CS only hardens verification + documents persistence. |

## Deliverables

- **`tests/AuthzEntitlements.E2E.Tests/TelemetryArrivalE2ETests.cs`** — drive `/alive` (asserting success) to all 7 project services and **poll `sum by (job)(http_server_request_duration_seconds_count)` until every one of the 7 services has a `job` series with count > 0**; keep an overall fail-closed behavior (absent/zero → fail).
- **`tests/AuthzEntitlements.AppHost.Tests/*`** — a new Docker-free app-model smoke test asserting the `observability` container's `ContainerMountAnnotation` has `Source == "authz-observability-data"`, `Target == "/data"`, `Type == Volume`, and is writable (the persistent telemetry disk).
- **`docs/observability/observability-stack.md`** — a short "Persistence (verified)" note: the `/data` named volume survives container recreation (two-run proof), so history persists without the persistent container lifetime; the per-service e2e guards each service.
- **Learning (optional)** — `LEARNINGS.md` LRN-093 only if the cross-run-persistence + orphan-on-hard-kill finding is judged durable; otherwise fold into the docs.

## User-approval gates

- None expected (tests + docs only). If the maintainer specifically wants the **container** persistent (reused across runs) rather than volume-based persistence, that reintroduces the CS60 split-brain and would be a separate, gated decision — surface it rather than silently choosing it.

## Exit criteria

- `RUN_ASPIRE_E2E=1` → the strengthened `TelemetryArrivalE2ETests` **passes**, polling until all 7 services have a non-zero `http_server_request_duration_seconds_count` `job` series (verified live); it fails closed if any service is missing or its `/alive` does not succeed.
- The new AppHost app-model smoke test asserts the named `authz-observability-data` volume mounted writable at `/data` (Type Volume) and passes in the Docker-free default `dotnet test`.
- `dotnet build` 0/0; full default `dotnet test` green; `harness lint` 23/0; LF/no-BOM.
- `observability-stack.md` documents the verified persistence; no OTLP-wiring/lifetime change.

## Risks + open questions

- **Per-service `/alive` reachability.** All 7 services expose an `http` endpoint (CS56) and `/alive` in Development (CS58), so `app.CreateHttpClient(service, "http")` + `/alive` should serve 200 for each; if a service lacks `/alive`, drive its root or another instrumented path instead.
- **e2e runtime.** Driving all 7 services adds a little time; the test stays within the existing ~5–6 min cap and opt-in `RUN_ASPIRE_E2E` gate.
- **Hard-kill orphan.** A hard kill of the AppHost orphans the run-scoped collector (leaves it running) instead of removing it; not a persistence bug (data is in the volume) and covered by the CS60 cleanup runbook — noted, not fixed here.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs61-plan-review (rubber-duck) | a6824ecfdfb6 | 2026-07-07T06:04:15Z | Go-with-amendments | Code claims verified (incl. Aspire mount API); amended: persistence test asserts named volume/type/writable; per-service e2e polls all 7 non-zero job samples + asserts /alive success. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Strengthen `TelemetryArrivalE2ETests` — drive `/alive` (assert success) to all 7 services; poll until every service has a non-zero `http_server` job series | done | omni-ae | agent-id=omni-ae \| role=test \| report-status=complete \| learnings=0 — Decision 1; **passes live** (1m7s, clean slate) |
| Add AppHost app-model smoke test asserting the named `authz-observability-data` volume mounted writable at `/data` (Type Volume) | done | omni-ae | agent-id=omni-ae \| role=test \| report-status=complete \| learnings=0 — Decision 3; AppHost.Tests 5→6, Docker-free |
| Docs — `observability-stack.md` "Persistence (verified)" note (volume survives container recreation; per-service e2e guard) | done | omni-ae | agent-id=omni-ae \| role=docs \| report-status=complete \| learnings=0 — Decision 4 |
| Close-out: docs + restart state | pending | omni-ae | Update WORKBOARD + CONTEXT.md after merge so a fresh agent restarts from actual state |
| Close-out: learnings + follow-ups | pending | omni-ae | File/disposition any learnings; open follow-up CSs for unresolved gaps |

## Notes / Learnings

### Implementation + verification (omni-ae, 2026-07-07)

- **Per-service e2e:** `TelemetryArrivalE2ETests` now drives `/alive` to all 7 project services (asserting each `/alive` succeeds) and polls `sum by (job)(http_server_request_duration_seconds_count)` until **every** service has a non-zero `job` series (replacing the old aggregate `> 0` + `>= 2`). Verified live on a **clean slate** (volume wiped): passes in 1m7s with all 7 services present.
- **Persistence — verified, not changed.** A two-run live test proved the named `authz-observability-data` volume at `/data` persists telemetry across container recreation (run A count 287 → survived into a fresh run-B container before new traffic). So dropping `ContainerLifetime.Persistent` (CS60) did **not** break persistence — the volume does the persisting. **Decision (autonomous, user-away):** keep volume-based persistence; do **not** restore the persistent container lifetime (that would reintroduce the CS60 split-brain). Guarded by a Docker-free app-model smoke test asserting the named, writable `/data` volume mount (AppHost.Tests 5→6).
- `dotnet build` 0/0; full default `dotnet test` green (AppHost.Tests 6/6); `harness lint` 23/0.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | omni-ae |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
