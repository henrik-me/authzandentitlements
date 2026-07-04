# CS35 — Weekly harvest: consolidate LRN-050..064 into project doc blocks

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c3 — 2026-07-04, weekly LRN harvest: dispositioning the post-CS33 open batch (LRN-050..064, filed by CS15/CS19/CS22/CS23/CS25) into project doc blocks + a follow-up code CS.
**Depends on:** none

## Goal

Run the weekly harvest over the 15 `open` learnings LRN-050..064 (the batch filed
*after* CS33's 37-entry consolidation) plus the 2 `deferred` entries LRN-035/LRN-040.
Propagate every durable, generalizable how-to into the project-local blocks of
`CONVENTIONS.md` (`conventions.project`) and `REVIEWS.md` (`reviews.project-gates`),
flip each consolidated entry to `applied` with a recorded disposition, flip the two
already-landed entries (LRN-059, LRN-062) to `applied`, file the one genuine code
follow-up (LRN-057) as planned **CS36**, and keep LRN-035/LRN-040 `deferred` per the
multi-agent boundary — all per the "knowledge lives in the repo" doctrine and the
RETROSPECTIVES weekly-harvest procedure.

## Background

CS33 (PR #119, merged `8c71a23`) consolidated the first 37 how-to learnings into the
project-local blocks and flipped them `applied`. LRN-050..064 were filed *afterward*
by CS15/CS19/CS22/CS23/CS25 as those clickstops closed out, so they never went through
a harvest and are accumulating the 14-day `check-learnings` age-out warning. Most are
durable how-to insights better served as convention/review doc content than as
perpetually-`open` LEARNINGS entries.

Two are already landed and only need a disposition flip:
- **LRN-059** — the repo-wide `ILogger` CR/LF log-forging (CWE-117) sanitization sweep
  it called for was delivered by **CS34** (`LogSanitizer` in `ServiceDefaults`, applied
  across the OpenFGA / Edge.Gateway / Bank.Api sinks).
- **LRN-062** — the ADR-format extension it called for already shipped in
  `CONVENTIONS.md` (`conventions.project` ADR block, PR #111).

One overlaps an existing bullet:
- **LRN-054** (Blazor `CS0542` from an `@inject` member colliding with the component
  type) refines the existing `CONVENTIONS.md` CS0542 note (from LRN-048) — refine +
  co-cite, do not add a duplicate bullet.

One is a genuine code change, not a doc consolidation:
- **LRN-057** — faithful 1:1 audit "replay" needs the PDP to persist the full
  `AccessRequest` snapshot per audit row (a security-critical change to the CS13
  tamper-evident store). File as planned **CS36**; leave LRN-057 `open`, linked to CS36.

Two are `deferred` with enforcement owned elsewhere:
- **LRN-035 / LRN-040** — their residual (making the advisory `dotnet-ci` build/test a
  *required* merge check) is being delivered by **yoga-ae-c5**'s in-flight
  branch-protection / CI-merge-gating maintenance (per WORKBOARD 2026-07-04). Their
  `deferred_until` (2026-10-01) has not been reached. Keep `deferred`; refresh the note
  only — do not touch branch-protection, `.github/workflows/`, or Dependabot (that
  boundary belongs to yoga-ae-c5).

The project-local blocks already exist and are the only surfaces this CS edits:
`CONVENTIONS.md` `conventions.project` (themed convention bullets) and `REVIEWS.md`
`reviews.project-gates` (review/CI/merge process gates). Both are project-owned
`composed` local blocks preserved across `harness sync`.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Harvest scope | Disposition all 15 `open` (LRN-050..064): consolidate durable how-tos into the two existing project-local blocks and flip each `applied`; file LRN-057 as CS36 and leave it `open` linked to CS36; keep LRN-035/LRN-040 `deferred` (residual owned by yoga-ae-c5's branch-protection work; `deferred_until` not reached) with only a refreshed note. | Mirrors the CS33 harvest pattern; honours the RETROSPECTIVES weekly-cadence dispositions (apply-upstream / file-a-CS / obsolete / defer) and the multi-agent ownership boundary. |
| 2 | Destination mapping | Code/domain patterns → `CONVENTIONS.md` `conventions.project`: LRN-050 (fail-closed 3-way live-probe classification), LRN-051 (OTLP→Prometheus metric-name mangling rule), LRN-054 (Blazor CS0542 refine+co-cite), LRN-058 (OBO delegation ordinal allow-list), LRN-059 (CR/LF log-sanitization convention), LRN-064 (eval-doc economics: model-not-figure + dated Sources + honesty caveat). Review/CI/merge process → `REVIEWS.md` `reviews.project-gates`: LRN-052+LRN-053 (CI zero-step failure triage: `started_at`/Copilot-billing check → local `harness pr-evidence` substitute / make-repo-public unblock + documented admin-merge), LRN-055 (re-fix PR body after `harness review`), LRN-056 (trial-merge build/test before admin-merge to preserve A4 evidence), LRN-060 (repo-goes-public-mid-CS procedure), LRN-061 (parallel doc sub-agents share one tree → aggregate text-encoding lint caveat), LRN-063 (docs-PR Copilot review loop → resolve non-blocking style threads with rationale). | File-class model — edit only the allowlisted project-local blocks, never managed-core; keep the CS33 mapping convention (domain/code gotchas → CONVENTIONS, review/process gotchas → REVIEWS). |
| 3 | Already-landed flips | Flip **LRN-059** `applied` (Disposition: CS34 `LogSanitizer` sweep + a new CR/LF-sanitization convention bullet in `conventions.project`) and **LRN-062** `applied` (Disposition: the existing `conventions.project` ADR block, PR #111) — add at most a co-citation, no re-implementation. | Accurate `applied` disposition per RETROSPECTIVES; avoids re-doing shipped work. |
| 4 | Dedup guard | LRN-054 refines the existing CONVENTIONS.md CS0542 bullet (LRN-048) to the precise "generated component class is named after the file, so an `@inject`/member named the same collides" wording and co-cites LRN-048+LRN-054 — no duplicate bullet. LRN-051 adds the OTLP→Prometheus mangling rule to the existing "Dev observability" subsection; the non-SoD-panel scrape-verification stays a documented caveat in the compliance-dashboard doc (verify the caveat exists; the mangling rule itself is code-verifiable). | Durable + discoverable without doc bloat; keeps `applied` claims honest (convention captured; live-scrape stays a caveat, not a false "verified"). |
| 5 | Follow-up code CS | File `project/clickstops/planned/planned_cs36_audit-request-snapshot.md` (LRN-057) with its own GPT-5.5-reviewed pinned plan, as a deliverable of this harvest; leave LRN-057 `open` with a Disposition linking CS36. | RETROSPECTIVES "File a CS" disposition: link the CS, keep the entry `open` until CS36 closes, then flip `applied`. |
| 6 | Scope guard | Edit only: `CONVENTIONS.md` + `REVIEWS.md` (content strictly between the `harness:local-start`/`harness:local-end` markers), `LEARNINGS.md`, `CONTEXT.md`, and the new `planned_cs36` file. No managed-core, no `.github/`, no branch-protection, no code, no CS21 files. | Composed-file discipline + multi-agent boundary (yoga-ae owns CS21; yoga-ae-c5 owns branch-protection/CI/Dependabot). |

## Deliverables

- `CONVENTIONS.md` `conventions.project` extended with the LRN-050/051/058/064 convention bullets + a CR/LF log-sanitization bullet (LRN-059) + the refined CS0542 bullet (LRN-054/LRN-048 co-cite), each LRN-cited, themed into the existing subsections.
- `REVIEWS.md` `reviews.project-gates` extended with the LRN-052/053/055/056/060/061/063 process/CI/merge gates, each LRN-cited, themed into the existing subsections (a new "CI billing / public-tier triage" grouping is acceptable).
- `LEARNINGS.md`: LRN-050..056, LRN-058..064 flipped to `status: applied` with a `**Disposition:**` recording the destination doc block + this CS's PR + landing commit SHA; **LRN-059** and **LRN-062** flipped `applied` citing their already-landed spots (CS34; PR #111); **LRN-057** left `open` with a `**Disposition:**` linking planned CS36; **LRN-035/LRN-040** left `deferred` with a refreshed note (residual owned by yoga-ae-c5; re-evaluate on tier change or `deferred_until`).
- `project/clickstops/planned/planned_cs36_audit-request-snapshot.md` — a fully-formed, GPT-5.5-plan-reviewed, hash-pinned planned CS for the LRN-057 audit-replay-snapshot follow-up.
- `CONTEXT.md` updated to note the completed harvest (LRN-050..064 dispositioned) if the codebase-state narrative warrants it.
- `harness lint` green (`check-learnings`, `check-clickstop`, `check-clickstop-plan-review`, `check-text-encoding`); `harness sync --mode=check` reports no drift (only allowlisted local blocks edited).

## User-approval gates

None — project-local doc content + LEARNINGS dispositions + one new planned CS file. No managed-core, code, workflow, or branch-protection changes.

## Exit criteria

- Every LRN-050..064 is either `applied` with a recorded disposition (LRN-050..056, 058..064) or `open` and linked to a filed planned CS (LRN-057 → CS36).
- LRN-035/LRN-040 remain `deferred` with a refreshed note; branch-protection/workflows untouched.
- `planned_cs36_audit-request-snapshot.md` passes `check-clickstop` + `check-clickstop-plan-review`.
- `harness lint` 0 failed; `harness sync --mode=check` no drift; no managed-core / code / workflow edits.

## Risks + open questions

- **Doc bloat** — keep each consolidated bullet concise and themed; dedup against existing bullets (LRN-054 overlap already identified).
- **Honest `applied` claims** — LRN-051's live-scrape verification cannot be performed in a docs CS; capture the code-verifiable mangling rule and keep the scrape step a documented caveat rather than claiming verification.
- **Multi-agent boundary** — do not edit CS21 files (yoga-ae) or any branch-protection / `.github/workflows/` / Dependabot surface (yoga-ae-c5).
- **Text-encoding gate** — new/edited files must be LF, no BOM.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 5c301b582b81 | 2026-07-04T22:40:00Z | Go | All fact-claims verified; destination mapping + dispositions sound; LRN-051 handled honestly (mangling rule captured, scrape stays a caveat); no blocking issues. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
