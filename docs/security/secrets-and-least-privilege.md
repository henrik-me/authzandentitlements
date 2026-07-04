# Secrets management & least-privilege review

## Purpose & scope

This document is the authoritative secrets-management and least-privilege reference for
**CS18 — Security hardening + threat model**. It is linked from
[`threat-model.md`](threat-model.md), which cites it as the detailed control write-up for
the STRIDE **Information Disclosure** and **Elevation of Privilege** categories.

This system is a **local-first dev/lab reference architecture**, not a production
deployment. Every service runs on the developer's box under .NET Aspire; the only
externally reachable surfaces are the ones a human demonstrator needs. Because the goal is
a *reproducible, deterministic* lab, some secrets are committed **on purpose** so a fresh
`aspire run` produces an identical, working environment with no manual setup.

The purpose of this review is therefore not to remove those dev secrets — they are correct
for the lab — but to:

1. **Inventory** every secret and credential the repository ships, with an exact
   `file:line` citation.
2. **Classify** each one as an Aspire-generated parameter (good) or a committed dev-only
   literal (must never ship to production).
3. Give **concrete, .NET/Aspire-specific production-hardening guidance** so a real adopter
   knows exactly what to change before deploying.

Every "current state" claim below is backed by a verified citation. Line numbers were
confirmed against the repository at the time of writing.

## Secrets inventory

| Secret | Location (`file:line`) | Type | Dev-only? | Production handling |
|---|---|---|---|---|
| Postgres password | `src/AuthzEntitlements.AppHost/AppHost.cs:45` (`AddPostgres`) | **Aspire-generated parameter** (good) | Generated per-run; not committed | Keep as an Aspire parameter; bind to a secrets backend (Key Vault) in deployed environments. |
| Postgres password (consumed) | `src/AuthzEntitlements.AppHost/AppHost.cs:67` (Unleash), `:166-167` (OpenFGA DSN) | Reference to the generated parameter (`postgres.Resource.PasswordParameter`) | Resolved at runtime, never a literal | No change — this is the pattern to copy. |
| `Keycloak:ClientSecret` = `bank-web-secret` | `src/AuthzEntitlements.AppHost/AppHost.cs:148` | Committed dev-only literal | **Yes** | Move to an Aspire parameter / `dotnet user-secrets` locally; inject from a secrets backend in prod; rotate. |
| `Keycloak:ClientSecret` = `bank-web-secret` | `src/AuthzEntitlements.Bank.Web/appsettings.Development.json:13` | Committed dev-only literal | **Yes** (Development-only config file) | Never place in a non-Development `appsettings*.json`; source from a secrets backend. |
| Unleash `INIT_ADMIN_API_TOKENS` = `*:*.unleash-insecure-admin-token` | `src/AuthzEntitlements.AppHost/AppHost.cs:69` | Committed dev-only literal (opt-in container) | **Yes** | Generate a strong admin token; store in a secrets backend; never seed via `INIT_*` env in prod. |
| Unleash `INIT_CLIENT_API_TOKENS` = `*:development.unleash-insecure-client-token` | `src/AuthzEntitlements.AppHost/AppHost.cs:70` | Committed dev-only literal (opt-in container) | **Yes** | Scope the client token to a single environment/project; source from a secrets backend. |
| Unleash client token echoed to entitlements-service | `src/AuthzEntitlements.AppHost/AppHost.cs:111` (`Entitlements__Unleash__ApiToken`) | Committed dev-only literal | **Yes** | Inject the scoped token from a secrets backend; never hardcode. |
| Keycloak `bank-web` client secret = `bank-web-secret` | `infra/keycloak/authz-bank-realm.json:183` | Committed dev-only literal (realm export) | **Yes** | Never commit a production realm export; set client secrets out-of-band and rotate. |
| Keycloak `bank-workload` client secret = `bank-workload-secret` | `infra/keycloak/authz-bank-realm.json:228` | Committed dev-only literal (realm export) | **Yes** | Same as above; workload identities should use rotated secrets or federated/workload identity. |
| Sample-user passwords = `Passw0rd!` (all 5 users, non-temporary) | `infra/keycloak/authz-bank-realm.json:261,287,313,339,367` | Committed dev-only literal (realm export) | **Yes** | Never seed real users with fixed passwords; use an IdP-backed directory; no committed credentials. |

