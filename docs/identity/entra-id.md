# Microsoft Entra ID — the real-world identity path

> **Scope:** how the CS03 Keycloak OIDC setup maps to **Microsoft Entra ID**
> (formerly Azure AD) as the production-grade alternative. See
> [ARCHITECTURE.md](../../ARCHITECTURE.md) layer 0 (AuthN) and the CS03
> design contract (`project/clickstops/active/active_cs03_authn-keycloak-oidc.md`).

## Why Keycloak for the lab, Entra ID for production

The lab runs **Keycloak** as an Aspire container so the whole identity provider —
realm, users, roles, scopes, and custom claims — is reproducible, offline, and
version-controlled (`infra/keycloak/`). That makes the AuthN layer a
first-class, inspectable part of the solution rather than an external
dependency.

In a real deployment you would not run your own IdP for a line-of-business app;
you would use your organization's **Microsoft Entra ID** tenant. The good news
is that the application code is **authority-driven**: it validates whatever
tokens the configured OIDC authority issues and reads a small set of claims. It
does not hard-code Keycloak semantics. So moving from Keycloak to Entra ID is
almost entirely a **configuration** change — the `JwtBearer` / `OpenIdConnect`
wiring is identical (see [What the app code keeps](#what-the-app-code-keeps)).

Both are standard OpenID Connect / OAuth 2.0 providers, so the flows
(authorization code + PKCE for users, client credentials for workloads) and the
token-validation model (issuer, audience, signing keys via JWKS discovery) are
the same. What differs is *where each concept lives* in the provider and *how
custom claims are emitted*.

## Concept mapping (Keycloak → Entra ID)

| CS03 Keycloak concept | Microsoft Entra ID equivalent |
|---|---|
| Realm `authz-bank` (isolation boundary) | The **tenant** (directory). A tenant is the top-level isolation boundary; you do not create per-app realms. |
| Realm roles `Teller`, `BranchManager`, `ComplianceOfficer`, `Auditor` | **App roles** (`appRoles` in the app registration manifest), assigned to users or groups. They surface in the `roles` claim. *Alternative:* security **groups** + a groups claim, but app roles are app-scoped and preferred here. |
| Client scopes `bank.read`, `bank.transactions.write`, `bank.approvals.write` | **Exposed API scopes** (delegated permissions) defined on the `bank-api` app registration under **Expose an API**; consented by `bank-web`. Emitted in the `scp` claim. |
| Protocol mappers for custom claims `tenant`, `branch` | **Optional claims** and/or **claims-mapping policies** backed by **directory extension attributes** / schema extensions. `roles` comes from app-role assignment, not a mapper. |
| Custom `roles` top-level claim (from realm roles) | The `roles` claim, populated automatically from **app-role assignments** — no mapper needed. |
| Client `bank-web` (confidential, auth code + PKCE) | A confidential-client **app registration** with a **Web** platform: redirect URIs + a client **secret or certificate**. Requests the `bank-api` scopes. |
| Client `bank-api` (bearer-only resource server) | An **app registration that exposes an API**: an **Application ID URI** `api://<app-id>`, plus the scopes and app roles it defines. It validates tokens; it does not sign in users. |
| Client `bank-workload` (client-credentials service account) | An **app registration** granted **application permissions** (app roles exposed for daemons) on `bank-api`, using the **client-credentials** flow. Admin-consented, no user present. |

### Notes on the mapping

- **Roles vs. groups.** Keycloak realm roles map most cleanly to Entra **app
  roles**, because app roles are defined *on the application* and travel in the
  `roles` claim regardless of directory structure. Security groups are an
  option, but group claims can be truncated for large memberships and are not
  app-scoped, so app roles are the recommended path for the `Teller` /
  `BranchManager` / `ComplianceOfficer` / `Auditor` model.
- **Scopes (delegated) vs. app roles (application).** For a *user* signing in
  through `bank-web`, `bank-api`'s delegated **scopes** (`scp`) express what the
  app may do on the user's behalf. For a *daemon* like `bank-workload`, there is
  no user, so access is expressed as **application permissions** (app roles in
  the `roles` claim) via client credentials. This mirrors the Keycloak split
  between OAuth client scopes and the service-account client.

## Token-validation differences

The validation model is identical in shape (verify signature, issuer,
audience, expiry); only the concrete values and the source of custom claims
differ.

| Aspect | Keycloak (CS03) | Microsoft Entra ID (v2.0) |
|---|---|---|
| Issuer (`iss`) | `https://<keycloak-host>/realms/authz-bank` (fixed endpoint so `iss` is stable) | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| Audience (`aud`) | the `bank-api` client id / configured audience | the `bank-api` **Application ID URI** `api://<app-id>` (or the app's client id, depending on token version/config) |
| Signing keys (JWKS) | discovered from the realm OIDC metadata document | discovered from the tenant OIDC metadata document (`.../v2.0/.well-known/openid-configuration`) |
| Roles | custom top-level `roles` claim + `realm_access.roles` | `roles` claim, populated from **app-role assignments** |
| Scopes | `scope` claim (`bank.read`, …) | `scp` claim (space-delimited delegated scopes) |
| Custom attributes (`tenant`, `branch`) | protocol mappers write them directly into the access token | **optional claims** and/or **claims-mapping policies** over directory extension attributes; must be explicitly configured |
| Dev-only relaxations | `RequireHttpsMetadata=false` (lab only) | always HTTPS; never relax metadata validation |

Practical implications when switching to Entra ID:

- Point `Authority` at the tenant v2.0 endpoint; JWKS and issuer are discovered
  automatically from the OIDC metadata document.
- Set `Audience` to `api://<app-id>` (the `bank-api` Application ID URI).
- Read `scp` for delegated scopes (Entra) where the lab reads `scope`.
- `tenant` and `branch` are **not** emitted by default — configure **optional
  claims** (and, where directory extension attributes are involved, a
  **claims-mapping policy**) so those claims appear in the token. Until then,
  code that depends on them must treat their absence as unauthorized
  (fail-closed), consistent with the domain layer never trusting caller-supplied
  tenant.

## What the app code keeps

The ASP.NET Core wiring does **not** change between Keycloak and Entra ID:

- `AddAuthentication().AddJwtBearer(...)` on the resource server (`bank-api`).
- `AddOpenIdConnect(...)` for the interactive sign-in in `bank-web`
  (authorization code + PKCE).
- The authorization policies (`Teller` / `BranchManager` /
  `ComplianceOfficer` / `Auditor`, the read/write scope policies, and the
  per-endpoint `tenant`-claim check) are unchanged.

Only **configuration** changes. The CS03 config keys and their Entra ID
equivalents:

| CS03 config key | Meaning | Entra ID equivalent value |
|---|---|---|
| `Keycloak:Authority` | OIDC authority the token issuer must match | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `Keycloak:Realm` | realm segment of the issuer | not applicable — the tenant id in the authority replaces it |
| `Keycloak:Audience` | expected `aud` on the access token | `api://<app-id>` (the `bank-api` Application ID URI) |

Beyond those, only the **claim names** differ where noted (`scp` vs `scope`),
and custom claims (`tenant`, `branch`) require the optional-claims /
claims-mapping-policy configuration described above. The token-driven,
authority-agnostic design is what makes the Keycloak-to-Entra move a
configuration exercise rather than a rewrite.
