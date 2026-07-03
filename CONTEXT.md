# Project Context

> **Last updated:** _(set when consumer first edits)_

## Codebase state

Bootstrap + pre-flight complete (2026-07-02). Git initialized on `main`; remote `origin` points to
`github.com/henrik-me/authzandentitlements` (pushed to GitHub, private). **agent-harness
v0.12.0 adopted** (process docs, review gates, linters, CI workflow). The full **27-CS plan**
is authored in `project/clickstops/planned/` with explicit Phase / Lane / Depends-on.

**CS01 (Aspire foundations) complete** (PR #2, merged 2026-07-03). The .NET Aspire solution
skeleton is scaffolded: `AuthzEntitlements.AppHost` (orchestration) + `AuthzEntitlements.ServiceDefaults`
(OTel/health/resilience) on **.NET 10 / Aspire 13**, with a single PostgreSQL resource exposing the
five logical DBs (`bank`, `openfga`, `entitlements`, `governance`, `audit`), Central Package Management
(`Directory.Packages.props`), shared `Directory.Build.props`, and a `global.json` pinning the .NET 10 SDK.
`dotnet build` is 0/0 and `aspire run` brings up the dashboard + a healthy Postgres container. Known-vulnerable
preview packages are handled with per-advisory `NuGetAuditSuppress` (tracked in LRN-003 for CS18).

**CS02 (fintech domain skeleton) complete** (PR #5, merged 2026-07-03). `AuthzEntitlements.Bank.Api`
(ASP.NET Core minimal APIs, net10.0) implements the fintech back-office domain — `Tenant → Region → Branch`,
`User`/`Role`/`UserRole` (RBAC), `Account`, `Transaction`, `Approval` — on **EF Core 10 (RC1) + Npgsql**
against the `bank` DB, with an `InitialCreate` migration, an idempotent deterministic seeder
(teller/manager/compliance/auditor users + posted / pending / manager-approved transaction scenarios), and
minimal CRUD + approve/reject endpoints. **Maker-checker + segregation-of-duties** are enforced (threshold
→ `Pending` + `Approval`; `checker != maker`; checker-role eligibility; tenant-scoped checker); tenant/branch
attributes are derived-from-account + FK-enforced; approval decisions use an `xmin` optimistic-concurrency
token. `dotnet build` 0/0, `dotnet test` 7/7; runtime-verified against Postgres 17. A new transitive advisory
(CVE-2025-55247, MSBuild via EF Core Design) was **remediated via a patched-version CPM transitive pin**
(LRN-005; drop in CS18). New learnings LRN-004..007 filed.

**CS03 (AuthN via Keycloak OIDC) complete** (PR #12, merged 2026-07-03). A Keycloak Aspire container
(fixed host port, `WithRealmImport`, `KC_FEATURES_DISABLED=organization`) imports the `authz-bank` realm
whose roles/users/branches/tenants/claims/clients mirror the CS02 seed (Keycloak user id = Bank.Api
`User.Id`, so JWT `sub` correlates). `AuthzEntitlements.Bank.Api` validates Keycloak JWTs
(`MapInboundClaims=false`, env-gated HTTPS metadata) and enforces role/scope/tenant authorization as an
**outer gate** over the CS02 maker-checker/SoD/tenant rules: reads are tenant-scoped, writes are scope+role
gated, and maker/checker + tenant are **bound to the token** (never caller-supplied) and fail-closed.
`AuthzEntitlements.Bank.Web` is an OIDC (code+PKCE) login stub; `docs/identity/entra-id.md` maps the setup to
Microsoft Entra ID. `dotnet build` 0/0, `dotnet test` 28/28; runtime-verified end-to-end against real Keycloak
(token claims for all 5 users + authz matrix + full `aspire run`). New learnings LRN-008..012 filed.

**CS04 (coarse-grained edge gateway) complete** (PRs #17 + #19, merged 2026-07-03). A new `AuthzEntitlements.Edge.Gateway`
(ASP.NET Core + YARP `Yarp.ReverseProxy` 2.3.0) fronts `Bank.Api` and enforces **coarse-grained** authorization at the
edge before routing — valid JWT + audience `bank-api` + the scope a route class needs (`bank.read` /
`bank.transactions.write` / `bank.approvals.write`) + `tenant`-claim presence — mirroring the CS03 token contract
(`MapInboundClaims=false`, shared `ResolveAudience`). Four coarse policies + a `TenantPresenceRequirement`; YARP routes
map each Bank.Api route class to its policy; the edge-authorized marker lives in the YARP proxy pipeline so routed
requests audit as `allow/routed`, edge short-circuits as denies, and unmatched (404)/method-mismatch (405) requests are
not audited. **Both gates emit structured, audit-ready authorization-decision events** (edge: `GatewayAuditMiddleware`
+ `gateway.decisions` meter; fine: `Bank.Api` `BankAuthorizationAuditMiddleware`) plus OTel — CS13 ingests later.
Boundary doc at `docs/architecture/coarse-vs-fine-boundary.md`. Build 0/0; gateway + Bank.Api unit suites pass;
runtime-verified against live Keycloak (401 no-token; scope-differentiated allow/deny; 405 not audited). GPT-5.5
rubber-duck (edge R1–R6) + Copilot reviewed. New learnings LRN-013..014.

**Next claimable:** CS05 (AuthZEN-aligned PDP abstraction) and CS12 (observability stack) — Wave 2, depend on CS02
(CS10 is active). CS04 (with CS03/CS06/CS10/CS11) advances toward CS14 (Blazor product UI) and, with CS05, CS18
(security hardening). `harness lint` is green; the remaining CSs carry an independent GPT-5.5 `## Plan review` attestation.

## Constraints

See `.harness-known-constraints.md` for repository tier and disposition (detected 2026-07-02T18:34:27.975Z).

## Architecture pointer

See [ARCHITECTURE.md](ARCHITECTURE.md).

_(Keep this section as a short pointer. All architecture detail lives in
`ARCHITECTURE.md`. Add a sentence or two summarising the top-level design only
if it helps orient a reader skimming this file.)_

## Blockers / open questions

- **Branch-protection ruleset not applied.** `infra/main-protection-ruleset.json` requires a public repo or GitHub Pro (private repo returns HTTP 403); by decision the repo stays **private**, so the review-gate workflows run on PRs as **CI only (not required-to-merge)**. Apply the ruleset later if the repo goes public or the plan upgrades.
- **Docker daemon must be running** for the container-based engines/infra (Docker Desktop is installed).
- **Plan review: complete** — all 27 CSs independently reviewed (GPT-5.5) and attested (Go / Go-with-amendments); 13 CSs were amended to fix dependency/scope gaps found in review. No plan-review blocker remains.
- **Open decision:** whether to promote SpiceDB from the Phase-7 expansion (CS26) into the Phase-2 core for a direct OpenFGA head-to-head.

## CS plan

The full clickstop queue lives in `project/clickstops/planned/` (**27 CSs**). Each CS file
carries **Phase**, **Lane**, and **Depends on** fields. A claim-time `## Plan review`
(independent reviewer) is required before a CS moves to `active/`.

### Dependency + lane map

| CS | Title | Lane | Depends on |
|----|-------|------|-----------|
| CS01 | Aspire solution foundations | Foundation | None |
| CS02 | Fintech domain skeleton | Foundation | CS01 |
| CS03 | AuthN via Keycloak OIDC | Identity | CS02 |
| CS04 | Coarse-grained edge gateway | Identity | CS03 |
| CS05 | AuthZEN-aligned PDP abstraction | PDP-core (hub) | CS02 |
| CS06 | Adapters: ASP.NET + Casbin | Engines | CS05 |
| CS07 | Adapter: OpenFGA (ReBAC) | Engines | CS05 |
| CS08 | Adapter: OPA / Rego | Engines | CS05 |
| CS09 | Adapter: Cedar | Engines | CS05 |
| CS10 | Commercial entitlements | Entitlements | CS02 |
| CS11 | Governance entitlements | Entitlements | CS02, CS08 |
| CS12 | Observability stack | Observability | CS02 |
| CS13 | Audit log pipeline | Observability | CS05 |
| CS14 | Blazor product UI | Product | CS03, CS04, CS06, CS10, CS11 |
| CS15 | AuthZ playground + audit explorer | Product | CS06, CS07, CS08, CS09, CS13 |
| CS16 | Explainability (why allowed/denied) | Cross-cutting | CS05, CS06, CS07, CS08, CS09 |
| CS17 | Policy lifecycle + testing | Cross-cutting | CS05, CS06, CS07, CS08, CS09 |
| CS18 | Security hardening + threat model | Cross-cutting | CS04, CS05 |
| CS19 | Agent + non-agent access | Cross-cutting | CS03, CS05, CS13, CS14 |
| CS20 | Migration & portability | Cross-cutting | CS05, CS06, CS07, CS08 |
| CS21 | Break-glass, delegation & OBO | Cross-cutting | CS05, CS11, CS13, CS14, CS19 |
| CS22 | Compliance mapping | Cross-cutting | CS11, CS12, CS13 |
| CS23 | Comparison matrix + survey | Eval | CS15, CS24 |
| CS24 | Performance benchmark + tracking | Eval | CS06, CS07, CS08, CS09, CS12 |
| CS25 | Managed-vs-self-host TCO | Eval | CS23, CS24 |
| CS26 | Expansion engines | Expansion | CS05, CS15 |
| CS27 | Full OpenMeter + Azure deploy | Expansion | CS10, CS12, CS14, CS15, CS25 |

### Parallelization (waves)

- **Wave 1 (serial foundation):** CS01 -> CS02.
- **Wave 2 (parallel after CS02):** CS03 (Identity), CS05 (PDP hub), CS10 (Entitlements), CS12 (Observability).
- **Wave 3 (after CS05):** engine adapters CS06, CS07, CS08, CS09 (parallel); CS04 (after CS03); CS13 audit (after CS05); CS11 (after CS02 + CS08).
- **Wave 4 (after engines):** CS16, CS17, CS20 (need engine behavior); CS24 (engines + CS12); CS22 (CS11+CS12+CS13); CS14 product (CS03+CS04+CS06+CS10+CS11); CS15 playground (engines + CS13); CS18 (CS04+CS05).
- **Wave 5:** CS19 (CS13+CS14), CS23 (CS15+CS24), CS26 (CS05+CS15).
- **Wave 6:** CS21 (CS19+CS13+CS14), CS25 (CS23+CS24), CS27 (CS14+CS15+CS25).

A fleet of orchestrators can each `harness claim` an independent, dependency-satisfied CS.