### Aspire-parameter (good) vs. committed dev-only literal

The Postgres password is the model to follow: `AddPostgres("postgres")` at
`AppHost.cs:45` creates an **Aspire parameter** whose value is generated per run and never
committed. Both the Unleash container (`AppHost.cs:67`) and the OpenFGA datastore DSN
(`AppHost.cs:166-167`) resolve it at runtime via `postgres.Resource.PasswordParameter`
rather than baking a literal into source. Contrast this with every other row above, which
is a **committed literal** — acceptable for the lab, unacceptable for production.

## Why the dev secrets are safe *for the lab*

The committed dev secrets are a deliberate, defensible trade-off in this context:

- **Reproducibility.** The Keycloak realm is imported fresh on every start with no data
  volume (`AppHost.cs:95`, `WithRealmImport`), so the realm — including its clients and
  sample users — is deterministic. Fixed secrets are what make a one-command
  `aspire run` demo work identically on any machine.
- **Explicitly marked dev-only.** The realm sets `sslRequired: none`
  (`infra/keycloak/authz-bank-realm.json:4`) and self-registration is off (`:5`);
  `infra/keycloak/README.md:9-11` states in bold that the realm is dev-only and must not
  be used outside the local dev loop.
- **Private repository, no external exposure of the secret-bearing engines.** Per
  `.harness-known-constraints.md`, the repo is private (free tier). The Unleash and
  OpenFGA containers that carry insecure tokens are opt-in `.WithExplicitStart()`
  resources (`AppHost.cs:72,83,176,186`) that never start on a default `aspire run` and are
  not externally exposed.
- **No real data.** The five sample users, tenants (`CONTOSO`/`FABRIKAM`), and accounts are
  synthetic fixtures aligned to the domain seed; there is no PII or production data at risk.

## Production secrets guidance

Concrete, .NET/Aspire-specific steps a real adopter should take:

- **Local development:** keep secrets out of source. Use Aspire **parameters**
  (`builder.AddParameter("bank-web-secret", secret: true)`) and back them with
  `dotnet user-secrets` (`dotnet user-secrets set "Keycloak:ClientSecret" <value>`) so the
  value lives in the per-developer secret store, not in `appsettings*.json` or `AppHost.cs`.
- **Deployed environments:** inject secrets from the environment or a secrets backend.
  Bind Aspire parameters to **Azure Key Vault** (or the platform-native secret store) and
  reference them from resource wiring; do not pass literals via `WithEnvironment`.
- **Rotate Keycloak client secrets.** Treat `bank-web-secret` and `bank-workload-secret`
  as compromised (they are in git history) and issue fresh secrets for any real deployment.
  Prefer a rotated-secret or federated/workload-identity model for `bank-workload`.
- **Never commit a production realm export.** The dev realm at
  `infra/keycloak/authz-bank-realm.json` is fine to commit *because* it is dev-only. A
  production realm's clients and users must be provisioned out-of-band, with secrets set
  post-import and never serialized into a committed JSON.
- **Scope tokens narrowly.** Replace the wildcard Unleash tokens (`*:*...`) with
  environment- and project-scoped tokens; grant each token only the access it needs.

## Least-privilege review

