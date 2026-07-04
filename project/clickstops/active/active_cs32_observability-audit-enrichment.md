# CS32 — Observability & audit-event enrichment; aspire-run 500 triage

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs32/content
**Started:** 2026-07-04
**Closed:** —
**Filed by:** yoga-ae-c3 — 2026-07-04, LRN harvest (CS28h): dispositioning open learnings into fix CSs.
**Depends on:** CS04, CS12, CS13

## Goal

Resolve the deferred observability/audit follow-ups: triage and reproduce the empty-body 500 under a full `aspire run` OTLP wiring, enrich edge-denial audit events, and uniformly skip auditing non-authz (404/405) requests across both audit gates.

## Background

Fixes **LRN-014** and **LRN-013**.

LRN-014: under `aspire run`, `Bank.Api` returned an empty-body HTTP 500 on **every** request (including `/alive`), which blocked the gateway's `WaitFor(bank-api)` so the gateway never started; the suspected cause is an Aspire/OTLP-export interaction on .NET 10 RC1. CS12 wired the real `grafana/otel-lgtm` collector and repointed every service's `OTEL_EXPORTER_OTLP_ENDPOINT` at it, but a full-run reproduction was never performed (a parallel `aspire run` may have been active; CS12 verified the stack standalone instead).

LRN-013: two edge-audit follow-ups remain — enrich edge-denial events with RouteId/RequiredScope (unset via `IReverseProxyFeature` on short-circuits), and skip auditing non-authz-decision requests (unmatched 404 / method-mismatch 405) uniformly across the edge gate and the Bank.Api gate.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Aspire-run 500 triage | Reproduce on a clean full `aspire run` with the collector ready (`WaitFor(observability)`); if it reproduces, isolate the OTLP-export interaction and either fix it or document the root cause. | LRN-014 — the empty-body 500 triage is still open after CS12. |
| 2 | Edge-denial enrichment | Capture the matched YARP route metadata (RouteId / RequiredScope) from endpoint/route metadata **before** the coarse-authz short-circuit runs — not from `IReverseProxyFeature`, which LRN-013 notes is unset on short-circuit denials — and stamp it onto the edge-deny audit event. | LRN-013 — 401/403 short-circuit denials lack route/scope context because the proxy feature is unset at that point, but the route match is known earlier in the pipeline. |
| 3 | Uniform non-authz skip | Both `GatewayAuditMiddleware` and `BankAuthorizationAuditMiddleware` skip auditing unmatched 404 / method-mismatch 405 requests (non-decisions). | LRN-013 — only genuine authz decisions should be audited. |

## Deliverables

- A root-cause note or fix for the aspire-run empty-body 500.
- Enriched edge-denial audit events carrying RouteId/RequiredScope, captured pre-short-circuit, with tests covering 401 and 403 edge denials.
- Uniform non-authz-request audit skip across both gates, with tests.

## User-approval gates

None — but note that the 500 may be an Aspire/RC environmental issue not fully fixable in-repo; if so, deliver a root-cause document plus a mitigation.

## Exit criteria

- A full `aspire run` serves without the empty-body 500, or the root cause is documented with a mitigation.
- Edge-deny events carry route/scope.
- 404/405 non-decisions are not audited on either gate.
- Full-solution `dotnet build` + `dotnet test` green.

## Risks + open questions

- The 500 may be environmental (Aspire/RC) and not fully fixable in-repo → root-cause documentation is the fallback deliverable.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 82f64b7c151b | 2026-07-04T17:47:00Z | Needs-Fix | Decision 2 sourced RouteId/RequiredScope from IReverseProxyFeature, which LRN-013 says is unset on short-circuit denials. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 39e16e630f24 | 2026-07-04T17:50:00Z | Go | Decision now captures route/scope pre-short-circuit (not IReverseProxyFeature) with 401/403 deny tests. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Triage/repro the aspire-run empty-body 500 | done | yoga-ae-c3 | Documented root cause (non-reproducible early-RC; OTLP request-path isolated; all 7 svcs WaitFor) + runbook; AppHost unchanged (LRN-014) |
| Edge-denial event RouteId/RequiredScope enrichment | done | yoga-ae-c3 | feature→endpoint-RouteModel fallback; 401/403 denies carry route/scope (LRN-013) |
| Uniform non-authz (404/405) audit skip | done | yoga-ae-c3 | Both gates skip unmatched-404 + method-mismatch-405 (LRN-013) |
| Tests | done | yoga-ae-c3 | Edge 81, Bank.Api 95; enrichment + skip tests on both gates |
| Close-out: docs + restart state | done | yoga-ae-c3 | CONTEXT.md updated; docs/observability/aspire-run-500-triage.md + docs/authz/audit-enrichment-and-skip-contract.md |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | LRN-013/014 flipped to applied; doc-link-hygiene follow-up noted |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T21:22:45Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| Aspire-run empty-body 500 root-cause note or fix | match | Documented-divergence: no AppHost change; OTLP exporter request-path isolation verified + all 7 services already `WaitFor(observability)`; early-RC/environmental root-cause + runbook in `docs/observability/aspire-run-500-triage.md` |
| Edge-denial events carry RouteId/RequiredScope (pre-short-circuit) + 401/403 tests | match | `GatewayAuditMiddleware.ResolveRouteMetadata` feature→endpoint-RouteModel fallback; `GatewayAuditEnrichmentTests` assert 401/403 carry route/scope |
| Uniform 404/405 non-authz skip across both gates + tests | match | Edge `ShouldAudit` (404/405) + Bank.Api `ShouldAudit(endpointMatched, statusCode)`; skip tests on both gates |
| Audit enrichment/skip contract doc | match | `docs/authz/audit-enrichment-and-skip-contract.md` |

**Test coverage:** sufficient — `dotnet build` 0/0; Edge 81, Bank.Api 95; full solution `dotnet test` **1347/1347**.

**Outcome GO:** All deliverables + exit criteria met. LRN-014's `aspire run` 500 is a **documented divergence** (not a dropped deliverable): the triage doc records the non-reproducible early-RC/environmental assessment, the OTLP request-path-isolation proof, the existing `WaitFor(observability)` mitigation state, and a clean-machine confirmation runbook. GPT-5.5 content review R1 Go + Copilot (1 pre-existing-link nit → doc-hygiene follow-up) + PvI GO.
