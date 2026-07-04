# CS15 — AuthZ playground + audit explorer

**Status:** done
**Owner:** yoga-ae-c2
**Branch:** cs15/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
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
| Playground fan-out API (all engines: decision/reason/explanation/latency/trace) | done | yoga-ae-c2 | agent-id=cs15-pdp-fanout \| role=implementer \| report-status=complete \| learnings=0 — `POST /api/authz/playground/fanout`, +29 PDP tests |
| Audit query: single-entry (`?sequence=`) filter | done | yoga-ae-c2 | agent-id=cs15-audit-filter \| role=implementer \| report-status=complete \| learnings=0 — closes the ingest Location-header gap, +11 audit tests |
| Bank.Web UI: Playground + Audit Explorer pages, clients, replay+verify, AppHost wiring | done | yoga-ae-c2 | agent-id=cs15-bankweb-ui \| role=implementer \| report-status=complete \| learnings=1 — 2 InteractiveServer pages + clients; +26 Bank.Web tests (incl. orchestrator preset fix) |
| Docs: playground + audit-explorer feature doc | done | yoga-ae-c2 | agent-id=cs15-docs \| role=implementer \| report-status=complete \| learnings=0 — `docs/product/authz-playground-and-audit-explorer.md` + bank-web.md pointer |
| Close-out: docs + restart state | done | yoga-ae-c2 | WORKBOARD row removed; CONTEXT.md CS15-done entry + next-claimable; `docs/product/authz-playground-and-audit-explorer.md` is the restart surface |
| Close-out: learnings + follow-ups | done | yoga-ae-c2 | LEARNINGS.md LRN-053..057 filed; faithful-replay follow-up recorded as open architectural learning (LRN-057) |

## Notes / Learnings

**Design decision — Audit Explorer "replay" fidelity (orchestrator, 2026-07-04).** The CS13
tamper-evident audit row captures `subject/action/resource(type,id)/tenant/decision/reason/trace`
but NOT the ABAC inputs (`amount`, `maker`, `status`, subject `roles`, context `scopes`). A naïve
auto-replay would re-run with those inputs missing and spuriously diverge, so it would mislead.
Chosen approach: **replay = "open in Playground"** — the Audit Explorer pre-fills the Playground
with the captured fields and shows the recorded decision alongside the live cross-engine fan-out,
with a banner that uncaptured ABAC fields must be completed to reproduce the original decision.
This keeps CS13's tamper-evident store untouched. **Faithful 1:1 replay** (storing a full request
snapshot per audit row) is deferred to a follow-up planned CS (option B), since it changes the
security-critical audit component and warrants its own review.

**Trace link** is best-effort (plan-review R1): the fan-out surfaces the request trace id; a deep
link to Grafana/Tempo (CS12) is rendered only when an observability base URL is configured.

**Playground fan-out is non-audited** (what-if semantics, mirroring the CS17 `WhatIfEvaluator`/
`/shadow` surface, which resolve engines by name and never emit an enforcement audit event).

**Implementation (2026-07-04).** Backend: `PlaygroundFanoutService` + `POST /api/authz/playground/fanout`
runs one request across all registered engines (in-process `reference/aspnet/casbin/cedar`; `opa/openfga`
fail closed to an `Available=false` row when their container is down), returning per-engine
`{decision, reasons, obligations, explanation, latencyMs, traceId, available}` + a cross-engine `allAgree`.
Audit: added a `?sequence=` filter to `GET /api/audit/entries` (closes the ingest `Location` header gap).
UI (Bank.Web, InteractiveServer, `[Authorize]`): `/playground` (form + presets + engine selection +
comparison table + replay pre-fill) and `/audit` (filters + hash-chain verify badge + "Replay in
Playground"); new fail-closed `IAuditClient` + `PdpClient.FanoutAsync`; `bank-web` now
`.WithReference(audit-service)`. Build 0/0; full solution `dotnet test` **1027** (PDP 573, Bank.Web 118,
Audit 58); `harness lint` 22/0.

**Orchestrator fix (local review).** SA3's Playground presets had identical "Permit" and "Deny" entries
and the form bound a single `Tenant` to both subject and resource, so a cross-tenant (`TenantMismatch`)
deny was inexpressible. Added a `PlaygroundInput.ResourceTenant` field (falls back to `Tenant` when blank)
+ form input, and pointed the "Deny" preset's resource at `FABRIKAM` while the subject stays `CONTOSO`, so
the deny preset now actually denies. +2 view-model tests.

**Learning candidate (blazor).** A Razor page cannot `@inject` a member whose name equals the
page/component type name — `Audit.razor` injecting a member named `Audit` failed with CS0542; SA3 renamed
it to `AuditApi`. File at close-out.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.6 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T19:47:13Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — Playground fan-out across all engines (result/latency/reason/explanation/trace) | match | `POST /api/authz/playground/fanout` validates request + engine names and fans one `AccessRequest` across the resolved providers, returning per-engine decision/reasons/obligations/explanation/latency/traceId/availability + `allAgree` over available engines; `/playground` renders comparable rows. Trace deep-linking is best-effort (conditional on `Observability:BaseUrl`), as documented. |
| D2 — Audit Explorer (filter/search, replay, chain-verify) | diverged (documented) | Filter/search via `/api/audit/entries` (incl. `sequence`) + chain verification via `/api/audit/verify`; `/audit` renders filters, results, hash-verify badge, and "Replay in Playground". Replay intentionally diverges from literal 1:1: it opens the Playground pre-filled with the captured audit fields + recorded decision, because CS13 rows omit ABAC inputs. Documented + justified. |
| Exit criteria — single decision fans out + comparable results; audit explorable + replayable | match | Side-by-side cross-engine fan-out + explorable audit with replay links under the documented pre-fill semantics. No genuine unmet exit criterion. |

**Test coverage:** targeted CS15 tests pass (PDP Playground 30, audit sequence-filter 12, Bank.Web Playground/Audit client + input 27); full solution `dotnet test` 1132/1132 on the merged state.

**Outcome GO:** the implementation satisfies the CS15 deliverables + exit criteria; the only material divergence is the intentional, documented replay-fidelity trade-off (the audit row lacks the full original request snapshot — faithful 1:1 replay deferred, LRN-057).
