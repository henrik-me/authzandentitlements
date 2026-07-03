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
**Next claimable: CS02 (fintech domain skeleton).** `harness lint` is green; all 27 CSs carry an
independent GPT-5.5 `## Plan review` attestation (hash-pinned).

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
