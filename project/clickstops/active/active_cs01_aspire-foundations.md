# CS01 — Aspire solution foundations

**Status:** active
**Owner:** yoga-ae
**Branch:** cs01/content
**Started:** 2026-07-03
**Closed:** —
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
  audit advisories are promoted to build errors at restore time. Three advisories on
  preview/RC-era packages currently block a clean build: **MessagePack 2.5.192** (High,
  GHSA-hv8m-jj95-wg3x — transitive via `Aspire.AppHost.Sdk` dashboard/DCP, not directly
  controllable) and **OpenTelemetry.Exporter.OpenTelemetryProtocol / OpenTelemetry.Api 1.14.0**
  (Moderate, GHSA-q834-8qmm-v933 / GHSA-g94r-2vxg-569j — from the `aspire-servicedefaults`
  template). Mitigation: scoped `<NoWarn>NU1902;NU1903</NoWarn>` in `Directory.Build.props`
  (NU1904 critical + all compiler warnings-as-errors stay active). These are localhost-only
  dev-loop packages in CS01 (no untrusted input path). **Revisit in CS18 (security hardening):**
  remove the suppression once non-vulnerable stable Aspire 13 / OTel packages ship, or pin
  patched versions via CPM.
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

> _(filled at close-out per the gate)_
