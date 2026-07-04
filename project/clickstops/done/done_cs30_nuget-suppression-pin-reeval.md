# CS30 — Supply-chain: re-evaluate NuGet audit suppressions & transitive pins

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs30/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Filed by:** yoga-ae-c3 — 2026-07-04, LRN harvest (CS28h): dispositioning open learnings into fix CSs.
**Depends on:** CS01, CS02, CS18

## Goal

Periodically re-evaluate the `NuGetAuditSuppress` entries and the MSBuild transitive pin, dropping any that patched stable package versions have made unnecessary — remediate rather than perpetually suppress.

## Background

Fixes **LRN-003** and **LRN-005**.

LRN-003: 15 NuGet audit advisories are suppressed in `Directory.Build.props` — MessagePack 2.5.192 (2 High + 9 Moderate, transitive via the Aspire dashboard/DCP and not directly controllable) and OpenTelemetry 1.14.0 exporter + Api (4 Moderate, direct via the ServiceDefaults template). They are localhost-only dev-loop packages with no untrusted-input path today.

LRN-005: `Microsoft.Build.Tasks.Core` / `Microsoft.Build.Utilities.Core` are pinned to the patched **17.14.28** (CVE-2025-55247 / GHSA-w3q9-fxm7-j8fq; High, Linux-only design-time MSBuild temp-dir DoS) via CPM transitive pinning in `Directory.Packages.props`, to be dropped once EF Core Design ships against patched MSBuild. CS18 (security hardening) was slated to revisit these but focused on JWT validation, so the re-evaluation is still outstanding.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Re-evaluation approach | Probe the current restore graph (`dotnet list package --vulnerable --include-transitive`) and check for newer **stable** (non-preview) Aspire 13 / OpenTelemetry / EF-Core-Design releases; drop a suppression or pin only where the advisory is genuinely resolved by an available stable version. | LRN-002/003/005 — prefer patched pins over suppression; only a real fix removes the advisory. |
| 2 | Scope guard | No blanket removal. Each removal is validated by clean `--vulnerable` output; any suppression still backed by a live advisory is retained with a dated recheck note plus its advisory link. | Keep the audit gate meaningful; never drop a still-live advisory. |
| 3 | Fail-closed verification | After changes, `dotnet build` under `TreatWarningsAsErrors` stays clean **and** `--vulnerable` shows no new advisories. | The build audit gate must remain the enforcement point. |

## Deliverables

- Updated `Directory.Build.props` / `Directory.Packages.props` dropping resolved suppressions/pins — or, per remaining entry, a dated "still required" note with its advisory link.
- A short `docs/security/` note recording the re-evaluation date and outcome.

## User-approval gates

None — this is dependency hygiene. If no suppression or pin can yet be dropped, the deliverable becomes a dated re-confirmation (still valuable).

## Exit criteria

- Every retained suppression/pin is either dropped (its advisory resolved by an available stable version) or carries a dated "still required" justification with its advisory link — no entry left unreviewed.
- `dotnet build` stays clean under `TreatWarningsAsErrors`.
- `dotnet list package --vulnerable --include-transitive` surfaces no advisory that is not either resolved (dropped) or a documented retained-with-justification entry — i.e. no new or unaccounted advisory. A fully-clean list is required only if every advisory was actually resolvable this cycle.

## Risks + open questions

