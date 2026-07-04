# CS33 — Consolidate durable learnings into project-local convention/review doc blocks

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs33/content
**Started:** 2026-07-04
**Closed:** —
**Filed by:** yoga-ae-c3 — 2026-07-04, LRN harvest (CS28h): dispositioning open learnings into fix CSs.
**Depends on:** none

## Goal

Propagate durable, generalizable how-to learnings from `LEARNINGS.md` into the project-local blocks of `CONVENTIONS.md` (dotnet/Aspire/Keycloak/Blazor/EF/adapter conventions) and `REVIEWS.md` / `OPERATIONS.md` (review/Copilot/merge/citation/CI-evidence process gotchas), per the "knowledge lives in the repo" doctrine — then flip each consolidated learning to `applied`.

## Background

Applies (does **not** "fix" — these are durable how-to learnings, not defects): LRN-001, 002, 004, 007, 008, 009, 010, 011, 012, 015, 016, 017, 018, 019, 020, 021, 022, 023, 024, 025, 026, 027, 028, 029, 030, 032, 034, 036, 037, 039, 041, 042, 043, 045, 046, 047, 048 (37 how-to entries). LRN-031 is tracked in full by CS31 (its follow-ups are code changes) and is not consolidated here.

The LEARNINGS backlog — 46 open entries (of 49 total; 2 deferred, 1 obsolete) — has accumulated with ~zero harvest since project start; most are durable how-to insights better served as convention/review doc content than as perpetually-`open` LEARNINGS entries (which carry a 14-day age-out warning). The project-local blocks already exist: `CONVENTIONS.md` `id=conventions.project` is already ~3.9k chars, while `REVIEWS.md` `id=reviews.project-gates` and `OPERATIONS.md` `id=operations.project-deploy` are near-empty placeholders. These local blocks are project-owned and preserved across `harness sync` (file-class model: composed).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Destination mapping | dotnet/Aspire/Keycloak/Blazor/EF/adapter gotchas → `CONVENTIONS.md` `conventions.project`; review/Copilot/merge/citation/CI-evidence gotchas → `REVIEWS.md` `reviews.project-gates` (and/or the OPERATIONS `operations.project-deploy` block). | File-class model — edit only the allowlisted local blocks, never managed-core. |
| 2 | Granularity | Consolidate into concise convention bullets grouped by theme (not a verbatim dump); each bullet cites its source LRN id(s). | Durable and discoverable without doc bloat. |
| 3 | Disposition | After landing, flip each consolidated LRN to `applied` with `**Disposition:**` set to the destination doc block plus the commit SHA. | RETROSPECTIVES applied-status requires a recorded landing spot. |
| 4 | Scope guard | Edit only content between the `harness:local-start` / `harness:local-end` markers; do not touch managed-core sections of any composed file. | Composed-file discipline — the file-class model allows editing project-local blocks only. |

## Deliverables

- `CONVENTIONS.md` `conventions.project` extended with consolidated dotnet/authz conventions, each LRN-cited.
- `REVIEWS.md` `reviews.project-gates` (plus the OPERATIONS `operations.project-deploy` block if needed) extended with consolidated review/process gotchas.
- `LEARNINGS.md` consolidated entries flipped to `applied` with a recorded disposition.

## User-approval gates

None — project-local doc content only; no managed-core or code changes.

## Exit criteria

- Every targeted how-to LRN is either reflected in a project-local doc block and marked `applied`, or explicitly retained `open` with a stated reason.
- `harness lint` green.
- No managed-core edits — verified with `harness sync --mode=check` showing no drift.

## Risks + open questions

- Large doc surface — keep the consolidation concise.
- Strict composed-file discipline: edit only the project-local blocks, never managed-core.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 8223e3cb4097 | 2026-07-04T17:47:00Z | Go | LRN list matches harvest table, excludes LRN-031, edits restricted to project-local composed blocks. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Consolidate dotnet/Aspire/Keycloak/Blazor/EF/adapter gotchas | done | yoga-ae-c3 | 25 LRNs into CONVENTIONS.md `conventions.project`, themed + LRN-cited |
| Consolidate review/Copilot/merge/citation/CI-evidence gotchas | done | yoga-ae-c3 | 12 LRNs into REVIEWS.md `reviews.project-gates` |
| Flip consolidated how-to LRNs to applied | done | yoga-ae-c3 | All 37 flipped to applied with Disposition (doc block + PR #119 + `8c71a23`) |
| Verify no managed-core edits | done | yoga-ae-c3 | `harness sync --mode=check` → no drift; only local blocks edited |
| Close-out: docs + restart state | done | yoga-ae-c3 | CONTEXT.md updated |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | 37 how-to LRNs applied; harvest queue (CS29-CS33) complete |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T21:58:56Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| `CONVENTIONS.md` `conventions.project` extended, LRN-cited | match | 25 targeted convention LRNs consolidated + cited in the local block |
| `REVIEWS.md` `reviews.project-gates` extended | match | 12 targeted review/process LRNs consolidated + cited in the local block |
| `LEARNINGS.md` entries flipped to `applied` with disposition | match | All 37 `status: applied` under `## Applied`; dispositions cite the destination doc block + PR #119 + commit `8c71a23` |

**Test coverage:** N/A (docs only) — `harness lint` 0 failed; `harness sync --mode=check` **no drift** (only the allowlisted local blocks edited).

**Outcome GO:** All deliverables + exit criteria met. R1 returned NEEDS-FIX for one mechanical item — the 37 dispositions cited PR #119 without the landing commit SHA required by plan Decision #3 — fixed by adding `8c71a23` to all 37 → **R2 GO**. Content review: GPT-5.5 R1 Needs-Fix (LRN-002 citation overstatement) → R2 Go → narrow re-attest (doc nits) + Copilot (3 nits: PR-ref/command-placeholders/LF-recipe, resolved).
