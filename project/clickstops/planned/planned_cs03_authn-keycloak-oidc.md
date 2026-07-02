# CS03 — AuthN via Keycloak OIDC

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 1 — AuthN + coarse-grained
**Lane:** Identity
**Depends on:** CS02

## Goal

Provide verified identity (AuthN) via an OIDC provider issuing tokens carrying tenant/roles/scopes/claims.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 00d5095e2d66 | 2026-07-02T19:47:54Z | Go | Dependency on CS02 is sound; scope cleanly establishes identity tokens and claims before CS04 and later CS19. |

## Deliverables

- Keycloak Aspire container; realm with users, roles, scopes, custom claims.
- Client-credentials/workload clients for non-human identities (used later by CS19).
- OIDC login wiring for Bank.Web (stub) + JWT validation for services.
- Microsoft Entra ID documented as the real-world alternative.

## Exit criteria

- Users can obtain a JWT whose claims include tenant/roles/scopes.
- Services validate incoming tokens.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add Keycloak container | pending | — | |
| Configure realm + clients + claims | pending | — | |
| Wire JWT validation | pending | — | |
| Document Entra ID path | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
