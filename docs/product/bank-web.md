# Bank.Web — Blazor fintech product UI (CS14)

`AuthzEntitlements.Bank.Web` is the product-facing Blazor application that
demonstrates one fintech back-office workflow travelling through **four
authorization layers**, each surfacing a visible outcome:

1. **AuthN** — Keycloak OIDC sign-in issues an access token carrying the user's
   roles, tenant, and scopes (built in CS03).
2. **Coarse gateway** — the Edge.Gateway (YARP, CS04) validates token + scope +
   tenant before routing to the domain API.
3. **Fine authorization** — Bank.Api (CS02) enforces maker-checker, segregation
   of duties (SoD), and tenant scoping on each resource; the standalone PDP
   (CS05) answers the AuthZEN decision contract.
4. **Entitlements** — the commercial Entitlements.Service (CS10) gates features
   by the tenant's subscription plan; the Governance.Service (CS11) runs the
   JIT access-request + SoD flow.

It replaces the CS03 minimal-API login stub, preserving that stub's exact
Keycloak/OIDC + cookie wiring.

> **See also:** the [AuthZ Playground & Audit Explorer](authz-playground-and-audit-explorer.md)
> (CS15) — the `/playground` engine-comparison and `/audit` decision-log
> surfaces layered on top of this app.

## Rendering & token strategy

Bank.Web is a **Blazor Web App with Interactive Server** components enabled
(`AddInteractiveServerComponents()` / `AddInteractiveServerRenderMode()`).

- **Token-forwarding pages render as static SSR** (no `@rendermode`). Calls to
  Bank.Api go **through the edge gateway** and must carry the user's bearer
  token; `AccessTokenHandler` reads it from `IHttpContextAccessor` via
  `GetTokenAsync("access_token")`. A static-SSR component executes during the
  HTTP request, so `HttpContext` (and the token) are present; an Interactive
  Server circuit has no per-event `HttpContext`, so token access would fail.
  Interactive write forms therefore use static SSR `EditForm` +
  `[SupplyParameterFromForm]` + `FormName`, which re-run the component
  server-side on POST.
- **One Interactive Server island** (`EntitlementChecker`) demonstrates the
  interactive render mode. It calls the **anonymous** Entitlements.Service (no
  token needed) and reads the tenant from the cascaded `AuthenticationState`
  (never `ICurrentUser`, which depends on `HttpContext`).

## Identity mapping (the glue)

| Concept | Source | Notes |
|---|---|---|
| Bank user GUID (Maker/Checker id) | `ICurrentUser.ResolveBankUserIdAsync()` — `GET /api/users` matched on `preferred_username` | The match returns the Bank user row id, which **equals** the token `sub` because the Keycloak realm user `id`s are aligned with the Bank seed GUIDs (e.g. `teller1` = `40000000-0000-0000-0000-000000000001`). Bank.Api rejects a create/decide whose `MakerId`/`CheckerId` ≠ token `sub` with **403**. |
| Tenant code | the `tenant` claim | `CONTOSO` = Professional plan, `FABRIKAM` = Standard plan. Used for entitlement lookups. |
| Governance principal | `user-{preferred_username}` | Matches the Governance seed principals (`user-teller1`, …). |
| Roles | the `roles` claim | Checker-eligible roles are `BranchManager` and `ComplianceOfficer`. |

**Security invariant:** maker/checker/approver/requester identity is always
derived from the token, never from a form field — a caller may not act as
another subject. UI role hints are convenience only; the server enforces every
rule independently (defense in depth).

## Pages

| Route | Auth | Render | Purpose |
|---|---|---|---|
| `/` | anonymous | static | Landing: explains the four layers + sign-in state. |
| `/claims` | `[Authorize]` | static | The signed-in user's identity claims. |
| `/accounts` | `[Authorize]` | static SSR | Read vertical slice: accounts via the gateway (AuthN → coarse → tenant-scoped read). |
| `/accounts/{id}` | `[Authorize]` | static SSR | Account detail + its transactions. |
| `/transactions/new` | `[Authorize]` | static SSR | **Maker** create-transaction form; surfaces coarse/fine/entitlement/validation outcomes. |
| `/approvals` | `[Authorize]` | static SSR | **Checker** approve/reject; surfaces SoD (409), role ineligibility (403), decide-once. |
| `/entitlements` | `[Authorize]` | static SSR + interactive island | Plan summary + feature gates; live interactive feature check. |
| `/access-requests` | `[Authorize]` | static SSR | JIT access-request flow with SoD governance. |

## Typed clients

All clients use Aspire service discovery base addresses and the shared
`AddServiceDefaults()` resilience/discovery handlers. Reads fail closed to
empty/null; writes capture the outcome into `ApiResult<T>`
(`IsSuccess`/`StatusCode`/`Value`/`Error`) without throwing on non-2xx, so
denials render as visible outcomes.

| Client | Base address | Auth | Endpoints |
|---|---|---|---|
| `IBankApiClient` | `https+http://edge-gateway` | bearer (via `AccessTokenHandler`) | accounts, transactions, users, create/approve/reject |
| `IEntitlementsClient` | `https+http://entitlements-service` | anonymous | plan, feature |
| `IGovernanceClient` | `https+http://governance-service` | anonymous | access-packages, requests, approve/reject, principal access |
| `IPdpClient` | `https+http://authz-pdp` | anonymous | native AuthZEN `POST /api/authz/evaluate` |

## Demo users (realm `authz-bank`, password `Passw0rd!`)

| Username | Roles | Tenant | Can create txn | Can decide approval |
|---|---|---|---|---|
| `teller1` | Teller | CONTOSO | yes | no (403 — not checker-eligible) |
| `manager1` | BranchManager | CONTOSO | yes | yes |
| `compliance1` | ComplianceOfficer | CONTOSO | yes | yes |
| `auditor1` | Auditor | CONTOSO | no read-write | no |
| `teller1-fabrikam` | Teller | FABRIKAM | yes (but high-value gated by Standard plan) | no |

**End-to-end demo:** sign in as `teller1`, create a transaction ≥ 10,000 (routed
to approval); sign in as `manager1` and approve it. Try approving your own
transaction to see the **409 segregation-of-duties** denial. As
`teller1-fabrikam`, a high-value transaction is denied by the **entitlement**
layer (Standard plan lacks `high-value-transactions`), while `teller1`
(CONTOSO / Professional) succeeds.

## Running

Bank.Web is wired into the Aspire AppHost with service-discovery references to
`edge-gateway`, `entitlements-service`, `governance-service`, and `authz-pdp`
(plus `keycloak`). Run the full stack with the Aspire CLI (requires Docker for
Keycloak + Postgres):

```
aspire run
```

Then open the `bank-web` endpoint from the Aspire dashboard and sign in.

## Testing

`tests/AuthzEntitlements.Bank.Web.Tests` covers the typed clients (against a
stub `HttpMessageHandler`), `ApiResult` mapping, `AccessTokenHandler`,
`CurrentUser` identity/GUID resolution, and the page view-models
(`NewTransactionInput`, `ApprovalsModel`, `EntitlementsModel`,
`AccessRequestsModel`). All tests run offline (no Docker/Keycloak):

```
dotnet test tests/AuthzEntitlements.Bank.Web.Tests/AuthzEntitlements.Bank.Web.Tests.csproj
```
