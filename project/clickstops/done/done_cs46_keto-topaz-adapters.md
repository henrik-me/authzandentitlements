# CS46 — Keto + Topaz expansion engine adapters

**Status:** done
**Owner:** yoga-ae
**Branch:** cs46/keto (#172) + cs46/topaz (#176)
**Started:** 2026-07-05
**Closed:** 2026-07-05
**Filed by:** yoga-ae-c4 on 2026-07-04 — the remaining two engine adapters from the CS26 expansion set after SpiceDB (PR #134) and Cerbos (PR #139) shipped; split out so CS26 can close on what's delivered.
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS05, CS15, CS45

## Goal

Continue the CS26 expansion by adding the **Ory Keto** (Zanzibar ReBAC) and **Topaz/Aserto** (OPA + directory) engine adapters behind the CS05 `IAuthorizationDecisionProvider` seam and the CS15 playground, so both answer the fintech scenario catalog and appear in the playground — each fail-closed, off the deterministic default path, and carrying the CS45 delegation/OBO guard from day one.

## Background

- **CS26** delivered 2 of the 5 planned expansion engines — **SpiceDB** (ReBAC, gRPC, `authzed/spicedb:v1.54.0`) and **Cerbos** (full-decision YAML/CEL, gRPC, `ghcr.io/cerbos/cerbos:0.53.0`) — establishing the out-of-process adapter patterns (lazy client, fail-closed, offline + env-gated tests, opt-in `WithExplicitStart` container, playground entry, adapter doc) and the gRPC h2c switch learning.
- **Keto** (verified in CS26 research): `Ory.Keto.Client` `0.11.0-alpha.0` (HTTP REST auto-generated OpenAPI client; ALL versions are `-alpha`, install with `--prerelease`), image `oryd/keto:v26.2.0`. It is **Zanzibar ReBAC** like SpiceDB/OpenFGA, so it answers **account-shaped relationship checks** — reuse the shared `Rebac*` schema/seed/scenario graph for a fair head-to-head. Keto exposes **two ports**: read `:4466` and write `:4467`; namespaces + relation tuples are configured via a `keto.yaml` + the write API.
- **Topaz/Aserto** (verified in CS26 research): `Aserto.Clients` `1.1.2` (+ optionally `Aserto.AspNetCore.Middleware` `0.53.3`), gRPC (authorizer `:8282`) + REST (`:8383`) + directory (`:9292`), image `ghcr.io/aserto-dev/topaz:0.33.14`. Topaz runs an **OPA policy bundle** (an OCI image or a locally-built bundle) plus a **directory** (a Zanzibar-style manifest + object/relation data). It is a hybrid — it can answer a full OPA-style decision and/or relationship checks.
- **Oso** is intentionally **de-scoped** (see ADR-0008, filed by CS47): there is no maintained in-process .NET/Polar library, and Oso's only self-hostable component is a **development-only** dev-server with **no production self-hosting path**, so it does not fit the repo's opt-in, production-representative pinned-container engine lab.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | One CS or two | **One CS, two sub-increments** (Keto first, then Topaz), each a separately-reviewable content PR | Mirrors CS26's incremental style; both are "expansion engine" adapters surfaced together; keeps the arc coherent while each PR stays reviewable. |
| 2 | Keto integration style | **ReBAC, account-only** — reuse the shared `Rebac*` graph (schema/namespaces + seed tuples), fail closed on non-account resources, exactly like SpiceDB/OpenFGA | Keto is Zanzibar ReBAC; a shared graph makes the SpiceDB/OpenFGA/Keto head-to-head genuinely fair and avoids graph drift. |
| 3 | Keto dual-port handling | Adapter manages both base URLs (read `:4466`, write `:4467`); seed tuples via the write API at bootstrap, check via the read API | Keto splits read/write across ports; the adapter must bind both and route each call correctly. |
| 4 | Topaz integration style | Author it as a **full-decision** adapter over its OPA policy bundle (like OPA/Cerbos), OR relationship checks over its directory — pick whichever yields a faithful catalog answer and **document the parity boundary** (as SpiceDB documents its ReBAC boundary) | Topaz is hybrid; the impl picks the faithful mapping and states the boundary rather than overclaiming full parity. |
| 5 | Topaz policy delivery | Prefer a **locally-built OPA bundle** (`opa build`) or a mounted config over pushing an OCI image to a registry; pin the manifest/data | Avoids a registry-push dependency in the lab; keeps the container opt-in + deterministic. |
| 6 | Delegation/OBO guard | Both adapters rely on the **CS45** shared factory guard — a **hard dependency**, with **no** per-adapter inline guard | ReBAC/OPA adapters ignore `Subject.Actor`; the shared guard is the single source of truth, so recreating a per-adapter guard is explicitly avoided (it reintroduces the exact drift CS45 eliminates). |
| 7 | Determinism + default path | Pin image tags (`oryd/keto:v26.2.0`, `ghcr.io/aserto-dev/topaz:0.33.14`); opt-in containers (`WithExplicitStart`, no hard `WaitFor`); default `Pdp:Provider` stays `reference`; build/test/`aspire run` stay Docker-free | Repo conventions (Aspire opt-in engines, image pinning); registering an adapter never changes the active engine. |
| 8 | Keto alpha client | Accept + CPM-pin the `-alpha` client (`Ory.Keto.Client` `0.11.0-alpha.0`); document the pre-release caveat + the client-version (`0.11.0-alpha.0`) vs server-version (`v26.2.0`) mismatch | It is the only .NET Keto client and is functional (auto-generated from the stable REST OpenAPI spec). |
| 9 | Testing | Offline provider/mapper tests (fail-closed + reason codes) with a fake seam; env-gated integration tests (`KETO_TEST_ENDPOINT` / `TOPAZ_TEST_ENDPOINT`) validated against a real container; full offline suite green with NO container | Matches CS26; the CI-invisible live path (esp. Topaz's policy/directory) is proven by an env-gated container run, mirroring the Cerbos validation. |

## Deliverables

- **Keto adapter** behind `IAuthorizationDecisionProvider` (`Name = "keto"`): pure request-mapper (account-only, reusing the shared `Rebac*` graph), lazy client managing read `:4466` + write `:4467`, namespace/tuple bootstrap, fail-closed provider with CS16 explanation; CPM pin `Ory.Keto.Client` (pre-release); opt-in `oryd/keto:v26.2.0` AppHost container + endpoint injection; playground entry; `docs/authz/keto-adapter.md`; offline + env-gated tests.
- **Topaz adapter** behind `IAuthorizationDecisionProvider` (`Name = "topaz"`): provider + client (gRPC/REST), OPA-bundle + directory manifest/data setup, fail-closed provider with CS16 explanation; CPM pin `Aserto.Clients`; opt-in `ghcr.io/aserto-dev/topaz:0.33.14` AppHost container + endpoint injection; playground entry; `docs/authz/topaz-adapter.md`; offline + env-gated tests.
- Both adapters covered by the CS45 delegation/OBO fail-closed guard.
- Registration in `AddPdp`; default stays `reference`; the deterministic Docker-free path unchanged.
- Head-to-head / boundary notes as appropriate (Keto vs SpiceDB/OpenFGA ReBAC; Topaz's hybrid boundary).

## User-approval gates

- None expected. If either engine forces a new runtime dependency beyond a CPM pin (e.g. a registry push for the Topaz bundle) or cannot be made offline-test-green, escalate.

## Exit criteria

- `keto` and `topaz` are selectable via `Pdp:Provider`, appear in the CS15 playground, and answer the scenario catalog (ReBAC-appropriate for Keto; full-decision-or-documented-boundary for Topaz); both fail closed on every uncertainty **and** on OBO/delegation/break-glass requests; the default run/build/test path stays deterministic and Docker-free; each engine's live behaviour is validated once against a real pinned container via its env-gated integration suite.

## Risks + open questions

- **Keto alpha client drift.** The `-alpha` client is generated from the REST OpenAPI spec; validate the write/read APIs against `v26.2.0` early.
- **Topaz complexity.** OPA-bundle + directory manifest/data + three ports is the most involved engine; the bundle-delivery decision (local build vs OCI push) and the directory seeding are the main unknowns — spike early.
- **Parity honesty.** Keto (ReBAC) answers account-shaped questions only (documented boundary, like SpiceDB); Topaz's faithful mapping + boundary must be documented, not overclaimed.
- **CS45 sequencing.** CS45 (the shared factory guard) is a hard prerequisite and must land + be implemented first; both adapters rely on it (no per-adapter inline guard).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs45-47-plan-review (rubber-duck) | 9a600b512e25 | 2026-07-05T02:48:12Z | Go-with-amendments | CS45 made a hard dep (no inline guard); Keto version normalized to 0.11.0-alpha.0 — both applied. Styles match repo ReBAC + full-decision patterns; deps acyclic. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Keto: `Ory.Keto.Client` CPM pin (pre-release `0.11.0-alpha.0`) + versionless csproj ref | done | yoga-ae | PR #172; pre-release + client/server version-skew caveat documented in `keto-adapter.md` |
| Keto: adapter — account-only mapper reusing the shared `Rebac*` graph + lazy dual-port client (read `:4466` / write `:4467`) + tuple bootstrap + fail-closed provider (CS16) + DI registration in `AddPdp` | done | yoga-ae | PR #172; `Name="keto"`; subject_id omitted for subject-set tuples (`WhenWritingNull`) per LRN-083; no per-adapter OBO guard (CS45) |
| Keto: AppHost container (`oryd/keto:v26.2.0`, `WithExplicitStart`, no hard `WaitFor`) + endpoint injection | done | yoga-ae | PR #172; opt-in; default `reference` Docker-free path unchanged |
| Keto: tests (offline mapper + provider fail-closed/reason-codes; env-gated integration `KETO_TEST_ENDPOINT`) | done | yoga-ae | PR #172; offline suite green with NO container; live integration 3/3 vs a real `oryd/keto:v26.2.0` |
| Keto: playground entry + `docs/authz/keto-adapter.md` (+ ReBAC boundary note) | done | yoga-ae | PR #172 |
| Topaz: `Aserto.Clients` CPM pin + versionless csproj ref | done | yoga-ae | PR #176; `1.1.2` |
| Topaz: adapter — full-decision provider over the OPA bundle + client + fail-closed provider (CS16) + DI registration in `AddPdp` | done | yoga-ae | PR #176; `Name="topaz"`; drives the shared `infra/opa/policy` Rego via the Aserto authorizer; directory/ReBAC path is the documented boundary; no per-adapter OBO guard (CS45) |
| Topaz: AppHost container (`ghcr.io/aserto-dev/topaz:0.33.14`, `WithExplicitStart`, no hard `WaitFor`) + endpoint injection | done | yoga-ae | PR #176; LOCAL OPA bundle, no registry push (LRN-084) |
| Topaz: tests (offline provider fail-closed + parity; env-gated integration `TOPAZ_TEST_ENDPOINT`) | done | yoga-ae | PR #176; offline suite green with NO container; live full-catalog parity 2/2 vs a real `ghcr.io/aserto-dev/topaz:0.33.14` |
| Topaz: playground entry + `docs/authz/topaz-adapter.md` (boundary note) | done | yoga-ae | PR #176 |
| Verify the CS45 delegation/OBO fail-closed guard covers both adapters; default stays `reference`; Docker-free build/test/`aspire run` unchanged | done | yoga-ae | both adapters wrapped by `ExtendedContextGuardProvider` (asserted in the provider tests); default `reference` + Docker-free path unchanged |
| Close-out: docs + restart state | done | yoga-ae | CONTEXT.md CS46 paragraph added; WORKBOARD row removed at close-out |
| Close-out: learnings + follow-ups | done | yoga-ae | filed LRN-083 (Keto write hazard) + LRN-084 (Topaz local-bundle offline integration); no follow-up CS required |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |

## Notes / Learnings

- **Shipped both remaining CS26 expansion engines**, completing the set (SpiceDB + Cerbos in CS26; Keto + Topaz here; Oso de-scoped in ADR-0008 / CS47).
  - **Keto** (ReBAC, PR #172): account-only adapter reusing the shared `Rebac*` seed graph over Keto's dual-port HTTP REST API (read `:4466` / write `:4467`); opt-in `oryd/keto:v26.2.0` with an OPL namespace config (`infra/keto/`). Live head-to-head 3/3 vs SpiceDB/OpenFGA on `RebacScenarioCatalog.Forward`.
  - **Topaz** (full-decision, PR #176): drives Topaz's OPA bundle over the SAME `infra/opa/policy` Rego via the Aserto authorizer — the "OPA standalone vs OPA-inside-Topaz" head-to-head; **local** bundle, no registry push. Live full `FintechScenarioCatalog` parity 2/2.
- **Both fail closed** and rely on the CS45 `ExtendedContextGuardProvider` (no per-adapter OBO guard); default `Pdp:Provider` stays `reference` and the Docker-free build/test/`aspire run` loop is unchanged. `dotnet build` 0/0; full-solution `dotnet test` 1796 (PDP 1031→1039 across the two engines; AppHost smoke 2/2 with both new containers).
- **Learnings filed:** **LRN-083** (Keto relation-tuple write hazard — an empty `subject_id` alongside a `subject_set` makes Keto v26.2.0 drop the `subject_set`; its PUT appends, not upserts — remediated by omitting `subject_id` via `JsonIgnoreCondition.WhenWritingNull`), **LRN-084** (Topaz boots offline from a local OPA bundle with no OCI registry; `IDENTITY_TYPE_NONE` required on anonymous authorizer queries; the decision object lands at `result[0].bindings.x`).
- **Non-blocking follow-up (Topaz R2):** the bounded query uses `Task.WaitAsync(timeout)` (fails closed promptly) but does not cancel the underlying gRPC call; prefer a real RPC deadline if the Aserto client later exposes one. Minor; no follow-up CS filed.
- **Review-of-record:** independent GPT-5.5 rubber-duck on both content PRs (Keto R1 Needs-Fix → R2 Go; Topaz R1 Needs-Fix → R2 Go), a Copilot review on each, and this plan-vs-implementation GO.

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, independent sub-agent `cs46-pvi-review`) — independent of the claude-opus implementers
**Date:** 2026-07-05T19:22:23Z
**Outcome:** GO

Reviewed the merged CS46 content (Keto PR #172 + Topaz PR #176) at `main` HEAD `f1b1f93` against the plan's Deliverables, Exit criteria, Decisions, and Risks. Blocking findings: **none**.

| Deliverable | Outcome | Rationale |
|---|---|---|
| `keto` adapter behind `IAuthorizationDecisionProvider`, selectable via `Pdp:Provider` | match | `KetoProvider`, `Name="keto"`, registered in `AddPdp`; case-insensitive selection asserted (`KETO`). |
| `topaz` adapter behind `IAuthorizationDecisionProvider`, selectable via `Pdp:Provider` | match | `TopazDecisionProvider`, `Name="topaz"`, registered in `AddPdp`; case-insensitive selection asserted (`TOPAZ`). |
| Default provider remains deterministic `reference` | match | `PdpOptions` default + `appsettings.json` `"Provider":"reference"`. |
| CS45 OBO/delegation/break-glass guard covers both | match | Neither declares `ISupportsExtendedAuthorizationContext`; factory wraps both in `ExtendedContextGuardProvider`; provider tests assert the guard-wrapping with the concrete adapter as `Inner`. |
| Keto ReBAC graph reuse + dual-port read/write | match | Mapper/service reuse shared `Rebac*` model + seed tuples; check on read `4466`, seed via write `4467`. |
| Topaz full-decision over OPA bundle + documented boundary | match | Queries `data.authz.bank.decision` through the Aserto authorizer over the same `infra/opa/policy` Rego; docs state the directory/ReBAC path is not used. |
| Local bundle, no registry push/pull | match | `infra/topaz/config.yaml` `opa.local_bundles.paths: /policy`; AppHost bind-mounts `infra/opa/policy`; no OCI dependency. |
| Pinned images + opt-in + no hard `WaitFor` | match | `oryd/keto:v26.2.0`, `ghcr.io/aserto-dev/topaz:0.33.14`, both `.WithExplicitStart()`, endpoint injection, no `WaitFor`. |
| Lazy clients + blank-endpoint fail-closed | match | Services validate endpoints on first use; provider tests cover blank-endpoint fail-closed. |
| Playground entries | match | `Playground.razor` has explicit `keto` + `topaz` choices. |
| Scenario catalog coverage | match | Keto env-gated integration covers `RebacScenarioCatalog.Forward` (live 3/3, PR #172); Topaz env-gated integration covers the full `FintechScenarioCatalog` decision+reason (live 2/2, PR #176). |
| Docs | match | `keto-adapter.md`, `topaz-adapter.md`, and both names added to `adding-an-engine-adapter.md`. |
| Benchmark/comparison-matrix inclusion | diverged | Not a CS46-owned close-out surface — CS24-owned benchmark/matrix, consistent with the CS26 handling; recorded as known future work, not a CS46 gap. |

**Test-coverage:** sufficient — offline mapper/provider/config/fail-closed suites for both engines + env-gated live integration; independently-established green build/test/lint (1796 solution tests; PDP 1039; lint 23/0).

**Security:** fail-closed posture preserved — both rely on the shared CS45 factory guard for OBO/delegation/break-glass, fail closed on unavailable/malformed engine responses, and keep opt-in containers off the deterministic default path; Topaz's self-signed TLS acceptance is scoped to loopback.

Review-log row: `model: gpt-5.5` · `branch HEAD SHA: f1b1f93` · `verdict: GO` · `evidence: PR #172 + PR #176`.
