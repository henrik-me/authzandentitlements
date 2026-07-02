# Project Context

> **Last updated:** _(set when consumer first edits)_

## Codebase state

Bootstrap in progress (2026-07-02). Git initialized on `main`; remote `origin` points to
`github.com/henrik-me/authzandentitlements` (repo not yet created on GitHub). **agent-harness
v0.12.0 adopted** (process docs, review gates, linters, CI workflow). The full **27-CS plan**
is authored in `project/clickstops/planned/` with explicit Phase / Lane / Depends-on. No
product code yet - **CS01 (Aspire foundations)** is the first claimable clickstop. `harness
lint` is green except the claim-time `## Plan review` attestation (added per CS at claim).

## Constraints

See `.harness-known-constraints.md` for repository tier and disposition (detected 2026-07-02T18:34:27.975Z).

## Architecture pointer

See [ARCHITECTURE.md](ARCHITECTURE.md).

_(Keep this section as a short pointer. All architecture detail lives in
`ARCHITECTURE.md`. Add a sentence or two summarising the top-level design only
if it helps orient a reader skimming this file.)_

## Blockers / open questions

- **GitHub repo not created yet.** `origin` is set to `github.com/henrik-me/authzandentitlements`; run `gh repo create` + push when ready (nothing pushed yet).
- **Docker daemon must be running** for the container-based engines/infra (Docker Desktop is installed).
- **Claim-time plan review pending** per planned CS (`## Plan review`, independent reviewer) before moving a CS to `active/`.
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
| CS14 | Blazor product UI | Product | CS03, CS06, CS10, CS11 |
| CS15 | AuthZ playground + audit explorer | Product | CS06, CS07, CS08, CS09, CS13 |
| CS16 | Explainability (why allowed/denied) | Cross-cutting | CS05 |
| CS17 | Policy lifecycle + testing | Cross-cutting | CS05 |
| CS18 | Security hardening + threat model | Cross-cutting | CS04, CS05 |
| CS19 | Agent + non-agent access | Cross-cutting | CS03, CS05 |
| CS20 | Migration & portability | Cross-cutting | CS05, CS07, CS08 |
| CS21 | Break-glass, delegation & OBO | Cross-cutting | CS11, CS05 |
| CS22 | Compliance mapping | Cross-cutting | CS13, CS11 |
| CS23 | Comparison matrix + survey | Eval | CS15 |
| CS24 | Performance benchmark + tracking | Eval | CS06, CS07, CS08, CS09, CS12 |
| CS25 | Managed-vs-self-host TCO | Eval | CS23 |
| CS26 | Expansion engines | Expansion | CS05, CS15 |
| CS27 | Full OpenMeter + Azure deploy | Expansion | CS10, CS12 |

### Parallelization (waves)

- **Wave 1 (serial foundation):** CS01 -> CS02.
- **Wave 2 (4 lanes open in parallel after CS02):** CS03 (Identity), CS05 (PDP hub), CS10 (Entitlements), CS12 (Observability).
- **Wave 3 (after CS05, 4 engine adapters in parallel):** CS06, CS07, CS08, CS09; plus CS13, CS16, CS17; CS04 after CS03; CS11 after CS02+CS08.
- **Wave 4:** cross-cutting CS18/CS19/CS20/CS21/CS22 as deps land; product CS14/CS15.
- **Wave 5 (eval + expansion):** CS23, CS24, CS25, CS26, CS27.

A fleet of orchestrators can each `harness claim` an independent, dependency-satisfied CS.
