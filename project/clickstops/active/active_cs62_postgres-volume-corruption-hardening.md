# CS62 — Postgres data-volume corruption hardening: ephemeral e2e DB + recovery runbook + LRN

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** omni-ae-c2 on 2026-07-07 — maintainer request after a live `dotnet test` (RUN_ASPIRE_E2E=1) run failed 4 of 5 e2e tests with an opaque `TaskCanceledException` at `StartAsync`. Root-caused to a **corrupted persistent Postgres data volume** (WAL PANIC "could not locate a valid checkpoint record") left by a force-killed prior run. A prototype fix (ephemeral e2e Postgres) is on branch `fix/e2e-postgres-ephemeral-volume` and is validated (full e2e 5/5). This CS lands that fix properly and closes the durable-tracking gaps (LEARNINGS entry + `aspire run` recovery runbook).
**Depends on:** none (builds on the CS57/58/61 e2e; independent of CS55)

## Goal

Eliminate the persistent-Postgres-data-volume corruption failure mode as a source of e2e flakiness and dev friction: (1) make the **e2e** Postgres ephemeral so a force-killed run cannot corrupt a reused volume; (2) record the failure mode + diagnosis durably in `LEARNINGS.md`; (3) add a short `aspire run` recovery runbook for the dev-runtime persistent volume (which stays persistent by design). No change to `aspire run`'s persistent volume or to CS61's observability persistence.

## Background

