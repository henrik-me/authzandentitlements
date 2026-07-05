# CS50 — AppHost application-model CI smoke test

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs50/content
**Started:** 2026-07-05
**Closed:** —
**Phase:** Cross-cutting
**Lane:** DevEx
**Filed by:** yoga-ae-c2 on 2026-07-05 — follow-up from CS48 / LRN-078: CS48's local validation found a boot-blocking AppHost resource-name collision that had shipped undetected because no CI job exercises the AppHost application model.
**Depends on:** CS01

## Goal

Add a minimal automated test that constructs the `AuthzEntitlements.AppHost` **application model** and asserts it builds without throwing, so Aspire resource-name collisions and comparable AppHost wiring defects **fail CI** instead of only surfacing at `aspire run`.

## Background

- **CS48 (local validation)** found that `aspire run` crashed on startup: two container resources (`unleash`, `openfga`) shared a name with the same-named shared-Postgres databases (Aspire resource names are case-insensitive and must be unique), so `Program.Main` threw before any resource started. It was fixed in-band (containers renamed `unleash-server` / `openfga-server`; `src/AuthzEntitlements.AppHost/AppHost.cs`) — see `docs/validation/local-stack-validation.md` and **LRN-078**.
- **Root gap:** `.github/workflows/dotnet-ci.yml` runs `dotnet build AuthzEntitlements.sln` + `dotnet test AuthzEntitlements.sln`, but **no test exercises the AppHost's `DistributedApplicationBuilder`**, which is only evaluated when the AppHost actually runs. A `dotnet build` compiles the AppHost but does not build its application model, so duplicate-resource-name / bad-`WaitFor` defects pass CI.
- **Mechanism available:** Aspire ships `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder.CreateAsync<Projects.AuthzEntitlements_AppHost>()`) to construct the app model in a test without starting containers. A test that awaits `CreateAsync` then `BuildAsync` (disposing the app; **never** calling `StartAsync`, so it stays Docker-free) and asserts no exception is thrown catches resource-name collisions at test time — a duplicate `AddResource` throws before any container starts. CI already runs `dotnet test AuthzEntitlements.sln`, so a new test project added to the solution runs automatically — **no CI-workflow change required**.
- **State-of-world probe (2026-07-05, F6):** CS ids in `project/clickstops/**` run up to CS49 (next free at/above CS50). `Aspire.Hosting.Testing` is not yet referenced in any `.csproj`; `Aspire.Hosting.*` packages are pinned at `13.1.0` in `Directory.Packages.props` (CPM). Dep CS01 (Aspire foundations) is in `project/clickstops/done/`.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Test surface | Construct the AppHost model via `Aspire.Hosting.Testing` (`await CreateAsync` → `await BuildAsync`, dispose the app; **no** `StartAsync`) and assert it builds without throwing | Reproduces the exact failure mode (Program.Main / builder throwing) that `dotnet build` + unit tests miss, at the Docker-free `BuildAsync` boundary. |
| 2 | Scope | App-model construction + `BuildAsync` only — do NOT `StartAsync` / start containers | Keeps the test fast, deterministic, and Docker-free so it runs in CI on every PR. |
| 3 | Placement | A dedicated `tests/AuthzEntitlements.AppHost.Tests` project added to `AuthzEntitlements.sln` | `dotnet test AuthzEntitlements.sln` picks it up automatically; no `dotnet-ci.yml` change needed. |
| 4 | Dependency | Add `Aspire.Hosting.Testing` via CPM (version-less `PackageReference`, pinned `13.1.0` in `Directory.Packages.props` to match the other `Aspire.Hosting.*` pins) | Follows the repo's Central Package Management convention. |
| 5 | Optional hardening | Additionally assert each expected resource name is unique (case-insensitive) | A targeted regression guard for the exact CS48 defect class, beyond the general build-without-throw. |

## Deliverables

