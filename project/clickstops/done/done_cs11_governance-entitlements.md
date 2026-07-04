# CS11 — Access-governance entitlements (Entra pattern)

**Status:** done
**Owner:** yoga-ae
**Branch:** cs11/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** 3 — Entitlements
**Lane:** Entitlements
**Depends on:** CS02, CS08

## Goal

Model access-governance entitlements: access packages, JIT elevation, and access reviews.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 3d85da8202a2 | 2026-07-02T19:47:54Z | Go-with-amendments | CS08 already pulls CS05; amend to state SoD checks go through PDP OpaProvider, not direct OPA coupling. |

## Deliverables

- Governance.Service; access packages (e.g., quarter-end close).
- JIT elevation with approval workflow; time-bound access.
- Access-review / recertification campaigns; JIT tied to SoD (via OPA).

## Exit criteria

- A user requests a JIT/access-package grant, gets approval, receives time-bound access that expires; reviews run.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Model access packages | done | yoga-ae | agent-id=cs11-governance-service \| role=service-impl \| report-status=complete \| learnings=0 |
| JIT approval workflow (maker-checker; SoD via PDP) | done | yoga-ae | agent-id=cs11-governance-service \| role=service-impl \| report-status=complete \| learnings=0 |
| Time-bound grants + expiry | done | yoga-ae | agent-id=cs11-governance-service \| role=service-impl \| report-status=complete \| learnings=0 |
| Review campaigns | done | yoga-ae | agent-id=cs11-governance-service \| role=service-impl \| report-status=complete \| learnings=0 |
| SoD via PDP OpaProvider (governance.access.request; reference+OPA parity) | done | yoga-ae | agent-id=cs11-pdp-opa-sod \| role=pdp-opa-impl \| report-status=complete \| learnings=2 |
| AppHost wiring + governance docs | done | yoga-ae | agent-id=cs11-apphost-docs \| role=integration-impl \| report-status=complete \| learnings=0 |
| Close-out: docs + restart state | done | yoga-ae | Renamed active→done; removed WORKBOARD row; updated CONTEXT.md (CS11 completion + next-claimable); docs/governance/access-governance.md shipped |
| Close-out: learnings + follow-ups | done | yoga-ae | Filed LRN-040 (concurrent-main .NET build break undetected by process-only CI + active-main merge race). No follow-up CS needed. |

## Notes / Learnings

Shipped `AuthzEntitlements.Governance.Service` (access packages, JIT elevation with maker-checker + SoD-via-PDP approval, time-bound grants with read-time expiry, access-review campaigns) plus the engine-agnostic `governance.access.request` SoD action (reference + OPA parity, integrated with CS16 explanations). Reviewed across GPT-5.5 R1–R10 + 7 Copilot rounds; build 0/0, 762-test full suite, `opa test` 64/64. New learning: LRN-040. The merge to `main` also repaired a pre-existing CS13×CS16 .NET build break (independently hotfixed on main by PR #60).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck, cs11-pvi)
**Date:** 2026-07-04T04:15:00Z
**Outcome:** GO

Per-deliverable + exit-criteria outcome (all `match`):

| Deliverable / criterion | Outcome |
|---|---|
| Governance.Service + access packages | match — service AppHost-wired as `governance-service`; startup migrates/seeds; catalog includes `quarter-end-close` (BranchManager + ComplianceOfficer) |
| JIT elevation + approval workflow + time-bound access | match — `POST /requests` → `/approve` (checker≠requester + checker-eligibility + SoD) → `AccessGrant` with `ExpiresAt`; read paths enforce `IsActive(now)` |
| Access-review / recertification campaigns | match — campaign create/run/decision endpoints; run materialises one item per active grant; decisions certify/revoke via `ReviewCampaignPlanner` |
| JIT tied to SoD via PDP/OPA | match — governance calls the PDP via `PdpSodClient` (`POST /api/authz/evaluate`, `governance.access.request`), not OPA directly, fail-closed; reference + Rego mirror the same `SodConflict` role-pair rules |
| Exit criteria lifecycle | match — request → approval → time-bound grant → read-time expiry → review campaign run/decide |

**Test coverage:** sufficient (seeded catalog, grant expiry/factory, approval gates + SoD fail-closed, PDP wire shape, review planner, reference + OPA/Rego governance tests). Non-blocking future improvement: one black-box in-process API lifecycle smoke test.

Intended-scope deferrals (not divergences): break-glass/CS21, Bank.Api enforcement of active grants, live audit ingestion/CS13.
