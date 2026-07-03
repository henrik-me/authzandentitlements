# CS03 — AuthN via Keycloak OIDC

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs03/content
**Started:** 2026-07-03
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
| Keycloak container + realm import (AppHost) | pending | — | agent-id=cs03-infra-apphost \| role=implementer \| report-status=pending \| learnings=0 — AddKeycloak + WithRealmImport + WithReference to bank-api/bank-web |
| Realm export JSON (users/roles/scopes/claims/clients) | pending | — | agent-id=cs03-realm \| role=implementer \| report-status=pending \| learnings=0 — infra/keycloak/authz-realm.json per Design contract |
| JWT validation + authz policies in Bank.Api (+ tests) | pending | — | agent-id=cs03-bank-api-authz \| role=implementer \| report-status=pending \| learnings=0 — JwtBearer, tenant/role/scope policies, protect endpoints |
| Bank.Web OIDC login stub | pending | — | agent-id=cs03-bank-web \| role=implementer \| report-status=pending \| learnings=0 — OIDC code flow, claims display |
| Document Entra ID path | pending | — | agent-id=cs03-entra-doc \| role=implementer \| report-status=pending \| learnings=0 — docs/identity/entra-id.md |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Design decisions & contract

Authoritative contract shared by all CS03 sub-agents. Aligns the Keycloak realm with the CS02 domain seed (`src/AuthzEntitlements.Bank.Api/Data/BankSeeder.cs`).

**Packages (CPM, `Directory.Packages.props`):**
- AppHost: `Aspire.Hosting.Keycloak` `13.1.0-preview.1.25616.3` (preview is the only 13.1.0-band build; dev-loop only, AppHost is never deployed).
- Services/Web (token validation on the request path): **stable** `Microsoft.AspNetCore.Authentication.JwtBearer` / `Microsoft.AspNetCore.Authentication.OpenIdConnect` `10.0.0-rc.1.25451.107` (exact installed-runtime build). Deliberately NOT the preview `Aspire.Keycloak.Authentication`, to keep the request path off preview packages and retain full control over Keycloak issuer/authority config under `TreatWarningsAsErrors`.

**Realm:** name `authz-bank`; import via `WithRealmImport` from `infra/keycloak/`. Dev-only; `RequireHttpsMetadata=false`. Keycloak resource gets a **fixed endpoint** so the token `iss` is stable and matches each service's `Authority` (avoid the Aspire/Keycloak issuer-mismatch trap).

**Realm roles:** `Teller`, `BranchManager`, `ComplianceOfficer`, `Auditor` (exact `RoleNames` strings from `BankPolicy.cs`).

**Users** (realm `id` = Bank.Api `User.Id` so JWT `sub` correlates to the domain user; password `Passw0rd!` for the lab; usernames must be realm-unique — Fabrikam teller is `teller1-fabrikam`):

| username | sub (User.Id) | tenant | branch | realm role |
|---|---|---|---|---|
| teller1 | 40000000-…-000000000001 | CONTOSO | NM01 | Teller |
| manager1 | 40000000-…-000000000002 | CONTOSO | NM01 | BranchManager |
| compliance1 | 40000000-…-000000000003 | CONTOSO | NM01 | ComplianceOfficer |
| auditor1 | 40000000-…-000000000004 | CONTOSO | NM01 | Auditor |
| teller1-fabrikam | 40000000-…-000000000005 | FABRIKAM | FH01 | Teller |

**Custom claims (protocol mappers → access token):** `tenant` (tenant `Code`: CONTOSO/FABRIKAM), `branch` (branch `Code`), `roles` (top-level multivalued from realm roles, in addition to default `realm_access.roles`).

**Client scopes / OAuth scopes:** `bank.read`, `bank.transactions.write`, `bank.approvals.write` (emitted in `scope`), so the CS04 edge gateway can enforce coarse-grained scope checks.

**Clients:**
- `bank-web` — confidential, OIDC authorization-code flow (login for the Bank.Web stub); default client scopes include the `bank.*` scopes + the `tenant`/`branch`/`roles` mappers.
- `bank-api` — bearer-only / resource server (audience for the access token; validates only).
- `bank-workload` — service account (client-credentials) for non-human identities, used later by CS19; carries `tenant=CONTOSO` + `bank.read`.

**Authz policy contract (Bank.Api):** authenticated baseline; role policies `Teller`/`BranchManager`/`ComplianceOfficer`/`Auditor`; scope policies for read vs write; every endpoint tenant-checks the `tenant` claim against the resource tenant (defence in depth — the domain layer already never trusts caller-supplied tenant).

## Notes / Learnings

_Populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