- A `tests/AuthzEntitlements.AppHost.Tests` project (added to `AuthzEntitlements.sln`) with a test that constructs the AppHost model via `Aspire.Hosting.Testing` (`await CreateAsync<Projects.AuthzEntitlements_AppHost>()` then `await BuildAsync()`, disposing the app; **not** `StartAsync`) and asserts it builds without throwing (and, per Decision #5, that resource names are unique case-insensitively).
- `Aspire.Hosting.Testing` added to `Directory.Packages.props` (CPM, `13.1.0`).
- LRN-078 flipped to `applied` (with the implementing CS/commit) at close-out.

## User-approval gates

- None — local test + CI addition, no runtime/behaviour change, no billable resources.

## Exit criteria

- The new test fails against a deliberately-reintroduced duplicate resource name and passes on the current AppHost; `dotnet test AuthzEntitlements.sln` (and therefore CI) runs it; `dotnet build` 0/0; `harness lint` green.

## Risks + open questions

- **`Aspire.Hosting.Testing` startup cost:** `CreateAsync` + `BuildAsync` should construct the model without starting containers (the collision throws during `AddResource`, before any start); keep the test to build + assert and never call `StartAsync` (Decision #2). If `BuildAsync` unexpectedly needs unavailable CI infrastructure, fall back to `CreateAsync` + a `builder.Resources` case-insensitive name-uniqueness assertion (not a pure reflection/string check that could bypass AppHost registration logic).
- **Preview-package churn:** the Aspire testing package must match the pinned `13.1.0` line; watch for a version skew with `Aspire.Hosting.Keycloak` (preview).
- **Determinism:** the test must be Docker-free and not bind ports, so it is safe in CI and in the deterministic default loop.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched | dfe237873291 | 2026-07-05T07:00:00Z | Go-with-amendments | Sound CI guard; facts verified. Amendment applied: mandatory CreateAsync→BuildAsync (no StartAsync, Docker-free); fallback asserts builder.Resources uniqueness, not reflection. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Add `tests/AuthzEntitlements.AppHost.Tests` project + `Aspire.Hosting.Testing` (CPM 13.1.0) | done | sub-agent | agent-id=cs50-impl-smoketest \| role=impl-test-author \| report-status=complete \| learnings=2 — version-less refs; added to AuthzEntitlements.sln (CPM entry on disk) |
| Smoke test: CreateAsync → BuildAsync (no StartAsync) asserts no throw + resource-name uniqueness | done | sub-agent | agent-id=cs50-impl-smoketest \| role=impl-test-author \| report-status=complete \| learnings=2 — Docker-free; 2 [Fact]s; BuildAsync used, no fallback needed |
| Verify guard: passes on current AppHost, fails on a reintroduced duplicate name | done | sub-agent | agent-id=cs50-impl-smoketest \| role=impl-test-author \| report-status=complete \| learnings=2 — orchestrator re-verified: dup `unleash` → 2 failed (DistributedApplicationException @ AppHost.cs:65); reverted → 2 passed |
| Close-out: docs + restart state | pending | yoga-ae-c2 | update WORKBOARD + CONTEXT.md so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c2 | flip LRN-078 to applied with the implementing commit |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-05T16:34:46Z
**Outcome:** GO

Reviewed content HEAD SHA `2e27035` (CS50 content squash-merged to `main` via PR #168). Independence confirmed (implementer model `claude-opus-4.8`; implementer agents `yoga-ae-c2` + `cs50-impl-smoketest`; reviewer agent `rubber-duck`).

### Per-deliverable outcome table

| Deliverable | Outcome | Evidence / rationale |
|---|---|---|
| D1: dedicated `tests/AuthzEntitlements.AppHost.Tests` project in solution with AppHost model smoke test and case-insensitive uniqueness assertion | match | Project in the solution (`AuthzEntitlements.sln`), references AppHost + version-less `Aspire.Hosting.Testing`, 2 tests: `CreateAsync<Projects.AuthzEntitlements_AppHost>()` → `BuildAsync()` with disposal, plus case-insensitive duplicate-name assertion. |
| D2: `Aspire.Hosting.Testing` CPM pin `13.1.0` | match | `Directory.Packages.props:16` pins `Aspire.Hosting.Testing` to `13.1.0`; test project uses a version-less reference. |
| D3: LRN-078 flipped to `applied` at close-out with implementing CS/commit | match | Correctly a close-out action (not content-PR content): at content-review time `LEARNINGS.md` still showed `status: open` and the CS task table left "flip LRN-078" pending for close-out. On-track/planned, not dropped. |

### Test-coverage assessment: sufficient

The guard is meaningful: the tests construct the real AppHost via `DistributedApplicationTestingBuilder.CreateAsync<Projects.AuthzEntitlements_AppHost>()`, which executes the model registration path; the current AppHost registers the previously-colliding resources as `unleash` database / `unleash-server` container (`AppHost.cs:59,65`) and `openfga` database / `openfga-server` container (`AppHost.cs:49,178`), so reintroducing either duplicate name fails during model construction/build. Docker-free criterion holds: `BuildAsync()` only, no `StartAsync`. Decisions #1–#5 honored. Test-count delta: before CS50 there was no AppHost test project; after, exactly 2 `[Fact]` AppHost tests. Reviewer independently ran `dotnet test AuthzEntitlements.sln --filter "FullyQualifiedName~AuthzEntitlements.AppHost.Tests"` → `2/0/0`. No scope creep: `git show 2e27035` changes only `AuthzEntitlements.sln`, `Directory.Packages.props`, the active CS task-table progress, and the new test project — no `src/**` runtime or `.github/workflows/**` changes.
