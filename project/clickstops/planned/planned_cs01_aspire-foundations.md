# CS01 — Aspire solution foundations

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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
| Install aspire CLI + templates | pending | — | |
| Create AppHost + ServiceDefaults | pending | — | |
| Add PostgreSQL integration + logical DBs | pending | — | |
| Add central package management | pending | — | |
| Verify dashboard + build | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
