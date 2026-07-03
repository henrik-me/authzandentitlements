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
| Keycloak container + realm import (AppHost) | done | yoga-ae-c2 | agent-id=cs03-infra-apphost(orchestrator-integrated) \| role=implementer \| report-status=complete \| learnings=2 — AppHost Keycloak + realm import + stable-authority env injection |
| Realm export JSON (users/roles/scopes/claims/clients) | done | cs03-realm | agent-id=cs03-realm \| role=implementer \| report-status=complete \| learnings=1 — infra/keycloak/authz-bank-realm.json |
| JWT validation + authz policies in Bank.Api (+ tests) | done | cs03-bank-api-authz | agent-id=cs03-bank-api-authz \| role=implementer \| report-status=complete \| learnings=2 — JwtBearer, role/scope/tenant policies, sub/tenant binding, protected endpoints |
| Bank.Web OIDC login stub | done | cs03-bank-web | agent-id=cs03-bank-web \| role=implementer \| report-status=complete \| learnings=0 — OIDC code flow + claims display |
| Document Entra ID path | done | cs03-entra-doc | agent-id=cs03-entra-doc \| role=implementer \| report-status=complete \| learnings=0 — docs/identity/entra-id.md |
| Close-out: docs + restart state | done | yoga-ae-c2 | WORKBOARD row removed; CONTEXT.md refreshed (CS03 done) |
| Close-out: learnings + follow-ups | done | yoga-ae-c2 | Filed LRN entries; follow-up: route-level integration tests |

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

**Delivered (content PR `cs03/content`):** Keycloak Aspire container + `authz-bank` realm import (`infra/keycloak/authz-bank-realm.json`); JWT bearer validation + role/scope/tenant authorization on Bank.Api (`Auth/` module, endpoints gated); Bank.Web OIDC login stub (`/`, `/login`, `/claims`, `/logout`); Entra ID mapping doc (`docs/identity/entra-id.md`); 21 new tests (7→28).

**Sub-agents (Wave A, disjoint ownership):** `cs03-realm` (claude-opus-4.6), `cs03-bank-api-authz`, `cs03-bank-web`, `cs03-entra-doc` (claude-opus-4.8). AppHost integration + all runtime fixes by orchestrator `yoga-ae-c2` (claude-opus-4.8).

**Integration fixes found only via runtime verification** (candidate learnings for close-out):
1. **Keycloak 26 `organization` feature** (default-on) crashes service-account import with "Session not bound to a realm" → AppHost sets `KC_FEATURES_DISABLED=organization`.
2. **Aspire `WithRealmImport` enforces `<realm>-realm.json`** naming (a bare directory import is lenient; Aspire's is not) → realm file is `authz-bank-realm.json`.
3. **Aspire/Keycloak proxy issuer instability** — a dynamic/proxied Keycloak endpoint makes the `iss` differ per access path, so JWT issuer validation fails → Keycloak pinned to a fixed host port (8088) with one explicit stable `Keycloak:Authority` shared by bank-api + bank-web.
4. **`JwtBearer.MapInboundClaims` defaults to true**, remapping Keycloak's `roles` claim to the legacy `ClaimTypes.Role` URI while `RoleClaimType="roles"` → `RequireRole` silently always failed (401/403). Fixed with `MapInboundClaims=false` (bank-api + bank-web). The synthetic-principal unit tests missed this (they bypass the JWT handler); added `AuthenticationSetupTests` as a regression guard. Reinforces LRN-007 (verify review/tests against the running system).
5. **Built-in Keycloak client scopes (`basic`/`profile`/`email`) are not auto-seeded** when a realm export supplies its own `clientScopes` → `sub`/`preferred_username`/`email` were absent. Fixed by adding those mappers to the applied `bank-claims` scope; bank-web requests only `openid`+`bank.read` (identity claims come from the default `bank-claims`). Client default-scope lists cleaned to reference only defined scopes.
6. **`POST /api/accounts` was unauthenticated** (missed in the sub-agent brief) → gated to the `BranchManager` role (interim; a dedicated account-lifecycle scope arrives with later authz CSs).

**GPT-5.5 R1 review hardening (Needs-Fix → fixed):** the review correctly found that endpoints trusted caller-supplied identity fields. Fixed in R2: (a) transaction **maker** and approval **checker** are bound to the token `sub` (a caller can no longer act as another user); (b) **every** account/transaction/reference endpoint is now tenant-scoped or tenant-checked against the token `tenant` claim (no cross-tenant read or write; fail-closed on a missing/unknown claim); (c) `RequireHttpsMetadata` is environment-gated (dev HTTP; non-dev fails closed on HTTPS). The `branch` claim is carried for later branch-scoped ABAC and is not yet enforced.

**Verification (runtime, real Keycloak):** token claims correct for all 5 users incl. `sub`=Bank.Api `User.Id` (standalone Keycloak); bank-api authorization matrix incl. cross-tenant + act-as-another-user denials; **full `aspire run` e2e** — Keycloak imports cleanly, bank-api 200/201, bank-web `/claims` 302→Keycloak login. Build 0 warnings; tests pass; `harness lint` 0 failed.

**Dev-only realm posture:** `sslRequired=none`, fixed lab passwords (`Passw0rd!`), permissive `redirectUris`/`webOrigins`. Not for non-local use.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.6 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (independent — sub-agent `cs03-pvi`)
**Date:** 2026-07-03T08:15:00Z
**Outcome:** GO

Per-item outcome (plan vs. merged `main` HEAD `6fd1548`):

| Planned item | Outcome | Evidence |
|---|---|---|
| Keycloak Aspire container; realm users/roles/scopes/custom claims | match | AppHost Keycloak + realm import (`AppHost.cs`); roles/scopes/mappers/users (`infra/keycloak/authz-bank-realm.json`). |
| Client-credentials/workload clients (used by CS19) | match | `bank-workload` service account + `bank.read` + tenant claim (`authz-bank-realm.json`). |
| Bank.Web OIDC login + service JWT validation | match | Bank.Web OIDC code flow (`Bank.Web/Program.cs`); Bank.Api JWT bearer (`Auth/AuthenticationSetup.cs`). |
| Entra ID documented alternative | match | Concept/token/config mapping (`docs/identity/entra-id.md`). |
| Exit: users obtain a JWT with tenant/roles/scopes | match | Realm scopes/mappers emit `tenant`/`roles`/`scope`; runtime-verified for all 5 seed users. |
| Exit: services validate incoming tokens | match | Auth middleware + policy-gated endpoints; runtime authz matrix (401/200/403/404/201). |
| R1/R2 identity + tenant hardening | added | Maker/checker bound to `sub`; every endpoint tenant-scoped + fail-closed (`TenantScope.cs`, `TransactionEndpoints.cs`). |

**Test coverage:** sufficient — 7→28 tests (policy, authentication-setup incl. `MapInboundClaims` + HTTPS-metadata gating, scope split, tenant/subject helpers). Gap: no route-level integration tests (filed as a follow-up suggestion).

**Review rounds:** GPT-5.5 rubber-duck R1 (Needs-Fix) → R2 (Needs-Fix) → R3 (Go) → R4 (Go, post-Copilot); Copilot PR review (4 comments, all resolved); plan-vs-implementation (this gate) = GO.
