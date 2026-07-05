# CS48 — Local stack validation + demo/lab readiness

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Validation
**Filed by:** yoga-ae-c2 on 2026-07-04 — prerequisite that gates the CS27/CS43/CS44 holds (their `## Hold / claim gate` precondition #1 is "the current stack has been validated in detail locally and that validation is documented"). Filed at the user's direction after those three were put on hold.
**Depends on:** CS12, CS14, CS15, CS21, CS26

## Goal

Validate the current .NET Aspire stack in detail **locally**, exercise the real demo/lab scenarios end-to-end, and **document** the result — producing the repo-resident validation evidence that the held CS27/CS43/CS44 require before they can be lifted. No cloud, no new product features.

## Background

- **CS27 (Azure app deploy), CS43 (full OpenMeter local) and CS44 (OpenMeter on Azure) are HELD** (guarded by `LEARNINGS.md` LRN-069/070/071 + `reviews.high_risk_clickstops`; see each file's `## Hold / claim gate`). Every hold's precondition #1 is a **documented, detailed local validation of the current stack**, and precondition #3 asks whether **additional demo/lab observability is warranted**. This CS produces exactly that evidence; it does **not** by itself lift the holds (that still needs the explicit user go-ahead of precondition #2).
- The stack today (all done): a .NET 10 **Aspire** app — `AuthzEntitlements.AppHost` + `AuthzEntitlements.ServiceDefaults` — with a shared PostgreSQL server (six logical DBs: `bank`, `openfga`, `entitlements`, `governance`, `audit`, `unleash`), a deterministic in-process PDP default (`reference`/`aspnet`/`casbin`/`cedar`) plus opt-in out-of-process engines (`opa`/`openfga`, and — from CS26 — `spicedb`/`cerbos`) and the `unleash` flag backend wired via `.WithExplicitStart()` (they do not auto-start with `aspire run`), OpenFeature entitlements + lightweight Postgres/OTel quota metering, governance, a tamper-evident audit hash-chain, `Bank.Api`/`Edge.Gateway`/`Bank.Web`, the AuthZ playground + audit explorer, and break-glass/delegation/OBO. Observability is OTLP → the always-on `grafana/otel-lgtm` container (per-service export gated on a non-empty `OTEL_EXPORTER_OTLP_ENDPOINT` in `ServiceDefaults`).
- **`aspire run` is container-backed:** Postgres, Keycloak (OIDC), and the `grafana/otel-lgtm` observability backend are **always-on containers** (services `WaitFor(keycloak)`), so a full local bring-up **requires Docker**. Only the additional PDP engines (`opa`/`openfga`/`spicedb`/`cerbos`) and `unleash` are opt-in explicit-start. The in-process PDP *default* is deterministic and needs no engine container, but the app infrastructure still runs in Docker.
- Build/test entry points: `dotnet build AuthzEntitlements.sln` and `dotnet test` across the nine `tests/*` projects (Audit, Authz.Pdp, Bank.Api, Bank.Web, Benchmarks, Compliance, Edge.Gateway, Entitlements.Service, Governance.Service).
- **State-of-world probe (2026-07-04, F6):** `project/clickstops/{planned,active,done}/` use CS ids up to CS47 (next free ids at/above CS48; low gaps 35/38/39/41/42 are collision-churn residue from concurrent sibling filings — not reused). Deps CS12, CS14, CS15, CS21, CS26 are all in `project/clickstops/done/`.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Scope | Local validation + demo/lab readiness only — no cloud, no new features | This is the prerequisite that gates the held CSs; keep it tight so it can't drift into the held cloud work. |
| 2 | Build + test gate | Full `dotnet build AuthzEntitlements.sln` + `dotnet test` on the solution, results recorded (warnings/errors + per-suite test counts) | Establishes the compile-clean, tests-green baseline as documented evidence. |
| 3 | End-to-end smoke | `aspire run` (container-backed — requires Docker for the always-on Postgres, Keycloak, and observability containers) + walk the real demo scenarios: authn (Keycloak OIDC), PDP decisions incl. multi-engine playground fan-out, entitlements/quota enforcement, audit + hash-chain verify, break-glass/delegation/OBO | Validates the *integrated* app runs the demo story, not just unit tests. The in-process PDP default is deterministic (no engine container), but the app infra still runs in Docker. |
| 4 | Opt-in engines | Bring up the opt-in engines `opa`/`openfga`/`spicedb`/`cerbos` + the `unleash` flag backend (Docker) at least once and confirm engine parity + playground fan-out and the Unleash flag path | Confirms the full swappable-engine + flag demo works when Docker is available, without changing the default resource set. |
| 5 | Observability assessment | Record what the demo/lab actually surfaces today (traces/metrics/logs via OTLP → Grafana) and give an explicit **warranted / not-warranted** recommendation on additional observability detail | Directly answers the CS43/CS44 observability-warrant precondition; the recommendation is recorded, never silently decided. |
| 6 | Deliverable = documented validation | A validation report + a demo/lab runbook under `docs/` | Durable, repo-resident evidence satisfying the held CSs' precondition #1; a fresh operator can re-run the demo from the repo alone. |
| 7 | Holds are NOT auto-lifted | This CS documents validation and makes a recommendation; it does **not** flip the CS27/CS43/CS44 holds or their LRNs | Lifting remains an explicit user decision (hold precondition #2 — cloud is out of scope until the user says otherwise). |
| 8 | Fixes surfaced during validation | Small, direct breakages found during smoke are fixed in-band on this branch; larger issues are filed as follow-up CSs, not silently expanded here | Keeps the validation honest and its scope bounded. |

## Deliverables

- A **local validation report** under `docs/validation/` (e.g. `docs/validation/local-stack-validation.md`): `dotnet build` + `dotnet test` results (warnings/errors + per-suite counts), the `aspire run` bring-up, per-scenario smoke outcomes, the opt-in-engine (`opa`/`openfga`/`spicedb`/`cerbos`/`unleash`) checks, and an explicit list of known gaps / not-yet-validated areas.
- A **demo/lab runbook** under `docs/` (e.g. `docs/demo/` or `docs/product/`): step-by-step to run the demo locally — the default path (Docker-backed: Postgres/Keycloak/observability + the in-process PDP) plus the optional-engine additions (`opa`/`openfga`/`spicedb`/`cerbos`/`unleash`) — so the lab is reproducible from the repo.
- An **observability assessment** (a section of the report): what the demo/lab shows today and a recommendation on whether the added observability of CS43/CS44 is warranted, cross-referenced from those CSs' hold gates.
- No product-code change is expected; any small fixes discovered during validation are documented, and larger gaps are filed as follow-up planned CSs.

## User-approval gates

- None for the validation work itself (local, no billable resources). **Lifting the CS27/CS43/CS44 holds is explicitly out of scope** for this CS and remains a separate, explicit user decision (each hold's precondition #2).

## Exit criteria

- `dotnet build AuthzEntitlements.sln` and `dotnet test` recorded green (warnings/errors + counts); the `aspire run` demo scenarios exercised and documented; the opt-in engines (`opa`/`openfga`/`spicedb`/`cerbos`/`unleash`) confirmed (or their unavailability recorded); the observability assessment made with a warranted/not-warranted recommendation; and the validation report + demo runbook committed. On close-out, the CS27/CS43/CS44 hold precondition #1 ("local validation, documented") is satisfied and referenced from those files.

## Risks + open questions

- **Docker is required for the full smoke.** `aspire run` brings up always-on Postgres, Keycloak, and observability containers, and the opt-in engines add more — so the end-to-end validation needs Docker. If Docker is unavailable in the validation environment, the `aspire run` smoke cannot run at all; record that explicitly (build + `dotnet test` still run without Docker) rather than reporting a partial pass as complete.
- **Latent breakages.** End-to-end smoke may surface integration issues that unit tests miss — Decision #8 governs fix-in-band vs. follow-up-CS.
- **Observability "warrant" is a judgment call.** Record the reasoning behind the recommendation; do not pre-commit CS43/CS44 either way.
- **Scope creep toward the held work.** This CS must stay local + read-only-ish; it must not begin any cloud or OpenMeter implementation (that is the held scope).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | 3d70a3cc2269 | 2026-07-05T05:00:00Z | Needs-Fix | "Docker-free default" wrong (aspire run needs Docker: Postgres/Keycloak/observability always-on); omitted CS26 spicedb/cerbos engines. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | ddcf7825fab5 | 2026-07-05T05:08:00Z | Needs-Fix | Both R1 content findings fixed; remaining items were the pre-attestation empty plan-review row + a probe-sentence tweak to add CS26 (applied). |
| R3 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | ddcf7825fab5 | 2026-07-05T05:14:00Z | Go | No plan-content blockers: Docker-backed aspire run, CS26 engines/deps, CONTEXT row, local-only scope, hold evidence, unique CS48 all verified. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
