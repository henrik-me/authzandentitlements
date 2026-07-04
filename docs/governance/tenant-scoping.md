# Governance request tenant-scoping & token binding

> **Scope:** the server-side tenant boundary CS29 added to the access-**request** endpoints of
> [`AuthzEntitlements.Governance.Service`](../../src/AuthzEntitlements.Governance.Service). Read the
> [access-governance model](./access-governance.md) first — this doc only describes *who may see and
> decide which request*, not the maker-checker/SoD lifecycle itself.

## Why this exists (LRN-049 — the confused-deputy gap)

Before CS29, `Governance.Service` was anonymous and **not** tenant-scoped: its `list`, `get`,
`approve`, and `reject` request endpoints returned and accepted requests across **every** tenant.
[`Bank.Web`](../../src/AuthzEntitlements.Bank.Web) guarded the decision client-side (its
`AccessRequestsModel.CanDecide` scoped the rendered list and the POST handlers to the caller's
tenant), but that is **defense-in-depth, not the boundary**: a caller who bypasses the UI could POST
a known cross-tenant request GUID straight to the service and have it accepted — a classic
[confused-deputy](https://en.wikipedia.org/wiki/Confused_deputy_problem) escalation. The complete fix
has to live in the service, bound to a **validated token**, never a caller-supplied field.

## The contract

### Authentication — JWT bearer, token-bound tenant

The access-request endpoints require a validated Keycloak access token
([`GovernanceAuthenticationSetup`](../../src/AuthzEntitlements.Governance.Service/Auth/GovernanceAuthenticationSetup.cs),
mirroring `Bank.Api/Auth/AuthenticationSetup`):

- `MapInboundClaims = false` so Keycloak's literal claim names survive (LRN-010); the tenant is read
  from the literal **`tenant`** claim — the same custom protocol-mapper claim `Bank.Api` uses, so a
  single forwarded token scopes both services.
- Audience defaults to **`bank-api`** (see [Audience](#audience-decision-4) below); the issuer is the
  Keycloak realm authority the AppHost injects.
- `ClockSkew` is tightened to 30s and unsigned / non-expiring tokens are rejected.
- Fail-closed: outside `Development`, a missing authority throws at startup rather than registering a
  validator that can never discover signing keys.

The tenant claim is read by
[`GovernanceTenantClaims`](../../src/AuthzEntitlements.Governance.Service/Auth/GovernanceTenantClaims.cs):
`GetTenant()` returns `null` for an absent/blank claim, and `BelongsToTenant(caller, resource)` is a
fail-closed **Ordinal** equality that is `true` only when *both* sides are present and exactly equal.

### Scoped endpoints and their fail-closed behaviour

Only these five endpoints carry `.RequireAuthorization()` and the tenant check (`RequireTenant` reads
the token tenant, returning **403** when the claim is missing/blank — the request authenticated but
carries no tenant, so it is forbidden rather than allowed to see another tenant's data):

| Endpoint | Tenant rule | Cross-tenant / missing outcome |
|---|---|---|
| `POST /api/governance/requests` | The created request's `TenantCode` is set from the **token**, never the body. The target principal must belong to the caller's tenant. | Principal in another tenant → **404** (same as an unknown principal — never leaks cross-tenant principal existence). |
| `GET /api/governance/requests` | List filtered to `TenantCode == tokenTenant`. | Other tenants' requests are simply absent. |
| `GET /api/governance/requests/{id}` | Loaded request must satisfy `BelongsToTenant`. | **404 NotFound** — *before* any status/state check, so existence is never leaked. |
| `POST /api/governance/requests/{id}/approve` | Same `BelongsToTenant` guard. | **404 NotFound**, checked before the maker-checker/SoD logic runs. |
| `POST /api/governance/requests/{id}/reject` | Same `BelongsToTenant` guard. | **404 NotFound**, checked before the reject validation. |

**Why 404 (not 403) for a cross-tenant request id.** A `403` on a request the caller may not touch
still confirms the request *exists*. Returning `404` — the identical response to an unknown id —
denies that oracle, so a caller cannot enumerate another tenant's request GUIDs. The **missing-tenant**
case is different: it is not about a specific resource, so it is a `403` "a tenant context is
required".

`CreateAccessRequestBody` has **no** tenant field, so create binds the tenant purely from the token;
there is nothing for a caller to spoof.

### Endpoints that deliberately stay anonymous

Every other governance endpoint is **unchanged and anonymous**:

- access packages (`/access-packages`, `/access-packages/{code}`),
- principals (`/principals/{id}/grants`, `/principals/{id}/access`),
- grants (`/grants/{id}/revoke`),
- review campaigns and items (`/review-campaigns**`, `/review-items/{id}/decision`).

This is required, not an oversight: the [`Compliance`](../../src/AuthzEntitlements.Compliance)
service ([`HttpGovernanceClient`](../../src/AuthzEntitlements.Compliance/HttpGovernanceClient.cs))
reads `/review-campaigns`, `/access-packages`, and `/principals/{id}/grants` **without a token**, and
the service is called intra-cluster. Gating those paths would break Compliance and the read flows.
CS29 scopes the confused-deputy fix to exactly the request endpoints where a cross-tenant decision is
the escalation.

### Token forwarding from Bank.Web

`Bank.Web` registers the `IGovernanceClient` with
[`AccessTokenHandler`](../../src/AuthzEntitlements.Bank.Web/Clients/AccessTokenHandler.cs) (mirroring
the `BankApiClient`), so the signed-in user's bearer token reaches governance-service. When there is
no token the request goes out unauthenticated and governance answers 401/403 — the UI never
fabricates an identity.

## Audience (Decision 4)

The forwarded token is the **Bank.Web user's** Keycloak access token, which the realm's `bank-claims`
default client scope stamps with the **`bank-api`** audience (the `aud-bank-api` mapper in
[`authz-bank-realm.json`](../../infra/keycloak/authz-bank-realm.json)). Governance validates that
**same `bank-api` audience**, so the existing token is accepted with **no realm change** — the
least-invasive option. The AppHost injects `Keycloak__Authority` (the shared realm authority) and
`Keycloak__Audience=bank-api` into governance-service and adds `WaitFor(keycloak)`, mirroring
bank-api. `Keycloak:Audience` is configurable, so a future dedicated `governance-service` audience can
be adopted by adding a realm mapper and flipping one env var — without code changes.

## Documented follow-ups (explicitly NOT in CS29)

CS29 binds the **tenant** boundary to the token. These remain open:

1. **Within-tenant approver-identity binding.** Approve/reject still resolve the approver from the
   directory via `body.ApproverId`; the maker-checker/SoD logic is unchanged. Binding the approver to
   the token subject (`sub`) so a caller cannot approve *as* another same-tenant user is a separate
   hardening step.
2. **Broader principal/access scoping.** `/principals/{id}/grants|access` and the grant/revoke and
   review endpoints remain anonymous and un-scoped for the intra-cluster/Compliance read paths.
   Tenant-scoping those (e.g. via a service-to-service credential that still carries a tenant) is
   future work.
3. **Handler-integration regression tests.** The tenant boundary is covered by unit tests (the
   fail-closed `BelongsToTenant` contract) plus an endpoint-authorization-metadata test, and each
   request endpoint filters by tenant in its EF query. There is no handler-level integration test
   exercising list-filtering / cross-tenant 404 against persisted mixed-tenant data, because the repo
   carries no EF-InMemory / TestHost harness in CPM. Adding a lightweight handler-or-query-seam test
   to regression-lock the cross-tenant behavior end-to-end is future work (GPT-5.5 review note).

## Tests

- `tests/AuthzEntitlements.Governance.Service.Tests` — the tenant-claim reader + fail-closed equality
  (the exact list-filter and cross-tenant-decide decisions), the JWT-bearer options contract
  (`MapInboundClaims`, claim types, default/override audience, authority resolution, HTTPS-metadata,
  fail-closed-on-missing-authority), and an endpoint-metadata test proving exactly the five request
  endpoints require authorization while every other governance endpoint stays anonymous.
- `tests/AuthzEntitlements.Bank.Web.Tests` — the governance client forwards the user's bearer token
  (and sends none when the user has no token).

> **Test strategy note.** These are pure unit/metadata tests, matching this repo's convention of
> testing decisions without a web host. There is no in-memory EF provider, `Mvc.Testing`, or
> `TestHost` package in Central Package Management, and the `GovernanceDbContext` uses the
> Npgsql-specific `xmin` row-version, so a full request/response integration test would need a live
> Postgres + Keycloak. The decision logic (`RequireTenant` + `BelongsToTenant`) and the authorization
> **metadata** are covered directly instead.
