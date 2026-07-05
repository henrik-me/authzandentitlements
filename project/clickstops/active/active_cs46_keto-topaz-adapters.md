# CS46 — Keto + Topaz expansion engine adapters

**Status:** active
**Owner:** yoga-ae
**Branch:** cs46/content
**Started:** 2026-07-05
**Closed:** —
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
- **Oso** is intentionally **de-scoped** (see the separate Oso-disposition CS): no classic in-process .NET library exists and the only local path is an unpinnable `latest`-only dev-server, conflicting with the repo's image-pinning-for-determinism convention.

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
| Keto: `Ory.Keto.Client` CPM pin (pre-release `0.11.0-alpha.0`) + versionless csproj ref | pending | yoga-ae | document the pre-release + client/server version-skew caveat (client `0.11.0-alpha.0` vs server `v26.2.0`) |
| Keto: adapter — account-only mapper reusing the shared `Rebac*` graph + lazy dual-port client (read `:4466` / write `:4467`) + namespace/tuple bootstrap + fail-closed provider (CS16 explanation) + DI registration in `AddPdp` | pending | yoga-ae | ReBAC, `Name = "keto"`; fail closed on non-account resources; no per-adapter OBO guard (CS45 owns it) |
| Keto: AppHost container (`oryd/keto:v26.2.0`, `WithExplicitStart`, no hard `WaitFor`) + endpoint injection | pending | yoga-ae | opt-in; default `reference` path stays Docker-free |
| Keto: tests (offline mapper + provider fail-closed/reason-codes; env-gated integration `KETO_TEST_ENDPOINT`) | pending | yoga-ae | full offline suite green with NO container |
| Keto: playground entry + `docs/authz/keto-adapter.md` (+ Keto-vs-SpiceDB/OpenFGA ReBAC boundary note) | pending | yoga-ae | mirror the SpiceDB doc's ReBAC boundary honesty |
| Topaz: `Aserto.Clients` CPM pin + versionless csproj ref | pending | yoga-ae | `1.1.2` (gRPC `:8282` / REST `:8383` / directory `:9292`) |
| Topaz: adapter — provider + client, locally-built OPA bundle + directory manifest/data, fail-closed provider (CS16 explanation) + DI registration in `AddPdp` | pending | yoga-ae | `Name = "topaz"`; full-decision-or-documented-boundary; no per-adapter OBO guard (CS45) |
| Topaz: AppHost container (`ghcr.io/aserto-dev/topaz:0.33.14`, `WithExplicitStart`, no hard `WaitFor`) + endpoint injection | pending | yoga-ae | prefer a local OPA bundle over an OCI push; pin manifest/data |
| Topaz: tests (offline provider fail-closed + parity; env-gated integration `TOPAZ_TEST_ENDPOINT`) | pending | yoga-ae | live policy/directory proven by a pinned-container run |
| Topaz: playground entry + `docs/authz/topaz-adapter.md` (hybrid boundary note) | pending | yoga-ae | document the faithful mapping + boundary, don't overclaim |
| Verify the CS45 delegation/OBO fail-closed guard covers both adapters; default stays `reference`; Docker-free build/test/`aspire run` unchanged | pending | yoga-ae | hard dep on the CS45 shared factory guard — no inline per-adapter guard |
| Close-out: docs + restart state | pending | yoga-ae | update WORKBOARD + CONTEXT.md so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae | file LRNs; planned follow-up CSs for any unresolved issues |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