- Patched stable versions may not exist yet → the outcome may be "re-confirmed, nothing to drop", in which case the dated re-evaluation note is the deliverable.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 19791b20f1d7 | 2026-07-04T17:47:00Z | Needs-Fix | Exit criteria required a clean --vulnerable list, contradicting the "nothing to drop" retained-suppression fallback. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 19791b20f1d7 | 2026-07-04T17:50:00Z | Go | Exit criteria now allow retained advisories with dated justification; fully-clean list required only if all resolvable. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Bump OpenTelemetry pins to patched stable | done | yoga-ae-c4 | Directory.Packages.props: Exporter/Extensions.Hosting/Instrumentation.AspNetCore/Http → 1.16.0, Instrumentation.Runtime → 1.15.1 (Api resolves transitively to 1.16.0 ≥ patched 1.15.3) |
| Add MessagePack transitive pin (remediate, not suppress) | done | yoga-ae-c4 | CPM transitive pin MessagePack 2.5.302 (≥ patched 2.5.301); resolves 11 advisories on transitive 2.5.192 via Aspire.AppHost.Sdk |
| Drop resolved NuGetAuditSuppress entries | done | yoga-ae-c4 | Directory.Build.props: removed all 15 suppressions (4 OTel + 11 MessagePack), now resolved by patched versions |
| Retain MSBuild transitive pin with dated note | done | yoga-ae-c4 | Kept Microsoft.Build.Tasks.Core/Utilities.Core 17.14.28; EF Core Design rc.1 still drags 17.14.8 (GHSA-w3q9-fxm7-j8fq); dated re-confirmation + advisory link added |
| Security re-evaluation note | done | yoga-ae-c4 | docs/security/nuget-audit-reeval-2026-07-04.md records re-eval date + outcome (15 dropped, MSBuild pin retained) |
| Verify | done | yoga-ae-c4 | dotnet build 0/0 under TreatWarningsAsErrors; dotnet test 1063/1063; dotnet list --vulnerable clean across all 20 projects |
| Close-out: docs + restart state | done | yoga-ae-c4 | Updated WORKBOARD (row removed), CONTEXT.md, and added the security re-eval doc |
| Close-out: learnings + follow-ups | done | yoga-ae-c4 | Flipped LRN-003/LRN-005 to applied; next-drop triggers documented in the security note (EF Core RC1→GA + stable Aspire MessagePack) |

## Notes / Learnings

Landed as content PR #95 (squash `23e4036`). All 15 NuGet audit suppressions (4 OpenTelemetry + 11 MessagePack) were **remediated** via patched stable versions — OTel bumped to 1.16.0 (Instrumentation.Runtime 1.15.1) and a MessagePack 2.5.302 CPM transitive pin — clearing `dotnet list --vulnerable` across all 20 projects with a 0/0 build under `TreatWarningsAsErrors`. The `Microsoft.Build.*` 17.14.28 pin is **retained** (dropping it reverts EF Core Design rc.1 to vulnerable 17.14.8) with a dated re-confirmation; the drop-trigger (EF Core RC1→GA) and the MessagePack-pin drop-trigger (stable Aspire with patched MessagePack) are recorded in `docs/security/nuget-audit-reeval-2026-07-04.md`. LRN-003 + LRN-005 flipped to `applied`. Reviewed by GPT-5.5 across R1/R2/R3 (all Go) plus Copilot; Copilot caught a durable-doc clickstop-link durability issue and an OTel wording ambiguity, both fixed.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T20:10:00Z
**Outcome:** GO

Per-deliverable / exit-criteria outcome table:

| Item | Outcome | Evidence |
|---|---|---|
| D1 — props drop resolved suppressions/pins or retain-with-dated-note | match | `Directory.Build.props` drops all 15 `NuGetAuditSuppress` entries; `Directory.Packages.props` bumps OpenTelemetry, adds `MessagePack` 2.5.302, and retains `Microsoft.Build.*` 17.14.28 with a dated CS30 advisory-linked justification. |
| D2 — docs/security re-eval note | match | `docs/security/nuget-audit-reeval-2026-07-04.md` records the 2026-07-04 method, disposition table, outcomes, advisory links, and retained-pin rationale. |
| E1 — every suppression/pin dropped or retained-with-dated-justification | match | All 15 prior suppressions removed; the one retained pin carries a dated CS30 note + advisory link — no entry left unreviewed. |
| E2 — build clean under TreatWarningsAsErrors | match | Reviewer ran `dotnet build AuthzEntitlements.sln` → 0 Warning(s) / 0 Error(s). |
| E3 — `--vulnerable` surfaces no unaccounted advisory | match | Reviewer ran `dotnet list … --vulnerable --include-transitive` → no vulnerable packages across all 20 projects. |

**Test-coverage assessment:** sufficient — CS30 is a dependency/config/doc remediation; the appropriate gates (build under `TreatWarningsAsErrors` + the `--vulnerable` scan) are clean, and no runtime-logic test gap is material.

Reviewer independently inspected the active CS plan, `git show 23e4036 --stat`, the merged diff (`git diff 4704143 23e4036`), and the final on-`main` files, and ran the build + vulnerable scan.
