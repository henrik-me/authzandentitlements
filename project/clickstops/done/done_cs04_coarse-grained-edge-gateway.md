# CS04 — Coarse-grained edge gateway (YARP)

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs04/content
**Started:** 2026-07-03
**Closed:** 2026-07-03
**Phase:** 1 — AuthN + coarse-grained
**Lane:** Identity
**Depends on:** CS03

## Goal

Enforce coarse-grained authorization on token scopes/claims at a YARP edge before any fine-grained check.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | bd7f5db0abb5 | 2026-07-02T19:47:54Z | Go-with-amendments | Blocker resolved; no live Audit.Service required, but align deliverable/task wording to say audit-ready events. |

## Deliverables

- Edge.Gateway (YARP) routing to services.
- Coarse policies on scope/claim/audience/tenant.
- Documented coarse-vs-fine boundary; both gates emit audit + telemetry.

## Exit criteria

- Requests lacking a required scope/audience/tenant are rejected at the edge.
- Allowed requests are routed; gateway decisions emit structured, audit-ready events + OTel (Audit.Service ingests them in CS13).

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Edge.Gateway project (YARP proxy + routes) | done | cs04-edge-gateway | agent-id=cs04-edge-gateway \| role=implementer \| report-status=complete \| learnings=1 |
| Coarse-grained policies (scope/audience/tenant) | done | cs04-edge-gateway | agent-id=cs04-edge-gateway \| role=implementer \| report-status=complete \| learnings=0 |
| Gateway audit-ready events + OTel telemetry | done | cs04-edge-gateway | agent-id=cs04-edge-gateway \| role=implementer \| report-status=complete \| learnings=0 |
| Gateway unit tests | done | cs04-edge-gateway | agent-id=cs04-edge-gateway \| role=implementer \| report-status=complete \| learnings=0 (35 tests) |
| Document coarse-vs-fine boundary | done | cs04-boundary-doc | agent-id=cs04-boundary-doc \| role=implementer \| report-status=complete \| learnings=1 |
| CPM + solution + AppHost wiring; runtime e2e | done | yoga-ae-c3 | orchestrator integration + verification (build 0/0, 63 tests, e2e coarse denials verified) |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

**Delivered (content PR `cs04/content`):** a new `AuthzEntitlements.Edge.Gateway`
ASP.NET Core YARP reverse proxy that fronts Bank.Api and enforces **coarse-grained**
authorization before routing — valid JWT + audience `bank-api` + the scope a route
class needs (`bank.read` / `bank.transactions.write` / `bank.approvals.write`) +
`tenant`-claim presence — mirroring the CS03 token contract (`MapInboundClaims=false`).
Four coarse policies + a `TenantPresenceRequirement`; YARP routes map each Bank.Api
route class to its policy. Every coarse decision emits a structured, **audit-ready**
event (subject/tenant/scope/route/decision/reason/trace) via `ILogger` + an OTel
`ActivitySource`/`Meter` (CS13 ingests later — no live Audit.Service yet). Boundary
doc at `docs/architecture/coarse-vs-fine-boundary.md`. A dedicated gateway unit-test
project covers JWT setup, scope, tenant presence, policy composition, and audit
classification/gating.

**Sub-agents (parallel, disjoint ownership):** `cs04-edge-gateway` (claude-opus-4.8) —
gateway vertical + tests; `cs04-boundary-doc` (claude-opus-4.8) — boundary doc.
Orchestrator `yoga-ae-c3` (claude-opus-4.8): CPM pin, `.sln` + AppHost wiring, verification.

**Verification.** Build 0 warnings/0 errors under `TreatWarningsAsErrors`; the gateway
and Bank.Api unit suites pass with no regressions; `harness lint` 0 failed. Runtime e2e against
live Keycloak + a real teller1 token: no token → **401** at edge; `bank.read` token →
`GET /api/accounts` **routed** (`allow/routed`); `bank.read` token → `POST /api/transactions`
→ **403** at edge (`deny/missing-scope`); `bank.transactions.write` token → same POST →
**forwarded** (scope differentiation). Audit events observed for each decision.

**Observation (not a CS04 regression) — Bank.Api runtime 500 under Aspire.** During
`aspire run`, Bank.Api returned an empty-body 500 on every request (incl. `/alive`), which
blocked the gateway's `WaitFor(bank-api)`. Bank.Api is unchanged by CS04, and the gateway
run standalone (without the Aspire OTLP export env) serves and enforces correctly — so this
is an environmental Aspire/OTLP-export issue orthogonal to CS04. Candidate close-out
follow-up (triage the OTLP/instrumentation vs .NET 10 RC1 interaction).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (independent — sub-agent `cs04-plan-vs-impl-2`)
**Date:** 2026-07-03T18:55:00Z
**Outcome:** GO

Plan vs. merged `main` (HEAD `665ba06`; content PRs #17 edge gateway + #19 fine-gate audit). Round 1 (HEAD `7e02fca`) returned **NEEDS-FIX** — only the edge gate emitted audit-decision events; addressed by PR #19 (the fine gate now emits a structured, audit-ready authorization-decision event). Re-review = GO.

| Planned item | Outcome | Evidence |
|---|---|---|
| Goal: enforce coarse authz on token scopes/claims at a YARP edge before fine checks | match | `Edge.Gateway/Program.cs`, `Auth/CoarseAuthorization.cs`, `appsettings.json` |
| Edge.Gateway (YARP) routing to services | match | `Edge.Gateway/Program.cs` (Add/MapReverseProxy); `AppHost.cs` (edge-gateway) |
| Coarse policies on scope/claim/audience/tenant | match | `Auth/GatewayAuthenticationSetup.cs`, `CoarseAuthorization.cs`, `GatewayScopeRequirement.cs`, `TenantPresenceRequirement.cs` |
| Documented coarse-vs-fine boundary | match | `docs/architecture/coarse-vs-fine-boundary.md` |
| Both gates emit audit + telemetry | match | Edge: `GatewayAuditMiddleware`, `GatewayMetrics`, OTel. Fine: `Bank.Api/Auth/BankAuthorizationAuditMiddleware` + `BankAuditEvent` + ServiceDefaults OTel |
| Exit: requests lacking scope/audience/tenant rejected at edge | match | `GatewayAuthenticationSetup`, `CoarseAuthorization`; gateway tests |
| Exit: allowed requests are routed | match | `Program.cs` MapReverseProxy; `GatewayAuditTests` |
| Exit: gateway decisions emit structured audit-ready events + OTel | match | `GatewayAuditEvent`, `GatewayAuditMiddleware`, `GatewayMetrics` |

**Test coverage:** sufficient — Edge.Gateway.Tests 47/47, Bank.Api.Tests 37/37 (`dotnet test AuthzEntitlements.sln`).

**Non-blocking follow-ups (LRN-013/014):** enrich edge-denial events with RouteId/RequiredScope; skip auditing non-authz-decision requests (unmatched 404 / method-mismatch 405) uniformly across both gates; triage the Aspire/OTLP `Bank.Api` runtime-500 (CS12).
