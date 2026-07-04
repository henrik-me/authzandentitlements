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
| `bank-agent` | confidential service account | `bank-agent-secret` | client-credentials | AI agent / MCP tool workload identity (CS19). Carries `subject_type=agent`; default scope `agent.bank.read`, with the write/approval delegated scopes requested per token. |

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

## Agent / non-human access (CS19)

The realm also carries an **AI-agent / MCP-tool workload identity** so agents can be
authorized alongside humans, with on-behalf-of (OBO) delegation. The design contract is in
[agent-and-nonhuman-access](../../docs/authz/agent-and-nonhuman-access.md); this section
covers only the realm surface.

### The `bank-agent` client

`bank-agent` is a **confidential service account** (secret `bank-agent-secret`) that uses the
**client-credentials** grant — no interactive login, no user password. It mirrors
`bank-workload`'s shape but is scoped for delegated agent capabilities and is **not**
`fullScopeAllowed` (least privilege). Its token carries a hardcoded `subject_type=agent`
claim (from the `agent-claims` default scope) so a resource server or PDP can distinguish a
non-human caller from a human.

### Delegated `agent.bank.*` scopes (default read; write/approvals optional)

The delegated capability scopes are **distinct** from the human `bank.*` scopes: an agent
holds them to act *for* a user, and it can never exceed the user's own rights.

| scope | on `bank-agent` | grants (delegated) |
|---|---|---|
| `agent.bank.read` | **default** | read on behalf of a user |
| `agent.bank.transactions.write` | optional | create account / transaction on behalf of a user |
| `agent.bank.approvals.write` | optional | approve / reject on behalf of a user |

Only `agent.bank.read` is a **default** client scope, so a plain agent token is read-only.
The write and approval scopes are **optional** and must be requested per token — this is the
"scoped, time-boxed agent tokens" property: an elevated capability is minted only for the
call that needs it, not carried by default.

### Obtaining an agent token

Use the client-credentials grant against the realm token endpoint, requesting any optional
delegated scopes needed for the action:

```text
POST /realms/authz-bank/protocol/openid-connect/token
grant_type=client_credentials
client_id=bank-agent
client_secret=bank-agent-secret
scope=agent.bank.transactions.write        # optional — omit for a read-only agent token
```

The resulting access token carries `subject_type=agent` and the requested `agent.bank.*`
scopes in its `scope` string.

### On-behalf-of (OBO)

Production OBO uses **OAuth 2.0 Token Exchange (RFC 8693)**: the agent exchanges its token
for one whose `sub` is the **user** it acts for and whose `act` / `on_behalf_of` names the
**agent**, so the resource server sees both the effective user and the acting delegate. In
this **offline, deterministic** lab the OBO binding is modeled at the app / PDP layer (see
[agent-and-nonhuman-access](../../docs/authz/agent-and-nonhuman-access.md)); enabling
Keycloak's preview token-exchange feature is therefore **not required** to run the demo.

