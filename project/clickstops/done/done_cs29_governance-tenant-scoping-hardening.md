# CS29 — Governance service tenant-scoping & fail-closed hardening

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs29/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Filed by:** yoga-ae-c3 — 2026-07-04, LRN harvest (CS28h): dispositioning open learnings into fix CSs.
**Depends on:** CS11, CS14

## Goal

Close the confused-deputy / cross-tenant authorization gap in `Governance.Service` by moving tenant scoping and principal binding server-side — `Bank.Web` currently guards only client-side — and harden the RBAC-policy factory to fail closed.

## Background

Fixes **LRN-049** and **LRN-044**.

LRN-049 (CS14): `Governance.Service` is anonymous and non-tenant-scoped. Its approve/reject/list endpoints return and accept **all** requests across every tenant. The CS14 Bank.Web fix binds each decision to the tenant-scoped `_pending` set — `AccessRequestsModel.CanDecide` guards both the rendered list and the approve/reject POST handling — and catches `JsonException` on a malformed 2xx body; but that is a *client-side* defense-in-depth guard. Because `Governance.Service` itself performs no tenant check, a caller bypassing Bank.Web can still POST a known cross-tenant request GUID directly and have it accepted (a classic confused-deputy escalation). The complete fix is server-side tenant scoping in `Governance.Service`, explicitly out of CS14 scope.

LRN-044 (CS20): `RbacPolicy.Create` validates cross-references between roles and permissions but does **not** verify that its `roles`/`permissions` lists are non-empty and distinct (Copilot R3, non-blocking). A mechanically-built policy with empty or duplicate members should fail closed at construction rather than emit an invalid policy.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Tenant-scoping location | Server-side in `Governance.Service` — the list is filtered by the caller's tenant, and approve/reject re-check the target request's tenant against the caller. The Bank.Web guard becomes defense-in-depth, not the sole boundary. | LRN-049 — a UI-only guard is insufficient because the posted request id is untrusted. |
| 2 | Principal source | Bind to authenticated token claims (`sub`/`tenant`) rather than caller-supplied fields, mirroring the Bank.Api token-bound contract (LRN-011); fail closed on a missing or unknown tenant claim. | Defense-in-depth over domain rules — identity must come from the validated token, never the request body. |
| 3 | Fail-closed validation | Reject a cross-tenant decide (403/404); `RbacPolicy.Create` rejects empty or duplicate `roles`/`permissions`. | LRN-044 — the policy factory must fail closed on degenerate input. |
| 4 | Test coverage | Cross-tenant decide → 403/404; list is tenant-scoped; `RbacPolicy.Create` empty/dup → throws; existing engine + governance parity stays green. | Lock in the fix and prevent regression of the confused-deputy gap. |

## Deliverables

- Server-side tenant scoping on `Governance.Service` list/approve/reject, plus authenticated-principal (or validated-tenant) binding.
- `RbacPolicy.Create` non-empty/distinct fail-closed validation.
- Tests: cross-tenant decide blocked, tenant-scoped list, policy-factory empty/duplicate validation.
- Governance doc update describing the tenant-scoping + principal-binding contract.

## User-approval gates

- **Governance.Service authentication model.** The service is currently anonymous. Full `JwtBearer` wiring (reusing `AuthenticationSetup` + the Keycloak authority, per LRN-009/010) versus a validated tenant header for the lab is a design decision to confirm at claim time before implementation.

## Exit criteria

- Cross-tenant approve/reject is blocked server-side (not only in the UI).
- The list endpoint is tenant-scoped to the caller.
- `RbacPolicy.Create` fails closed on empty or duplicate roles/permissions.
- Full-solution `dotnet build` + `dotnet test` green.

## Risks + open questions

- Adding authentication to `Governance.Service` may ripple into the Bank.Web client and AppHost wiring — keep the change additive.
- The authn-vs-validated-header choice (see User-approval gates) is the main open question and gates the implementation shape.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 06d5cf65890e | 2026-07-04T17:47:00Z | Go-with-amendments | LRN-049 wording amended: Bank.Web CanDecide guards render+POST but Governance.Service stays anonymous/unscoped; server-side fix is the CS. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Decide authn model (JwtBearer vs validated-tenant-header) from code | done | yoga-ae-c3 | Chose JwtBearer token-bound tenant (LRN-011); audience bank-api, no realm change |
| Server-side tenant scoping: list/approve/reject | done | yoga-ae-c3 | Query-level tenant filter; cross-tenant get/approve/reject → 404 before status check |
| Principal/tenant binding to authenticated context | done | yoga-ae-c3 | RequireTenant reads token tenant, 403 on missing; create binds tenant from token |
| RbacPolicy.Create non-empty/distinct validation | done | yoga-ae-c3 | Fail-closed on empty/duplicate roles/permissions (LRN-044) |
| Tests | done | yoga-ae-c3 | Governance +32, Pdp +6, Bank.Web +2; handler-integration deferred (documented) |
| Governance doc update | done | yoga-ae-c3 | docs/governance/tenant-scoping.md |
| Close-out: docs + restart state | done | yoga-ae-c3 | WORKBOARD row removed, CONTEXT.md updated |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | LRN-049/LRN-044 flipped to applied; handler-integration-test follow-up documented |

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
**Date:** 2026-07-04T19:32:44Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| Server-side tenant scoping + token-bound tenant | match | `GovernanceEndpoints` query-level tenant filter + `GovernanceAuthenticationSetup`; cross-tenant get/approve/reject → 404 before status check; `RequireTenant` → 403 on missing claim |
| Request create binds tenant from token, not body | match | `CreateRequestAsync` sets `TenantCode` from the validated token; cross-tenant principal → 404 |
| `RbacPolicy.Create` non-empty/distinct fail-closed | match | `RbacPolicy.cs` throws on empty/duplicate roles/permissions (LRN-044) |
| Tests | match (documented divergence) | Governance +32, Pdp +6, Bank.Web +2; handler-integration deferred (no EF-InMemory/TestHost in CPM) — documented follow-up |
| Governance doc | match | `docs/governance/tenant-scoping.md` |

**Test coverage:** sufficient — Governance 96/0, Pdp 550/0, Bank.Web 93/0; full-solution `dotnet test` **1063/0** at d25aa77.

**Outcome GO:** All deliverables and exit criteria met (cross-tenant approve/reject/get blocked server-side via query-level tenant filter; list tenant-scoped; `RbacPolicy.Create` fails closed). Documented divergences: unit/metadata tests not handler-integration; within-tenant approver-`sub` binding deferred; authn-vs-header gate resolved as JwtBearer token-bound tenant (audience bank-api, no realm change). Independent GPT-5.5 rubber-duck R1 (Go) → R2 (Go, query-filter refinement) + Copilot (5 findings resolved) + PvI R1 GO at d25aa77.
