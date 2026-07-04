# CS30 — Supply-chain: re-evaluate NuGet audit suppressions & transitive pins

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs30/content
**Started:** 2026-07-04
**Closed:** —
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
| Bump OpenTelemetry pins to patched stable | pending | — | Directory.Packages.props: Exporter/Extensions.Hosting/Instrumentation.AspNetCore/Http → 1.16.0, Instrumentation.Runtime → 1.15.1 (>= patched 1.15.3 for Api/Exporter advisories) |
| Add MessagePack transitive pin (remediate, not suppress) | pending | — | CPM transitive pin MessagePack 2.5.302 (>= patched 2.5.301); resolves 11 advisories on transitive 2.5.192 via Aspire.AppHost.Sdk |
| Drop resolved NuGetAuditSuppress entries | pending | — | Directory.Build.props: remove all 15 suppressions (4 OTel + 11 MessagePack) now resolved by patched versions |
| Retain MSBuild transitive pin with dated note | pending | — | Keep Microsoft.Build.Tasks.Core/Utilities.Core 17.14.28; EF Core Design rc.1 still drags 17.14.8 (GHSA-w3q9-fxm7-j8fq); add dated re-confirmation + advisory link |
| Security re-evaluation note | pending | — | docs/security/ note recording 2026-07-04 re-eval date + outcome (15 dropped, MSBuild pin retained) |
| Verify | pending | — | dotnet build 0/0 under TreatWarningsAsErrors; dotnet test green; dotnet list package --vulnerable --include-transitive clean |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and security docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | Flip LRN-003/LRN-005 to applied; file/disposition learnings; open follow-up CSs for unresolved issues (EF Core RC1→GA MSBuild pin drop) |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
