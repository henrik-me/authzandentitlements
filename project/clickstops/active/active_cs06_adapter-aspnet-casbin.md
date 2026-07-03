# CS06 — Adapters: ASP.NET Core policies + Casbin.NET

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs06/content
**Started:** 2026-07-03
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Provide the .NET-native baselines (RBAC) as container-free adapters: ASP.NET Core policy-based + Casbin.NET.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | b4705206e5f1 | 2026-07-02T19:47:54Z | Go-with-amendments | Clarify RBAC baseline coverage so passing the scenario catalog allows explicit unsupported denies for non-RBAC cases. |

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
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