- **The failure.** A maintainer `dotnet test` run (`RUN_ASPIRE_E2E=1`) failed 4 of 5 e2e tests: 3 with `TaskCanceledException` at `StartAsync` (~5–6 min caps), 1 (`ApprovalsAntiforgeryE2ETests`) with a Postgres `SocketException 10053` mid-boot; `ExpiredTokenE2ETests` (no DB dependency) **passed**.
- **Root cause.** `src/AuthzEntitlements.AppHost/AppHost.cs:54-55` boots Postgres with `.WithDataVolume()` — a persistent named volume. When a run is force-killed mid-write (a timeout teardown, the node wrapper's `killSignal: SIGKILL`, or a DCP force-stop under load), Postgres leaves a corrupted write-ahead log; the **next** run reuses the volume and Postgres PANICs on startup (`could not locate a valid checkpoint record ... startup process was terminated by signal 6: Aborted`), exits, and the five Postgres-dependent services never reach Healthy — so `StartAsync` blocks to the CTS cap and surfaces as an opaque `TaskCanceledException`. `docker logs <postgres-*>` shows the WAL PANIC; deleting the `*-postgres-data` volume made the smoke test pass in **1m13s**, proving the boot is fast and the timeout was never the cause.
- **Not a timeout, not host load.** Instantaneous CPU was ~12% during the failures; a healthy boot is ~1–1.5 min vs. the 5–6 min caps. `ExpiredToken` passing (no DB dependency) while the DB-dependent tests hung is the signature of a Postgres-specific block. An earlier timeout-bump hypothesis was tested and reverted.
- **Prototype fix already validated.** Branch `fix/e2e-postgres-ephemeral-volume` adds `tests/AuthzEntitlements.E2E.Tests/E2EStack.cs` (`CreateBuilderAsync` strips the Postgres data-volume mount) and routes all 5 e2e tests through it. Verified: each run uses a distinct anonymous volume; full e2e suite **5/5 (6m8s)**; `harness lint` 23/0; `dotnet build` 0/0.
- **Scope boundary.** CS61's `observability` `/data` persistence is deliberately kept (only Postgres is stripped, and its Docker-free AppHost app-model guard stays valid). `aspire run` (dev) keeps its persistent Postgres volume by design (data across restarts); its exposure to the same corruption on a *hard* kill is handled by a recovery runbook, not a code change.
- **Independent of CS55.** CS55 (the `apphost-smoke` CI gate for external `aspire` CLI/DCP skew) is separate; a note in CS55's `## Risks + open questions` cross-references this corruption class for a stack-booting CI gate.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | e2e Postgres isolation | Make the e2e Postgres **ephemeral** — a shared `E2EStack.CreateBuilderAsync` removes the Postgres data-volume mount after `CreateAsync` (asserting **exactly one** `ContainerMountAnnotation` of `Type == Volume` on the `postgres` resource and throwing a clear error if zero or more than one is found), so each run uses a fresh anonymous volume | An ephemeral DB cannot carry a corrupted WAL between runs and gives each run a clean seeded baseline; the tests already use unique per-run ids + lower-bound assertions, so no test semantics change. Confined to test infra — no AppHost / `aspire run` impact. The exact-one assertion keeps the strip fail-closed if a future volume is added. |
| 2 | Scope of the strip | Strip **only** the `postgres` resource's volume mount; leave the `observability` `/data` volume (CS61) and every bind-mount (config, Keycloak realm import) intact | The corruption class is Postgres-WAL-specific; CS61 deliberately persists observability and mechanically guards it, and bind-mounts carry config the boot needs. Targeted minimises blast radius. |
| 3 | `aspire run` dev volume | **Keep** `AppHost.cs`'s persistent `.WithDataVolume()`; do **not** make dev ephemeral. Add a recovery runbook instead | Persistent dev data across restarts is intentional; normal Ctrl+C teardown is graceful (no corruption). Corruption occurs only on a hard kill and is fully recoverable by removing the volume — a documented runbook is proportionate vs. dropping a deliberate dev convenience. |
| 4 | Durable knowledge | File a `LEARNINGS.md` entry (next free LRN id) capturing the failure mode, the diagnosis path (`docker logs` → WAL PANIC), and the ephemeral-e2e fix + dev recovery, `source_cs: CS62` | Repo rule: knowledge lives in the repo, not agent memory. The opaque-`TaskCanceledException` → WAL-corruption link is non-obvious and will save future e2e debugging time. |
| 5 | No timeout change | Do **not** change the e2e `CancellationTokenSource` caps | A healthy boot is ~1–1.5 min; the 5–6 min caps have ample headroom. The failure was volume corruption, not slowness — bumping caps would mask, not fix, and slow real failures. |

## Deliverables

- **`tests/AuthzEntitlements.E2E.Tests/E2EStack.cs`** — `CreateBuilderAsync(CancellationToken)` that creates the AppHost testing builder and removes the `postgres` resource's data-volume mount, making the e2e Postgres ephemeral. (Prototyped on `fix/e2e-postgres-ephemeral-volume`.) **Fail-closed:** assert exactly one `ContainerMountAnnotation` with `Type == ContainerMountType.Volume` on the `postgres` resource and throw a clear error if zero or more than one is found, so a future AppHost/Aspire change that adds another Postgres volume surfaces loudly instead of being silently stripped.
- **`tests/AuthzEntitlements.E2E.Tests/*E2ETests.cs` (all 5)** — route each `DistributedApplicationTestingBuilder.CreateAsync<Projects.AuthzEntitlements_AppHost>` call through `E2EStack.CreateBuilderAsync`; correct the now-stale "DB … accumulates across runs" comment.
- **`docs/testing/e2e-smoke.md`** — document that the e2e boots against an ephemeral Postgres (its data volume is stripped), so each run starts clean from the seeded baseline; note `docker volume prune` for dangling per-run anonymous volumes.
- **`docs/demo/local-demo-runbook.md` — `aspire run` recovery runbook.** Add a Troubleshooting subsection: if `aspire run`'s Postgres fails to start with a WAL PANIC (`could not locate a valid checkpoint record` in `docker logs`) after a hard kill, remove its persistent data volume and re-run. Include (a) volume-identification guidance (`docker volume ls` → the `authzentitlements.apphost-*-postgres-data` name) and (b) an explicit **⚠️ data-loss warning** that `docker volume rm` discards all local dev bank data (which is re-seeded on the next boot).
- **`LEARNINGS.md`** — a new LRN entry per Decision #4 (`source_cs: CS62`).
- **Validation** — full e2e suite 5/5 under `RUN_ASPIRE_E2E=1`, each run on a fresh anonymous Postgres volume (no `*-postgres-data` named volume created); `harness lint` green; `dotnet build` 0/0; LF / no-BOM.

## User-approval gates

- **Keeping `aspire run` persistent (Decision #3) vs. making dev ephemeral.** If the maintainer prefers dev Postgres also ephemeral (accepting loss of cross-restart data), that is a one-line config-gated change in `AppHost.cs` instead of a runbook — surface rather than silently choose. Default: keep persistent + runbook.

## Exit criteria

- The 5 e2e tests pass under `RUN_ASPIRE_E2E=1`, each booting a fresh (anonymous) Postgres volume; the e2e creates no `*-postgres-data` named volume.
- CS61's `observability` `/data` persistence and its Docker-free AppHost app-model guard remain green; `aspire run` still mounts the persistent Postgres volume (`AppHost.cs` unchanged).
- `docs/testing/e2e-smoke.md` + the recovery runbook document the behavior; `LEARNINGS.md` carries the new entry; `harness lint` green; LF / no-BOM.

## Risks + open questions

- **Dangling anonymous volumes.** An ephemeral Postgres leaves a per-run anonymous volume if DCP does not remove it with `-v`; benign and prunable (`docker volume prune`) — documented, not blocking.
- **Aspire mount-API stability.** Stripping `ContainerMountAnnotation` relies on the `Aspire.Hosting.ApplicationModel` mount API — the same surface CS61's AppHost app-model guard already asserts against — pinned via CPM; revalidate on an Aspire bump.
- **Seeding on a fresh DB each run.** Ephemeral means migrations + seeders run every boot; adds a little boot time but stays within caps (validated 5/5).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck (cs62-plan-review) | 56043afd2cd7 | 2026-07-08T05:28:12Z | Go-with-amendments | Facts verified (WithDataVolume, 5 e2e tests, mount API, CS61, fix branch). Applied both amendments: runbook pinned + data-loss warning; volume-strip fail-closed. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
