# CS55 — apphost-smoke CI gate: actually run the AppHost so DCP/CLI/runtime skew fails CI

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-05 — surfaced while landing the `.NET 10 GA + Aspire 13.4.6 lockstep` maintenance PR (#189, squash `357b08d`), which repaired the `aspire run` DCP version-mismatch from Dependabot #105 and added a *static* guard (`AppHostAspireVersionLockstepTests`). The static guard checks manifest versions only; the original blocker was a **runtime** DCP handshake failure that only actually booting the AppHost can catch. This CS turns the manual `aspire run` validation done in #189 into a repeatable CI gate.
**Depends on:** none (builds on the merged #189; not blocked by it)

## Goal

Add a CI job — `apphost-smoke` — that **actually boots the Aspire AppHost** (not just `dotnet build`/`dotnet test`) and fails CI if the DCP orchestrator handshake fails with a version-mismatch / "JSON-RPC connection lost" error. This closes the gap the static `AppHostAspireVersionLockstepTests` guard structurally cannot cover: (a) the **externally-installed `aspire` CLI / DCP** version (it is not in `Directory.Packages.props`), (b) a real **runtime** DCP↔runtime handshake, and (c) resource-wiring breakage. Plain `dotnet build`/`dotnet test` never boots the orchestrator, so today only a human running `aspire run` locally exercises the exact failure mode #105 introduced.

## Background

- **The #189 fix + its static guard.** #189 brought the Aspire family to 13.4.6 lockstep, moved .NET to GA 10.0.301, and added `tests/AuthzEntitlements.AppHost.Tests/AppHostAspireVersionLockstepTests.cs`. That test parses the AppHost `<Project Sdk="Aspire.AppHost.Sdk/…">` attribute and the `Aspire.Hosting.*` `PackageVersion`s and asserts they share a base version — a **manifest-level** guard. It deterministically fails `dotnet test` on a single-package Dependabot bump (the #105 shape).
- **What the static guard cannot see.** The `aspire` CLI + DCP are installed out-of-band (`~/.aspire/bin/aspire.exe`), not pinned in the repo, so a CLI↔project skew is invisible to it. It also cannot catch a genuine runtime DCP handshake failure or broken resource wiring — those only surface when the AppHost actually boots. The #105 blocker was exactly a runtime DCP version-check ("Newer version of the Aspire.Hosting.AppHost package is required" / JSON-RPC connection lost).
- **The manual validation in #189 should be mechanical.** Landing #189 required manually: install GA SDK + the matching Aspire CLI, `aspire run`, confirm the dashboard serves HTTP 200 and `postgres`/`keycloak`/`otel-lgtm` containers come up with **no DCP version-mismatch error**, then tear down. That is precisely the assertion a CI gate should own so it never depends on an operator remembering to run it.
- **Boot surface (from `src/AuthzEntitlements.AppHost/AppHost.cs`).** The default `aspire run` critical path starts an `observability` (grafana/otel-lgtm) container, a `postgres` container (+6 databases), `keycloak`, and the .NET service projects; unleash/opa/openfga/spicedb are `.WithExplicitStart()` and stay off the default path. A CI smoke needs Docker for the critical-path containers, so cost/time and flakiness are first-order design constraints.
- **Update since filing (2026-07-07) — e2e landscape changed; CS55's unique scope narrowed.** After filing (2026-07-05), CS57/CS58/CS61 landed a full in-process e2e (`tests/AuthzEntitlements.E2E.Tests`, in the solution + wired into `harness startup`) that **already boots the real Aspire stack** (Keycloak + Postgres + observability + all 7 services) via `Aspire.Hosting.Testing` `StartAsync` and asserts health/OIDC/auth/telemetry. But it orchestrates through the **package-pinned** DCP (`Directory.Packages.props`) — the versions the static `AppHostAspireVersionLockstepTests` already guards — and never installs/exercises the **external `aspire` CLI**. So CS55's genuinely-unique value narrows to (a) booting via the real `aspire run` **CLI** to catch CLI↔project/DCP skew (the #105 mode, per Decision #3), and (b) the Docker-in-CI enforcement CS57 explicitly deferred (its e2e is a local honor-system gate). The Decisions/Deliverables are unchanged, but a claimer should weigh whether the cheaper Docker-in-CI path is to run the **existing** e2e (`RUN_ASPIRE_E2E=1`, richer coverage), reserving a bespoke `aspire run --detach` gate for the CLI-skew dimension.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Add a runtime boot gate | A CI job `apphost-smoke` that **boots the AppHost** and fails on a DCP version-mismatch / JSON-RPC-lost error, complementing (not replacing) the static `AppHostAspireVersionLockstepTests` | The static guard catches manifest drift; only actually booting catches CLI/DCP/runtime skew — the real #105 failure mode. Defense-in-depth: static (fast, always) + smoke (slow, runtime). |
| 2 | Boot mechanism | Prefer `aspire run --detach` (starts the AppHost in the background, exits after start; `--format json` for a structured result) with a bounded timeout, then explicit teardown. The timed `dotnet run` fallback is **temporary/degraded**: it validates the AppHost/DCP runtime boot but **not** the external `aspire` CLI path, so it does not satisfy the CLI-skew assertion (Decision #3) unless that is validated separately | `--detach` is the CLI's own "start then exit after started" mode — a natural fit for a boot-and-assert gate — and returns after the DCP handshake, which is exactly what we assert. The fallback keeps the gate runnable if `--detach` is flaky in CI, at the cost of the CLI dimension. |
| 3 | Aspire CLI version in CI | Install the `aspire` CLI at the version parsed from the **AppHost csproj `Aspire.AppHost.Sdk/<version>` attribute** (the clean canonical source), via the official `aspire.dev` install script pinned to that base version; use the `Aspire.Hosting.*` package versions only as a cross-check | Makes the job also guard CLI↔project skew — the dimension the static test cannot see. The `<Sdk>` attribute is a single stable value; `Directory.Packages.props` mixes stable (`13.4.6`) and preview (`13.4.6-preview…`, e.g. Keycloak) entries, so a naive package parse could install a wrong/nonexistent CLI. |
| 4 | Pass/fail assertion | Pass iff the AppHost reaches "started" (DCP connected, dashboard endpoint responds) with **no** DCP version-mismatch / JSON-RPC-lost error in the CLI/AppHost logs; **do not** require every resource `Healthy` | Asserting full resource health makes the gate slow and flaky (image pulls, container races). The blocker we guard against is the DCP handshake, which is established at boot — assert that, keep it fast and stable. |
| 5 | Runner + Docker | Linux runner with Docker (`ubuntu-latest`); cache the .NET/Aspire toolchain where possible. Note: the `AddContainer` resources (observability, opa, openfga, spicedb, …) pin explicit image tags in `AppHost.cs`, but the critical-path `postgres`/`keycloak` come from the `AddPostgres`/`AddKeycloak` **integration defaults** (no explicit tag) — a deliverable pins those two explicitly for reproducibility | The critical-path resources are Linux containers; `ubuntu-latest` ships Docker. Explicit tags on **every** critical-path image + toolchain caching bound the flakiness/time budget. |
| 6 | Rollout posture | Land the job **advisory (non-required) first**, watch it across several PRs for stability, then a follow-up promotes it to a **required** status check via a branch-protection change (maintainer-approved) | A brand-new Docker-in-CI gate should prove non-flaky before it can block merges. Promotion to required is a maintainer decision (branch-protection ruleset), not an implementer one. |
| 7 | Skew-catch verification | Verify the gate by temporarily reintroducing a version skew (e.g. bump one `Aspire.Hosting.*` or the CLI out of lockstep) on a throwaway branch and confirming `apphost-smoke` **fails**, then reverting | A gate that never demonstrably fails on the condition it guards is untrustworthy. Prove it catches the #105 shape before trusting it. |

## Deliverables

- A CI job `apphost-smoke` (new `.github/workflows/apphost-smoke.yml` or an added job in an existing workflow) that: checks out, installs .NET 10 GA from `global.json`, installs the Aspire CLI at the version parsed from the AppHost csproj `Aspire.AppHost.Sdk/<version>` attribute (Decision #3), boots the AppHost (`aspire run --detach`, bounded timeout), asserts a clean DCP handshake + dashboard reachability, scans logs for version-mismatch / JSON-RPC-lost, then tears down all started containers.
- An explicit deliverable pinning the critical-path `postgres` and `keycloak` image tags in `AppHost.cs` (they currently ride the `AddPostgres`/`AddKeycloak` integration defaults), so the smoke boot is reproducible and not silently retagged by an integration bump.
- A small helper (`scripts/apphost-smoke.mjs` or a shell step) that encapsulates: parse the CLI version from the AppHost `<Sdk>` → install → boot → poll dashboard/health → assert-no-DCP-error → teardown, so the same check is runnable locally and in CI.
- Unconditional (`always()`) teardown that stops the **detached Aspire session / AppHost / DCP** (e.g. `aspire stop` or a session-aware stop) **and** removes AppHost-generated containers even on failure/timeout (no orphaned `postgres`/`keycloak`/`observability`/`tunnelproxy` containers or leftover DCP/dashboard process on the runner — mirroring the manual cleanup done in #189).
- Captured **negative-test evidence** for Decision #7 (a CI-log excerpt or PR note showing that an intentionally-introduced Aspire CLI/package skew made `apphost-smoke` fail, then reverted), so the gate's skew-catching is auditable.
- A short CI doc (e.g. under `docs/ci/`) describing what `apphost-smoke` asserts, why it complements the static guard, its advisory→required rollout, and the `.gitignore`d `aspire.config.json` the CLI generates.
- A `LEARNINGS.md` entry (if warranted at implementation) recording the "static guard ≠ runtime boot; only booting catches CLI/DCP skew" finding, `source_cs: CS55`.
- The follow-up hook: a note (or a separate planned CS) for promoting the gate to a required branch-protection check once stable (Decision #6).

## User-approval gates

- **Making `apphost-smoke` a required status check** (branch-protection ruleset change) — maintainer approval required; land advisory first (Decision #6).
- **CI cost/time posture** — Docker-in-CI plus image pulls add minutes per PR; confirm the acceptable time budget and whether to trim the boot to a minimal resource subset.

## Exit criteria

- `apphost-smoke` runs on PRs, installs GA .NET 10 + the project-matched Aspire CLI, boots the AppHost, and **passes** on `main` (clean DCP handshake, dashboard reachable, no version-mismatch / JSON-RPC-lost); it **fails** when a deliberately-introduced Aspire CLI/package skew is present (verified per Decision #7) and cleans up all started containers even on failure.
- The static `AppHostAspireVersionLockstepTests` guard remains and both are green together.
- CI doc added; `harness lint` green (clickstop, text-encoding LF/BOM, xref durability). Advisory posture documented, with the required-check promotion left as an explicit, maintainer-approved follow-up.

## Risks + open questions

- **CI flakiness / time.** Image pulls (otel-lgtm is large) and container start races can make the job slow or flaky — mitigate with pinned tags, toolchain caching, a bounded-but-generous timeout, a minimal boot subset, and the advisory-first rollout.
- **`aspire run --detach` behaviour in CI.** Needs validation that `--detach` reliably returns only after the DCP handshake and yields a parseable "started" signal; the timed `dotnet run` fallback (Decision #2) covers the case where it does not.
- **Teardown reliability.** A killed/timed-out boot can orphan containers; teardown must be unconditional (`always()`), matching the manual cleanup done in #189.
- **Runner Docker assumptions.** Assumes `ubuntu-latest` Docker availability and Linux containers; a self-hosted or Windows runner would change the design.
- **Open question — boot scope.** Full default critical path vs a trimmed "DCP-handshake-only" boot: the former is closest to the real dev loop, the latter is faster/stabler. Resolve at implementation with a measured time budget.
- **Persistent Postgres data-volume corruption (found 2026-07-07).** `AppHost.cs` boots Postgres with `.WithDataVolume()` (a persistent named volume). A stack boot force-killed mid-write — exactly what a bounded-timeout / `always()`-teardown CI gate does — leaves a corrupted WAL, and the **next** boot's Postgres PANICs (`could not locate a valid checkpoint record`) → exits → the Postgres-dependent services never reach Healthy → the boot hangs to the cap (an opaque failure, *not* a version-mismatch, so it can be mistaken for the very signal this gate watches for). A stateful `apphost-smoke` gate must therefore boot on a **clean/ephemeral** Postgres volume per run (a fresh runner, or `docker volume rm`/`volume prune` pre-boot), never a reused one. This was fixed for the in-process e2e by **CS62** — the fail-closed ephemeral-Postgres strip in `tests/AuthzEntitlements.E2E.Tests/E2EStack.cs` (plus an `aspire run` recovery runbook and LRN-094); the CI gate needs equivalent hygiene. It also reinforces Deliverable 2 (pin the `postgres`/`keycloak` image tags): an unpinned Postgres image rolling to a new major version would make a *persistent* data dir incompatible and fail the boot for the same class of reason.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs55-plan-review (rubber-duck) | 77dab52f9d5b | 2026-07-06T00:36:29Z | Go-with-amendments | Verified static guard manifest-only + AppHost.cs path; amended: CLI ver from AppHost <Sdk>, pin pg/keycloak tags, scope dotnet-run fallback, always() teardown, skew-catch evidence. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
