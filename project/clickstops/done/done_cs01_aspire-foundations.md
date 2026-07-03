# CS01 — Aspire solution foundations

**Status:** done
**Owner:** yoga-ae
**Branch:** cs01/content
**Started:** 2026-07-03
**Closed:** 2026-07-03
**Phase:** 0 — Foundations
**Lane:** Foundation
**Depends on:** None

## Goal

Stand up the .NET Aspire solution skeleton that every other CS builds on.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 18df1ade0426 | 2026-07-02T19:47:54Z | Go | Foundational scope and no dependencies are coherent; logical DBs unblock later lanes without owning their business logic. |

## Deliverables

- Aspire AppHost + ServiceDefaults projects; solution file; central package management (Directory.Packages.props).
- PostgreSQL Aspire integration with logical DBs (bank, openfga, entitlements, governance, audit).
- Aspire dashboard verified via `aspire run`.

## Exit criteria

- `aspire run` starts the AppHost and dashboard.
- Postgres resource is healthy; solution builds clean.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Install aspire CLI + templates | done | yoga-ae | Aspire CLI 13.1.0 present; aspire-apphost + aspire-servicedefaults templates verified |
| Create AppHost + ServiceDefaults | done | yoga-ae | src/AuthzEntitlements.AppHost + src/AuthzEntitlements.ServiceDefaults (net10.0) |
| Add PostgreSQL integration + logical DBs | done | yoga-ae | Aspire.Hosting.PostgreSQL; postgres resource + 5 DBs (bank, openfga, entitlements, governance, audit) |
| Add central package management | done | yoga-ae | Directory.Packages.props (CPM); Directory.Build.props; global.json pins .NET 10 RC SDK |
| Verify dashboard + build | done | yoga-ae | dotnet build 0/0; `aspire run` → dashboard :17254 (302→login), postgres:17.6 container healthy |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and any feature docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; create planned follow-up CSs for unresolved issues |

## Notes / Learnings

- **Security-audit suppression (follow-up for CS18).** Under `TreatWarningsAsErrors`, NuGet
  audit advisories are promoted to build errors at restore time. **15 advisories across 3
  preview/RC-era packages** currently apply: **MessagePack 2.5.192** (2 High + 9 Moderate —
  transitive via `Aspire.AppHost.Sdk` dashboard/DCP, not directly controllable),
  **OpenTelemetry.Exporter.OpenTelemetryProtocol 1.14.0** (3 Moderate) and **OpenTelemetry.Api
  1.14.0** (1 Moderate — from the `aspire-servicedefaults` template). Mitigation: per-advisory
  `<NuGetAuditSuppress>` entries listing the specific GHSA IDs (NOT a blanket NU1902/NU1903
  code) in `Directory.Build.props`, so any NEW advisory — on these or any other package — still
  fails the build. These are localhost-only dev-loop packages in CS01 (no untrusted-input
  path). **Revisit in CS18 (security hardening):** drop entries as non-vulnerable stable
  Aspire 13 / OTel packages ship, or pin patched versions via CPM.
- Aspire 13 tooling notes: `aspire add <integration>` fails in a non-interactive/agent shell
  (exit 5) — use `dotnet add package Aspire.Hosting.<X>` instead. `aspire-apphost` template
  emits `AppHost.cs` (not `Program.cs`).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-03T01:11:20Z
**Outcome:** GO

Per-deliverable outcome (plan vs. merged content `b51efb6..85ad4f0`):

| Deliverable / Exit criterion | Outcome | Rationale |
|---|---|---|
| Aspire AppHost + ServiceDefaults + solution + Central Package Management | match | Solution, both projects, and `Directory.Packages.props` exist; no `Version=` on any `PackageReference`. |
| PostgreSQL integration + 5 logical DBs (bank, openfga, entitlements, governance, audit) | match | `AppHost.cs` defines one `postgres` resource + all five DBs, matching ARCHITECTURE.md Stores. |
| Aspire dashboard verified via `aspire run` | match | Dashboard reachable at `:17254` (302→login); `launchSettings` supports it. |
| Exit: `aspire run` starts AppHost + dashboard | match | Runtime evidence accepted. |
| Exit: Postgres healthy + solution builds clean | match | `postgres:17.6` container healthy; `dotnet build` 0/0. |
| global.json / Directory.Build.props / per-advisory NuGetAuditSuppress / `.aspire/` ignore | added | Not promised but positive: SDK-pin reproducibility, strict build defaults, narrow advisory suppression (new advisories still fail), no tracked local Aspire state. |

**Test coverage:** sufficient — zero automated tests is acceptable for a pure-scaffolding foundations CS (no business logic); the meaningful checks are build + Aspire runtime smoke, both verified externally. Domain tests begin in CS02.
