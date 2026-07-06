# CS51 — Get-latest-first discipline + harness pin-mismatch guard (doc note + LRN-79)

**Status:** done
**Owner:** omni-ae
**Branch:** cs51/content
**Started:** 2026-07-06
**Closed:** 2026-07-06
**Filed by:** yoga-ae-c3 on 2026-07-05 — surfaced when this session ran `startup --pull-ff-only` as its **first action instead of getting latest first**: the command pulled the already-merged v0.17.0 pin bump (PR #157) mid-run while still invoking the v0.16.0 CLI, colliding a fresh pull with a stale-CLI `sync` and producing a cryptic `Template file not found: .../template/managed/DISPATCH-PREAMBLE.md`. Upstream enforcement tracked as henrik-me/agent-harness#502.
**Depends on:** none

## Goal

Codify and help enforce the **get-latest-first** discipline so the "stale harness CLI collides with a fresh pull" failure cannot recur: (a) a project-local INSTRUCTIONS.md note that you must `git pull` to get latest **before** reading instructions or running any harness command, and derive the pin from `harness.config.json` `version` (never a cached snapshot), with a recognisable symptom-to-remedy; (b) a durable learning (LRN-79) recording the operator-sequencing root cause; and (c) tracking the upstream harness enforcement (startup should pull first, then re-exec/validate at the pulled pin) via agent-harness#502.

## Background

- **Root cause — operator sequencing (did not get latest first).** This session began at `harness.config.json` = **v0.16.0** (HEAD `f01475a`; `.github/copilot-instructions.md` also v0.16.0). Instead of getting latest first, the **first action** was `npx ...#v0.16.0 startup --pull-ff-only` — using the v0.16.0 pin from the session-start snapshot. That command fast-forwarded the working tree onto the already-merged v0.17.0 config (commit `024a6b9`, PR #157) **within the same run**, then ran its bootstrap `sync --mode=check` still under the **v0.16.0** CLI — a fresh pull colliding with a stale-CLI check.
- **Symptom.** v0.17.0's config declares managed target `DISPATCH-PREAMBLE.md` (first added at `024a6b9`); the v0.16.0 package does not ship that template, so sync failed: `Sync error: Template file not found: ...7390b14.../template/managed/DISPATCH-PREAMBLE.md (required for managed target "DISPATCH-PREAMBLE.md")`. Re-running `startup` with `#v0.17.0` passed (23 passed / 0 failed, no drift).
- **The clean sequence** (the repo's own Session-Start rule — INSTRUCTIONS.md: "Pull: `git pull` to fetch the latest state before doing anything else"): `git pull` -> read the pin from the now-current `harness.config.json` `version` (`harness.config.json:4`, the single source of truth from which every doc `#<ver>` literal is sync-rendered) -> invoke the harness at that pin -> green. Never take the pin from a cached session/doc snapshot.
- **Secondary (defense-in-depth) — the harness should enforce this.** Even with the discipline, `startup` should pull **first** then re-exec/validate under the pulled pin, and/or fail fast on a running-CLI vs `config.version` mismatch. Verified against the v0.17.0 source: `config.version` is consumed for doc-ref rendering (`sync.mjs`), major-bump detection (`sync.mjs` `semverMajor`), workflow-pin checks (`check-workflow-pins.mjs`), and review-gate identity checks (`bin/harness.mjs`) — but is **never** compared to the running package version. Tracked upstream as **henrik-me/agent-harness#502**.
- **State-of-the-world probes (2026-07-05):** `gh issue view 502 --repo henrik-me/agent-harness` -> OPEN; `harness.config.json:4` -> `"version": "v0.17.0"`; `gh pr view 157` -> merged, branch `deps/harness-0.17.0` (v0.17.0 was adopted via a `deps/` maintenance PR, not a consumer clickstop).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Primary lesson | **Get latest first — with a plain `git pull`** (not `startup` or any harness CLI, which runs the stale pin), before reading instructions or running any harness command; then derive the pin from `harness.config.json` `version` and invoke the harness at that pin | The incident's practical root cause was sequencing — running a stale-pinned harness CLI (`startup --pull-ff-only`) as the first action, so the pull it performed collided with its own stale-CLI check. Getting latest with a plain `git pull` first avoids running any harness code until the pin is known. |
| 2 | Consumer mitigation surface | A note in the **INSTRUCTIONS.md `instructions.harness` local block** (get-latest-first + pin from `harness.config.json` + symptom-to-remedy) | The managed core is overwritten on sync; the local block is the only consumer-editable, sync-preserved place. |
| 3 | Enforcement (upstream) | The harness should **help enforce** get-latest-first — `startup` pulls first then re-execs/validates at the pulled pin, and/or fails fast on a running-CLI vs `config.version` mismatch — tracked via **agent-harness#502** | Docs alone rely on memory; a CLI guard makes the discipline mechanical. This is harness-runtime (not ours to code — `lib/` is off-limits in a consumer), so it is filed/tracked upstream. |
| 4 | Durable learning | File **LRN-79** (`category: operational`, `status: open`, `source_cs: CS51`) centred on the get-latest-first sequencing pitfall, linked to agent-harness#502 | Knowledge lives in the repo, not agent memory. `category: operational` per the taxonomy — a procedural pitfall / ordering precondition (get latest first), not merely a tool quirk. `source_cs` must be a CS id (schema-required); this CS is the legitimate source. |
| 5 | Scope | **Docs only** — the INSTRUCTIONS.md local-block note + the LRN-79 entry. No code, schema, or workflow changes | The deliverable is a recurrence guard + durable record + upstream tracking, not an implementation. |

## Deliverables

- **INSTRUCTIONS.md** — inside the existing `harness:local-start id=instructions.harness` / `harness:local-end` markers, add a short note (verbatim intent):
  > **Get latest FIRST — with a plain `git pull`.** Fetch the latest state **before** reading these instructions or running any harness command — per Session Start. Do **not** use `startup`/`sync` or any `npx ...agent-harness` command as the get-latest step: running a harness CLI is what collides a fresh pull with a stale pin. After `git pull`, take the harness pin from the now-current **`harness.config.json` `version`** (the single source of truth; every `#<ver>` doc literal is sync-rendered from it) — **never** from a cached session/doc snapshot, which can lag. Invoke `npx ...agent-harness#<ver> ...` with that `<ver>`. If `startup`/`sync` fails right after a pull with `Template file not found: .../template/managed/<X>.md (required for managed target "<X>")`, your CLI is **older** than the just-pulled pin — re-run with the pinned version. (Upstream enforcement tracked: agent-harness#502.)
- **LEARNINGS.md** — a new `## Open` entry **LRN-79**:
  - Frontmatter: `id: LRN-79`, `date: 2026-07-05`, `category: operational`, `source_cs: CS51`, `status: open`, `tags: [get-latest-first, harness, version-pin, startup, sequencing]`, `claim_area: orchestrator-loop`.
  - **Problem:** Running the harness CLI (`startup --pull-ff-only`) as the **first action instead of getting latest first** pulled a newer pin (v0.17.0) mid-command while still executing the older CLI (v0.16.0) from a stale snapshot — colliding a fresh pull with a stale-CLI `sync` and yielding a cryptic `Template file not found` for a managed target that exists only in the newer version.
  - **Finding:** Get latest **first** (plain `git pull`), **then** read the pin from `harness.config.json` `version` (the source of truth), **then** invoke the harness at that pin — never trust a version literal from a cached session snapshot. The harness should also enforce this (startup pull-first + re-exec/validate at the pulled pin), tracked via agent-harness#502.
  - **Evidence:** session-start config v0.16.0 (`f01475a`); first action `startup --pull-ff-only` FF -> v0.17.0 (`024a6b9` / PR #157, adds `DISPATCH-PREAMBLE.md`) still under the v0.16.0 CLI -> template-not-found; re-run at `#v0.17.0` -> 23/0. Upstream agent-harness#502.
  - **Disposition:** `open` — the INSTRUCTIONS.md note (this CS) is the consumer mitigation; the upstream enforcement is tracked via #502. Flip to `applied` at this CS's close-out (record the commit/PR).

## User-approval gates

- None. Docs-only recurrence guard + learning. (An in-repo harness runtime change, if ever wanted, would be a separate CS and is out of scope — the fix belongs upstream per Decision #3.)

## Exit criteria

- INSTRUCTIONS.md `instructions.harness` local block carries the **get-latest-first** note (pull first; derive the pin from `harness.config.json` `version`; symptom-to-remedy); LRN-79 (`category: operational`) is present in LEARNINGS.md (`source_cs: CS51`, cross-linking agent-harness#502) — filed `open` during implementation and flipped to `applied` at this CS's close-out (per the harvest convention that a learning tracked by its own CS flips to `applied` at close-out, recording where the note landed); `harness lint` green (clickstop, learnings-schema, text-encoding LF/BOM, xref durability). No code, schema, or workflow changes.

## Risks + open questions

- **Low risk** — docs + one learning; no runtime surface.
- **Docs rely on discipline.** The INSTRUCTIONS.md note only helps an operator who reads it; the mechanical guarantee is the upstream enforcement (agent-harness#502). Until that ships, the note + LRN-79 are the mitigation.
- **Local-block boundary.** Edit strictly between the `instructions.harness` markers — the managed core is overwritten on sync (per the file-class model).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs51-plan-review (rubber-duck) | 73163656e50f | 2026-07-05T16:41:29Z | Go-with-amendments | Verified #502 open, PR157 merged, v0.17.0 pin, DISPATCH-PREAMBLE add, no CLI-vs-config preflight, LRN-79 free/valid; amended Background config.version usage + clarified LRN-79 open->applied. |
| R2 | GPT-5.5 | Claude Opus 4.8 | cs51-plan-review-r2 (rubber-duck) | 0a6b5b49c1f8 | 2026-07-05T17:04:48Z | Go-with-amendments | Reframed to get-latest-first (category operational); verified INSTRUCTIONS Session-Start quote, #502 open, facts; amended Decision 1 + note to require plain git pull first (not startup). |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| INSTRUCTIONS.md `instructions.harness` block: get-latest-first + derive-pin note; de-hardcode `#<ver>` literals | done | omni-ae | agent-id=omni-ae \| role=docs \| report-status=complete \| learnings=0 |
| README.md: de-hardcode pin refs → `harness.config.json` `version` lookup | done | omni-ae | agent-id=omni-ae \| role=docs \| report-status=complete \| learnings=0 |
| ARCHITECTURE.md: de-hardcode decision-log pin ref → `harness.config.json` `version` | done | omni-ae | agent-id=omni-ae \| role=docs \| report-status=complete \| learnings=0 |
| File LRN-091 (get-latest-first + pin-lookup) | done | omni-ae | agent-id=omni-ae \| role=docs \| report-status=complete \| learnings=1 |
| Close-out: docs + restart state | done | omni-ae | WORKBOARD row removed + active→done rename (this PR); CONTEXT.md refreshed |
| Close-out: learnings + follow-ups | done | omni-ae | LRN-091 flipped open→applied (PR #209 / `4de8b09`); no follow-up CSs needed (upstream #502 already tracked) |

## Notes / Learnings

**Implementation deviations from the plan (omni-ae, 2026-07-06):**
- **LRN id:** the plan named `LRN-79`, but `LRN-079` is already taken and the repo standardised on 3-digit zero-padded ids; filed as **LRN-091** (next free id) with `category: operational` per Decision #4.
- **Scope extension (user-approved):** in addition to the INSTRUCTIONS.md `instructions.harness` note, the same de-hardcoding is applied to `README.md` and `ARCHITECTURE.md` — consumer-owned files whose hand-maintained pin literals lag the real pin — replacing each literal with a lookup of `harness.config.json` `version`. Managed/composed-core `#<ver>` literals are **sync-rendered** from that same field (single source of truth) and are left untouched per the file-class rules; making the harness itself pin-agnostic is tracked upstream as agent-harness#502.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | omni-ae |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-06T20:25:00Z
**Outcome:** GO

| Deliverable | Outcome (match \| diverged \| added \| dropped) | Notes |
|---|---|---|
| INSTRUCTIONS.md `instructions.harness` get-latest-first note + symptom remedy | match | Implemented in the local block, derives `<pin>` from `harness.config.json` `version`, warns against stale cached pins, and includes the template-missing symptom remedy. |
| INSTRUCTIONS.md local command literals de-hardcoded to `#<pin>` | match | The local harness examples now use `agent-harness#<pin>` with an explicit lookup from `harness.config.json`. |
| LEARNINGS.md entry planned as LRN-79 | diverged | Implemented as LRN-091 instead of LRN-79; acceptable because LRN-079 is already taken, the repo uses 3-digit IDs, and the deviation is documented in the CS Notes. |
| LRN content/status/cross-linking | match | LRN-091 has `category: operational`, `source_cs: CS51`, `status: open`, relevant tags, and tracks upstream enforcement via agent-harness#502; open→applied at close-out. |
| README.md and ARCHITECTURE.md de-hardcoding | added | User-approved scope extension; consumer-owned docs with hand-maintained pin literals. Managed/composed-core `#<ver>` literals left untouched per file-class rules. |
| No code/schema/workflow changes | match | Merged diff (`5e0e72e..4de8b09`) touches only INSTRUCTIONS.md, README.md, ARCHITECTURE.md, LEARNINGS.md. |

Test-coverage assessment: **sufficient** — docs-only CS, no test surface; `harness lint` 23/0, `sync --mode=check` no drift.

Overall outcome: **GO**.
