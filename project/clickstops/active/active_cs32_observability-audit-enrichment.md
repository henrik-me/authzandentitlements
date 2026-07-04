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
| Triage/repro the aspire-run empty-body 500 | pending | — | Full `aspire run` with collector ready (WaitFor(observability)); fix or document root cause (LRN-014) |
| Edge-denial event RouteId/RequiredScope enrichment | pending | — | Capture matched route metadata pre-short-circuit (not IReverseProxyFeature, unset on short-circuit) (LRN-013) |
| Uniform non-authz (404/405) audit skip | pending | — | Both GatewayAuditMiddleware + BankAuthorizationAuditMiddleware skip unmatched 404 / method-mismatch 405 (LRN-013) |
| Tests | pending | — | 401/403 edge-deny carry route/scope; 404/405 not audited on either gate |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, feature docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | Flip LRN-014/013 to applied; file/disposition learnings; open follow-up CSs for unresolved issues |

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

> _(filled at close-out per the gate)_
