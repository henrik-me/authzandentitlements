# Keycloak realm: `authz-bank`

This directory holds the Keycloak **realm export** (`authz-bank-realm.json`) that the
Keycloak dev container imports at startup. The realm supplies the **AuthN layer**
of the four-layer authz model in `ARCHITECTURE.md`: it issues OIDC tokens whose
claims (`tenant`, `branch`, `roles`, `scope`, `aud`) drive the downstream
coarse-grained scope checks (CS04) and the domain/PDP authorization layers.

It is deliberately dev-only: `sslRequired` is `none`, self-registration is off,
and all secrets/passwords are fixed lab values. **Do not use this realm outside
the local dev loop.**

## How it is imported

The Aspire AppHost starts `quay.io/keycloak/keycloak` (v26.x) and mounts this
directory as a realm import, e.g.:

```csharp
var keycloak = builder.AddKeycloak("keycloak")
    .WithRealmImport("../../infra/keycloak")
    // Keycloak 26's default-on "organization" feature crashes service-account
    // import ("Session not bound to a realm"); the lab does not use it.
    .WithEnvironment("KC_FEATURES_DISABLED", "organization");
```

Keycloak imports every `*-realm.json` in the mounted directory on first start, and
the file name must match the realm it declares (`<realm>-realm.json`), so
`authz-bank-realm.json` becomes the `authz-bank` realm. The realm is aligned with
the CS02 domain seed (`src/AuthzEntitlements.Bank.Api/Data/BankSeeder.cs`): each
user's realm `id` equals the Bank.Api `User.Id`, so the JWT `sub` correlates
directly to the seeded domain user.

## Users and credentials

All users share the lab password **`Passw0rd!`** (non-temporary). The realm `id`
equals the Bank.Api `User.Id` (JWT `sub`).

| username | sub (User.Id) | tenant | branch | realm role |
|---|---|---|---|---|
| `teller1` | `40000000-0000-0000-0000-000000000001` | CONTOSO | NM01 | Teller |
| `manager1` | `40000000-0000-0000-0000-000000000002` | CONTOSO | NM01 | BranchManager |
| `compliance1` | `40000000-0000-0000-0000-000000000003` | CONTOSO | NM01 | ComplianceOfficer |
| `auditor1` | `40000000-0000-0000-0000-000000000004` | CONTOSO | NM01 | Auditor |
| `teller1-fabrikam` | `40000000-0000-0000-0000-000000000005` | FABRIKAM | FH01 | Teller |

Usernames must be realm-unique, so the Fabrikam teller is `teller1-fabrikam`
(the domain seed reuses `teller1` under a different tenant).

## Clients

| clientId | type | secret | flows | purpose |
|---|---|---|---|---|
| `bank-web` | confidential | `bank-web-secret` | authorization-code + direct-access-grant | Login for the Bank.Web stub; the direct-access (password) grant lets the orchestrator fetch tokens for verification. |
| `bank-api` | bearer-only resource server | — | none | Audience for the access token; validates only. |
| `bank-workload` | confidential service account | `bank-workload-secret` | client-credentials | Non-human workload identity (used later by CS19); its service-account user carries `tenant=CONTOSO`. |

`bank-web` default client scopes include `bank.read` plus the `tenant`/`branch`/
`roles` mappers; the write scopes (`bank.transactions.write`,
`bank.approvals.write`) are **optional** scopes requested per call.

## Realm roles

`Teller`, `BranchManager`, `ComplianceOfficer`, `Auditor` — the exact
`RoleNames` strings from `src/AuthzEntitlements.Bank.Api/Domain/BankPolicy.cs`.

## Emitted token claims

The `bank-claims` client scope (a default scope on `bank-web` and `bank-workload`)
adds protocol mappers so the **access token** carries:

| claim | source | shape |
|---|---|---|
| `tenant` | user attribute `tenant` | string (`CONTOSO` / `FABRIKAM`) |
| `branch` | user attribute `branch` | string (branch `Code`, e.g. `NM01`) |
| `roles` | realm roles | multivalued string array (also in the id token; in addition to the default `realm_access.roles`) |
| `aud` | audience mapper | includes `bank-api` |

OAuth scopes surface in the standard `scope` string: `bank.read`,
`bank.transactions.write`, `bank.approvals.write`. CS04's edge gateway enforces
coarse-grained scope checks from that `scope` string; Bank.Api validates the
`aud` and `roles` claims and enforces the `tenant` claim on every endpoint. The
`branch` claim is carried for later branch-scoped (ABAC) authorization and is not
yet enforced by Bank.Api.