| Surface | Current state (`file:line`) | Assessment | Production recommendation |
|---|---|---|---|
| Postgres credentials | Single shared server (`AppHost.cs:45`) with per-service databases `bank`, `openfga`, `entitlements`, `governance`, `audit`, `unleash` (`:48-59`), but every consumer connects as the **`postgres` superuser** (`:66` Unleash `DATABASE_USERNAME=postgres`; `:167` OpenFGA DSN `postgres://postgres:...`) | Separation is per-**database**, not per-**credential** — a compromise of any one service yields superuser access to all databases | Create a **per-service role** that owns/accesses only its own database with the minimal grants (`CONNECT` + table DML); no shared superuser. |
| Network exposure | Only Grafana (`AppHost.cs:37`), `edge-gateway` (`:142`), and `bank-web` (`:151`) call `WithExternalHttpEndpoints()`. `bank-api`, `entitlements-service`, `audit-service`, `authz-pdp`, and `governance-service` have **no** external endpoint and are internal by default | Attack surface is already small; **Bank.Api is internal-only behind the Edge.Gateway** — a good posture (see note below) | Keep Bank.Api internal; expose only the gateway and the web UI. Terminate TLS at the edge. |
| OTLP telemetry ingest | Grafana's OTLP ports (4317/4318) are modeled as `tcp` endpoints, so `WithExternalHttpEndpoints()` marks only the Grafana **UI** external, not the ingest ports (`AppHost.cs:32-37`) | No off-box telemetry-injection surface | Keep OTLP ingest internal; authenticate collectors in prod. |
| Grafana kiosk | `GF_AUTH_ANONYMOUS_ENABLED=true`, `GF_AUTH_ANONYMOUS_ORG_ROLE=Editor`, `GF_AUTH_DISABLE_LOGIN_FORM=true`, `GF_AUTH_BASIC_ENABLED=false` (`AppHost.cs:27-30`) | Intentional: anonymous **capped Editor** with the login form *and* Basic Auth disabled, so the image-default `admin/admin` cannot be used to escalate | For any shared/deployed Grafana, disable anonymous access and configure real auth (OIDC/SSO) with least-privilege org roles. |
| HTTPS metadata enforcement | `RequireHttpsMetadata = !environment.IsDevelopment()` in both JWT setups (`src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs:115`, `src/AuthzEntitlements.Edge.Gateway/Auth/GatewayAuthenticationSetup.cs:116`) | **Existing prod-safety control** — HTTPS metadata is required outside Development | Keep; ensure the deployed issuer is HTTPS so metadata retrieval is protected. |
| Opt-in policy/flag engines | Unleash (`AppHost.cs:72`), OPA (`:83`), OpenFGA migrate/server (`:176,:186`) all use `.WithExplicitStart()` and are off the default `aspire run` path | Their insecure dev tokens are never live on a default run | If enabled in prod, replace insecure tokens and put the engines behind internal network policy. |

### Note — Bank.Api exposure (briefing correction)

The CS18 reconnaissance briefing stated that `Bank.Api` is `WithExternalHttpEndpoints()`.
Verification against `AppHost.cs:115-124` shows Bank.Api has **no** external endpoint — only
Grafana (`:37`), `edge-gateway` (`:142`), and `bank-web` (`:151`) are external. Bank.Api is
therefore **already internal-only behind the Edge.Gateway**, which is the desired hardened
posture. No change is required; this is called out so the threat model reflects the
verified topology rather than the briefing's assumption.

## Production hardening checklist

- [ ] Rotate and relocate every committed dev secret (`bank-web-secret`,
      `bank-workload-secret`, the Unleash `*-insecure-*` tokens, sample-user passwords).
- [ ] Source all secrets from Aspire parameters + `dotnet user-secrets` (local) and a
      secrets backend such as Azure Key Vault (deployed); no literals in `AppHost.cs` or
      `appsettings*.json`.
- [ ] Provision a **per-service Postgres role** with least-privilege grants on only its own
      database; stop using the shared `postgres` superuser for application connections.
- [ ] Never commit a production Keycloak realm export; set client secrets and user
      credentials out-of-band after import.
- [ ] Keep `Bank.Api` internal-only behind the `Edge.Gateway`; expose only the gateway and
      web UI, and terminate TLS at the edge (HTTPS everywhere).
- [ ] Replace the Grafana anonymous-Editor kiosk with real authenticated access
      (OIDC/SSO) and least-privilege org roles for any shared or deployed instance.
- [ ] Scope Unleash (and any engine) tokens to a single environment/project; enable the
      opt-in engines only behind internal network policy with rotated credentials.
- [ ] Confirm `RequireHttpsMetadata` stays enforced (it is, outside Development) and that
      the deployed OIDC issuer is served over HTTPS.

## References

- [`threat-model.md`](threat-model.md) — the CS18 STRIDE threat model that links here.
- [`../identity/entra-id.md`](../identity/entra-id.md) — identity provider mapping (the
  production path away from the dev Keycloak realm).
- [`../observability/observability-stack.md`](../observability/observability-stack.md) —
  the Grafana/OTLP observability stack whose kiosk trade-off is reviewed above.
- [`../../.harness-known-constraints.md`](../../.harness-known-constraints.md) — the
  private-repo / free-tier constraint context for the lab posture.
