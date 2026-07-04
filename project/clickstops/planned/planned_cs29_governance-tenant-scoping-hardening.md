# CS29 — Governance service tenant-scoping & fail-closed hardening

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
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
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
