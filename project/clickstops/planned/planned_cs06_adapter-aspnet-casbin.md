# CS06 — Adapters: ASP.NET Core policies + Casbin.NET

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Provide the .NET-native baselines (RBAC) as container-free adapters: ASP.NET Core policy-based + Casbin.NET.

## Deliverables

- AspNetCorePolicyProvider (roles: teller/manager/compliance/auditor).
- CasbinProvider (RBAC/ABAC model + policy).
- Both implement IAuthorizationDecisionProvider; container-free "lite" profile.

## Exit criteria

- Both adapters pass the scenario catalog and are selectable at runtime.
- Lite profile runs with no containers.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Implement ASP.NET policy adapter | pending | — | |
| Implement Casbin adapter | pending | — | |
| Map scenarios | pending | — | |
| Verify lite profile | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
