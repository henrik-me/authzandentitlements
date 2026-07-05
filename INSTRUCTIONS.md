# INSTRUCTIONS — Orchestrator Workflow

> **File class:** composed — managed core + one project-local block.
> Do **not** edit the managed-core sections directly. Edit only the content
> inside the `instructions.harness` local block (see § Local block at the end of this file).
> All managed-core sections are overwritten on every `harness sync`.

---

## Hard rules (non-negotiable)

These rules are mechanical — automated linters or workflow gates enforce them.
Violating them is treated as a process bug to fix immediately, not a style preference.

### Planning-locality

Strategic planning content (multi-CS arcs, decisions outliving the session)
MUST live in `project/clickstops/{planned,active,done}/**`. Tactical session
state (e.g. `~/.copilot/session-state/<id>/plan.md`) may track only:
(a) which CS this session is currently executing,
(b) ephemeral todos for that one CS.

Banned filename shapes (case-insensitive) anywhere in the repo outside
`project/clickstops/`, `template/`, `node_modules/`, `.git/`, `tests/fixtures/`:
`PLAN.md`, `ROADMAP.md`, `TODO.md`, `NOTES.md`, `STRATEGY.md`.

Enforced by `scripts/check-planning-locality.mjs` (in `harness lint`).
Rationale: session storage is non-durable; any agent restart, model swap,
or handoff must succeed from the repo alone.

### Agent does not file issues in this repository

GitHub issues in this repository are an INBOUND channel — external
contributors and the user open them; the agent READS them as input to file
CSs. The agent NEVER opens issues in this repository itself, even for
follow-ups. If a follow-up is needed, file a planned CS under
`project/clickstops/planned/`. Stand-alone issues from the agent fragment
the canonical arc and create coordination drift.

This rule is doctrine — not mechanically enforceable because the agent runs
under the maintainer's `gh` credentials and is indistinguishable from the
user at the GitHub-API level. Orchestrator self-check + visible code review
is the only feasible enforcement.

### Knowledge lives in the repo, not agent memory

Durable, project-applicable knowledge MUST be recorded in versioned repo docs —
never in an assistant's private "memory" feature. Learnings go in `LEARNINGS.md`
(entry schema + harvest per [RETROSPECTIVES.md](RETROSPECTIVES.md)); doctrine and
process go in this file, `OPERATIONS.md`, `CONVENTIONS.md`, or `REVIEWS.md`. The
orchestrator may run across multiple machines and multiple repo clones, and
per-agent memory is not committed, not shared, and not portable across them — so
memory-only knowledge is invisible to every other clone and to any agent that
restarts from the repo alone. The planning-locality rule makes the same point for
session state: tactical session notes are allowed, but durable or strategic content
must live in the repo, not in non-durable session files — if a fact is worth
remembering, it is worth a commit. Use agent memory only for ephemeral, session-local
scratch.

This rule is doctrine — not mechanically enforceable (the harness cannot inspect an
agent's private memory store). Orchestrator self-check and code review enforce it.

---

## Quick Reference Checklist

Re-read this section after every `git pull`, even if INSTRUCTIONS.md did not change.

### Session Start

- **Pull:** `git pull` to fetch the latest state before doing anything else.
- **Derive your agent ID** per [TRACKING.md § Agent Identification](TRACKING.md#agent-identification):
  format `<machine-short>-ae[-c<N>]`. Override the machine segment via the
  `HARNESS_AGENT_AE_MACHINE` environment variable if needed
  (per Decision #20c).
- **State your identity:** in your **first response** write your derived agent ID and
  "INSTRUCTIONS.md re-read complete @ \<SHA\>". Treat session resume as session start
  for this rule — no exceptions.
- **First-run environment setup (once per fresh clone):** the bootstrap sanity
  check below runs dependency-backed commands, so install dev dependencies
  first. Requires **Node ≥ 20**. Run a one-time `npm ci` from the repo root —
  `node_modules` is gitignored and per-checkout, so a brand-new clone (or a new
  git worktree) starts with none. Triage: if `node --test` floods with
  `ERR_MODULE_NOT_FOUND`, run `npm ci` — `main` is **not** broken. (Use
  `npm install` only when the lockfile is intentionally stale.)
- **Bootstrap sanity check:** before claiming any CS, run `harness startup`
  from the repo root. It mechanizes the sequence below and exits non-zero
  only on a genuinely broken tree (tests / lint / sync drift). The repo's
  invariant is "main is always green" — if `harness startup` reports a
  broken-tree failure, **stop and investigate** before claiming new work.

  ```bash
  npx -y github:henrik-me/agent-harness#v0.17.0 startup --pull-ff-only
  ```

  Once `startup` passes, `harness status` prints a one-screen resume snapshot
  (the active CS, the WORKBOARD Active Work rows, and the in-flight
  `planned`/`active` arc) — read-only and zero-network, so it is safe to run
  any time you need a handoff context.

  The equivalent expanded sequence (kept for triage when the verb itself
  has not yet been installed in a fresh clone):

  ```bash
  git pull --ff-only origin main
  git status --short                                    # expect: clean
  git log -3 --oneline                                  # last 3 commits on main
  git tag --list 'v*' | tail -5                         # latest release tags (if applicable)
  node --test tests/*.test.mjs                          # expect: all pass
  npx -y github:henrik-me/agent-harness#v0.17.0 lint --quiet                     # expect: 0 failed
  npx -y github:henrik-me/agent-harness#v0.17.0 sync --mode=check --cwd .        # expect: "No drift detected"
  git ls-files project/clickstops/{planned,active}/ | sort   # show in-flight clickstop arc; resume rather than restart
  ```

### Filing a CS

- A CS must exist as a `planned` plan before it can be claimed. To create one,
  follow [OPERATIONS.md § Filing a clickstop](OPERATIONS.md#filing-a-clickstop):
  pick a collision-free `CS<NN>`, author the plan from the canonical skeleton,
  get an independent GPT-5.5 plan review, pin the `## Plan review` attestation
  hash, then run `harness lint`. The procedure and skeleton live there — don't
  reverse-engineer the shape from an existing CS file.

### Claiming a CS

- Follow [OPERATIONS.md § Claim](OPERATIONS.md#claim) for the step-by-step procedure.
- **Branch-protection posture:** from the second commit onward, every change —
  including WORKBOARD claim/closeout — goes through a PR. Respect your
  repository's branch-protection ruleset; where protection is unavailable
  (for example, on private free-tier repositories), discipline plus
  independent review enforces the policy. A documented one-time bootstrap
  commit to `main` is the only exception.
- **Pre-claim gate:** before claiming, review `LEARNINGS.md` for stale `open` items
  tagged `process` or `architectural`, or items whose `claim_area` matches the area
  you are about to claim. Disposition all relevant items before proceeding.
  `harness harvest` runs this pre-claim scan — run it before claiming.
  `harness claim CS<NN>` invokes it automatically as part of the
  preflight gate.

### Re-evaluating private-tier disposition

Re-evaluate the private-tier disposition whenever the constraint assumptions change:

- Repo visibility flips (`private` → `public` or `public` → `private`).
- The GitHub plan changes (`Free` → `Pro`, `Pro` → `Team`, etc.).
- The chosen disposition changes (for example, from `discipline-only` to `upgrade-pro`).

Recommended path: re-run `harness init` from the repo root. Init is idempotent for
constraint records: it updates the `constraints` block in `harness.config.json`, rewrites
`.harness-known-constraints.md`, and keeps the `CONTEXT.md` reference under
`## Constraints` to a single line with no duplicates or orphan keys.

Manual path: edit the `constraints` block in `harness.config.json` directly, following the
schema fields:

- `tier`
- `disposition` (only when `tier` is `private-free`)
- `detected_at`
- `owner`
- `repo`

Then update `.harness-known-constraints.md` so the written disposition matches the config.
The schema validator catches shape errors such as an invalid enum value or a `disposition`
key on a non-`private-free` tier.

Override path: pass `--constraint-disposition <value>` to `harness init` to force one of
`discipline-only`, `upgrade-pro`, or `flip-public-when-ready`. The default for
`private-free` is `discipline-only`.

Skip path: pass `--skip-constraint-detection` to `harness init` to avoid GitHub API calls
entirely. Use this in CI or other network-restricted environments; init proceeds without
populating `constraints`.

A re-run touches only the constraint surfaces:

- `harness.config.json` — inserts or updates the `constraints` block.
- `.harness-known-constraints.md` — rewrites the recorded values and disposition guidance.
- `CONTEXT.md` — adds or refreshes one root-relative reference to
  `.harness-known-constraints.md` under `## Constraints`.

A re-run does **not** touch other `harness.config.json` fields (`composed`, scaffolds,
etc.), other root files such as `README.md`, or `.harness-lock.json`. Constraint state does
not flow through `harness sync`. Delete `.harness-known-constraints.md` only when you also
remove the `constraints` block intentionally and no longer want a persisted constraint
record.

### Closing a CS

- **Run the plan-vs-implementation review gate (GPT-5.5)** — see
  [OPERATIONS.md § Plan-vs-implementation review (close-out gate)](OPERATIONS.md#plan-vs-implementation-review-close-out-gate).
  Capture the review in the active CS file's `## Plan-vs-implementation review`
  section before the `active → done` rename. NEEDS-FIX outcome blocks close-out.
- Rename `active_cs<NN>_*.md` → `done_cs<NN>_*.md` and move it to
  `project/clickstops/done/`. Use the directory form if the CS carries artifacts.
- Remove the row from `WORKBOARD.md`.
- Update `CONTEXT.md` if the codebase state changed.
- Ensure the active/done CS file's `## Tasks` table includes explicit
  **Close-out: docs + restart state** and **Close-out: learnings + follow-ups**
  rows. `check-clickstop.mjs` enforces these rows; see
  [OPERATIONS.md § Claim](OPERATIONS.md#claim) for the exact scope.
- File any new learnings in `LEARNINGS.md` (see [RETROSPECTIVES.md](RETROSPECTIVES.md)
  for entry shape and categories) and planned follow-up CSs for unresolved
  issues.

### Cutting a release

- A release is its own CS. Follow [OPERATIONS.md § Release process](OPERATIONS.md#release-process)
  for the ordered cut (pre-release audit, state-of-the-world probes
  per REVIEWS.md § 2.6c F6, `npm version` bump, CHANGELOG `[Unreleased]` → `[x.y.z]`
  promotion, README pin sweep, plan-vs-impl + Phase-2 review, Copilot engage, CI,
  squash-merge, post-merge `git tag` + `git push origin v<x.y.z>`, the
  `harness release` verb creates the draft Release (`gh release edit --draft=false`
  to publish), consumer notification via `harness cross-repo open-issue`) and the
  solo-orchestrator content-PR admin-merge subsection.

### Every CS

- **Implementation models:**

  | Role | Model |
  |---|---|
  | Orchestrator | Claude Opus 4.8 (fallback Claude Opus 4.7) |
  | Coding, unit-test & implementation sub-tasks (code/docs/config) | Claude Opus 4.8 (fallback Claude Opus 4.7) |
  | Local review (primary) | GPT-5.5 |
  | Local review (fallback, non-high-risk) | Claude Sonnet 4.6 (independence invariant) |

- **Local review is mandatory** before opening any PR and before committing any
  template change. Use GPT-5.5 rubber-duck. Record the model used and timestamp in
  the PR body. Fallback rules and independence invariant are in
  [REVIEWS.md](REVIEWS.md).
  - **Rubber-duck scope — fact-claim verification (REVIEWS.md § 2.6a).** A "Go"
    verdict is only valid when the reviewer has verified that every factual
    claim in the diff matches the cited shipped surface. For docs and prose
    PRs this is the dominant failure mode: a reviewer who only reads the diff
    will miss CLI flags that don't exist, file paths that don't exist, doctrine
    that's been paraphrased into a different requirement-level, and LRN/CS
    references whose summarised scope overstates the source. Reviewer prompts
    MUST explicitly require: (a) every `--flag` mentioned exists in
    `bin/harness.mjs` help text, library code, or pass-through
    `scripts/*.mjs` (e.g. `harness review-output` forwards to
    `scripts/check-review-output.mjs`); (b) every file path
    mentioned exists in the tree; (c) every doctrine claim (`required`,
    `enforces`, `mandatory`, `recommended`, `optional`) matches the cited
    source's wording verbatim or via a documented synonym; (d) every LRN/CS scope
    summary respects the source entry's Problem/Finding scope (no
    generalisation beyond what the source asserts); (e) cross-doc claims
    (CHANGELOG vs OPERATIONS vs README vs LRN) are mutually consistent.
- **Branch naming:** `cs<NN>/<slug>` for CS work; `workboard/cs<NN>-claim`,
  `workboard/cs<NN>-close`, etc. for WORKBOARD-only PRs; `deps/<pkg>-<ver>` for
  dependency/maintenance (Dependabot adoption) PRs (see
  [OPERATIONS.md § Dependency-bump adoption](OPERATIONS.md#dependency-bump-adoption)).
- **Commit trailers:** every commit must include
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`.
- **Mid-CS sync prohibition:** do not run `harness sync` mid-clickstop unless
  resolving a harness blocker. Harness updates land in their own dedicated CS.
- **Sub-agent file ownership:** when dispatching parallel sub-agents, each sub-agent
  owns exactly the files listed in its briefing. Overlapping write scope causes silent
  file races. Enforce non-overlapping
  ownership at dispatch time, not after.
- **No-commit preflight:** every sub-agent briefing must begin with a hard no-commit
  preflight. Require the sub-agent to record `git --no-pager log
  --oneline -1` in its final report and confirm "No commit was created."
- **Test minimums:** brief sub-agents with minimum test counts, never exact counts.
  Over-delivery on tests is a positive signal, not scope creep.
- **Schema-first:** any sub-agent writing config-reading code must read
  `schemas/*.schema.json` before authoring any field access. Field name guessing
  causes silent integration failures.
- **Report shape:** every sub-agent must respond with the canonical report shape from
  [OPERATIONS.md § Sub-agent report shape](OPERATIONS.md#sub-agent-report-shape-mandatory).
  Reports missing this structure are rejected and the sub-agent is re-dispatched.
- **Sub-agent briefing preamble**: every sub-agent dispatch MUST paste the canonical preamble —
  emitted verbatim by `npx -y github:henrik-me/agent-harness#v0.17.0 dispatch` from the harness-owned `DISPATCH-PREAMBLE.md`
  (see [OPERATIONS.md § Mandatory briefing preamble](OPERATIONS.md#mandatory-briefing-preamble-copy-verbatim-into-every-dispatch)) —
  verbatim into the prompt. Verbatim paste (not just reference) is the discipline that prevents
  process steps from being forgotten.

---

## Per-CS Loop

Complete these steps in order for every clickstop. Do not skip or reorder.

1. **Pre-claim — learnings gate.** Run `harness harvest` or manually review
   `LEARNINGS.md` for stale `open` items tagged `process` or `architectural`, and any
   items whose `claim_area` matches the area you are claiming. Disposition before
   proceeding. See [Harvest Cadence](#harvest-cadence) for disposition options.

2. **Claim.** Run `harness claim CS<NN>` to preflight, run the
   harvest gate, and render the claim plan; re-run with `--apply` to cut
   the `cs<NN>/claim` branch + `git mv` planned→active + edit `WORKBOARD.md`.
   The verb NEVER commits — you own the commit message and PR. Commit via a
   `workboard/cs<NN>-claim` PR; where your branch-protection ruleset allows it,
   eligible workboard-only PRs are bot-approved and auto-merged after the workflow
   validation gate passes. WORKBOARD task states:
   - `planned` — filed, not yet started
   - `active` — claimed and in flight (you own it; no other orchestrator may claim it)
   - `blocked` — cannot proceed; document the blocker and set a `reclaimable` threshold
     (default 7 days) in the WORKBOARD row so another orchestrator can pick it up
   - `paused` — intentionally paused; same reclaimable convention as `blocked`
   - `done` — merged to `main`; remove the row at step 11

3. **Branch.** Create `cs<NN>/<slug>` from `main`:
   `git checkout -b cs<NN>/<slug> origin/main`.

4. **Plan-internal.** Identify parallelisable sub-tasks. Record each in the CS file's
   `## Tasks` table with the canonical Notes format before dispatching any sub-agent:
   `agent-id=<id> | role=<role> | report-status=pending | learnings=0`.
   Use `harness dispatch` to emit the canonical sub-agent briefing
   preamble verbatim — never re-derive it from memory.
   Follow [OPERATIONS.md § Sub-agent dispatch](OPERATIONS.md#sub-agent-dispatch) for
   briefing structure, file-ownership declarations, no-commit preflight, and the
   mandatory report shape. Brief sub-agents with test **minimums**, never exact counts.
   Every briefing must include:
   - Hard no-commit preflight in the first paragraph.
   - Explicit file ownership list — exactly the files this sub-agent may write.
   - Required reading list — active CS file, INSTRUCTIONS.md, CONVENTIONS.md, and any
     relevant schemas.
   - Decision authority and escalation path — what the sub-agent may decide alone vs.
     what must come back to the orchestrator.
   - Self-check requirements — tests, linters, `git status --short`, SHA verification.

5. **Implement.** All code, template, and doc changes land on the CS branch. Sub-agents
   may run in parallel as long as they own disjoint file sets. After each parallel
   wave, verify disk state: `git status --short` plus per-file size check.
   If a sub-agent's disk state contradicts its report, re-dispatch with the
   lost-work briefing. Do not declare a parallel wave complete until the disk state
   matches every sub-agent's reported deliverables.

6. **Local review.** GPT-5.5 rubber-duck mandatory. Record model + timestamp + fallback
   reason (if any) and the list of CS implementers in the PR body.

7. **Open PR** using the pull request template. Ensure the title is `<type>(scope): ...`
   and the body includes the local-review record.

8. **CI checks** must all pass before requesting review. Fix failures on the branch;
   never merge a red CI.

9. **Review.** Obtain the mandatory rubber-duck review and any approving reviews
   your branch-protection ruleset requires before merge. Engage Copilot review per
   your project's review policy (see [REVIEWS.md](REVIEWS.md)).

10. **Resolve all threads**, then **squash-merge**. Never merge with unresolved
    suggestions or blocking review threads.

11. **Plan-vs-implementation review gate (GPT-5.5).** Run before the close-out PR.
    See [OPERATIONS.md § Plan-vs-implementation review (close-out gate)](OPERATIONS.md#plan-vs-implementation-review-close-out-gate).
    Record the review in the active CS file's `## Plan-vs-implementation review`
    section. NEEDS-FIX outcome blocks close-out.

12. **Post-merge closeout.** Run `harness close-out CS<NN>`: Phase 1
    preflights (correct branch, clean worktree, populated `## Plan-vs-implementation
    review` section with **Outcome:** GO). Phase 2 (`--apply`) renames
    `active_cs<NN>_*.md` → `done_cs<NN>_*.md`, removes the WORKBOARD row,
    and refuses to mark the close-out PR-ready until `CONTEXT.md` has been
    updated (freshness gate). Then update `LEARNINGS.md` with new findings
    before opening the close-out PR. The verb NEVER commits — you own the
    commit message and PR.

13. **Harvest** if the cadence triggers — see [Harvest Cadence](#harvest-cadence).

### Harvest Cadence

Two triggers drive the harvest. Both use `harness harvest`, which scans
`LEARNINGS.md` for `open` entries and prompts you to disposition each one.
Full procedure and disposition states are in [RETROSPECTIVES.md](RETROSPECTIVES.md).

#### Weekly

Run `harness harvest` at the start of your work week (Monday morning or equivalent).

For each `open` learning, choose one disposition:

- **Apply upstream.** Edit the relevant process doc (INSTRUCTIONS, CONVENTIONS,
  OPERATIONS, REVIEWS, RETROSPECTIVES, ARCHITECTURE, or TRACKING) to incorporate the
  finding. Mark the LEARNINGS.md entry `applied` with the commit SHA in its YAML
  frontmatter.
- **File a CS.** For tooling or automation gaps that require code changes, create a
  `planned_cs<NN>_<slug>.md` and link it from the learning entry. Leave the entry
  `open` until the CS closes.
- **Obsolete.** Mark `obsolete` with a short reason if the learning is no longer
  relevant (e.g., the problem it describes was eliminated by a subsequent change).
- **Defer.** Leave `open` with an explicit reason and a `deferred_until` date. The
  CLI prevents indefinite re-deferral: after the second consecutive defer, the entry
  is dropped from before-claim prompts and surfaces only at weekly harvest.

#### Before-Claim (bounded)

Run `harness harvest` before claiming a CS (`harness claim CS<NN>` runs it
automatically as part of the preflight gate). **Silent if no stale
relevant learning exists.** Fires only when at least one of the following is
true:

- a stale `open` learning is tagged `process` or `architectural`;
- a stale `open` learning has a `claim_area` matching the area being claimed.

Output is batched — for example: "3 stale learnings; choose apply / defer / obsolete /
skip-for-this-CS each." You are not required to fully resolve learnings that lack a
clear disposition yet — file them as `open` with a reason and let the weekly harvest
handle full resolution. The goal of this gate is to prevent known process gaps from
being silently carried into new work.

---

## When to Add X

Use this section as a decision tree when you are unsure whether a change warrants a
new file, a new script, a new schema, or a new scaffold. Scaffold templates are
referenced by expected name below; where a referenced scaffold does not yet exist,
note its expected name in your briefing.

### When to Add a Script

**Add** a new file under `scripts/check-*.mjs` (linter) or `scripts/*.mjs` (utility)
when:

- The logic runs at authoring time, not consumer runtime (e.g., a linter, a validator,
  a report generator).
- The logic is not already covered by a function in `lib/`.
- The script accepts an explicit `--file <path>` flag; never infer the target path from
  `import.meta.url`.

Linter scripts additionally must:
- Exit 0 for valid input, 1 for validation errors, 2 for bad CLI usage.
- Print `ERROR:` / `WARNING:` prefixed lines and end with `✅ Linter passed` or
  `❌ Linter FAILED`.
- Be registered in the `harness lint` aggregator so CI picks them up.
- Use `requireValue(args, i, flagName)` for all flag-value parsing to prevent silent
  misparsing when a flag is the last token with no value.

Use scaffold: `scaffolds/new-script.md`.

**Do NOT add** a script if the logic belongs at consumer runtime — that belongs in
`bin/harness.mjs` or a subcommand module.

### When to Add a CLI Subcommand

**Add** a new subcommand to `bin/harness.mjs` (or a dedicated module it delegates to)
when:

- The feature is part of the harness CLI surface that consumer projects invoke at
  runtime or in CI (e.g., `harness sync`, `harness lint`, `harness harvest`).
- It is NOT a one-off authoring script — CLI subcommands are versioned and appear in
  `harness --help`.

CLI subcommand requirements:
- Forward `--help` to print usage and exit 0.
- Accept `--config <path>` and resolve it once into a single variable used for all
  config reads and for threading to delegated subcommands.
- Use `requireValue(args, i, flagName)` for all flag-value parsing.
- Use `spawnSync` with `shell: true` on Windows-compatible paths.

Use scaffold: `scaffolds/new-subcommand.md`.

**Do NOT add** a CLI subcommand for logic that only runs during authoring-time
validation — use a `scripts/check-*.mjs` script instead.

### When to Add a Library Module

**Add** a new module under `lib/` when:

- The same logic is called by two or more scripts, commands, or test suites.
- The module has a stable public API that should be independently testable.
- The module has zero runtime dependencies beyond Node.js built-ins (runtime deps
  require explicit approval and a separate CS).

Use scaffold: `scaffolds/new-library-module.md`.

**Do NOT add** a library module for one-off utilities used by only a single script —
keep them inline.

### When to Add a Template File

**Add** a new template under `template/managed/` or `template/composed/` when:

- The file is delivered to consumer repos via `harness sync`.
- You have explicitly classified it as managed or composed:
  - **Managed** (`template/managed/`): harness overwrites the file in full on every
    sync; the consumer must not edit it. No marker blocks. Use for policy files whose
    content is entirely harness-owned.
  - **Composed** (`template/composed/`): harness manages a core block; the consumer
    may add local content via `<​!-- harness:local-start id=<block-id> -->` /
    `<​!-- harness:local-end id=<block-id> -->` markers (block ID must be allowlisted
    in `harness.config.json` `composed.overrides[<file>].local_blocks`).
    Use for files that need a
    harness-provided core plus project-specific extensions.

**Add** a file to `template/seeded/` when:
- The file is copied to consumer repos on initial setup only and is **never**
  overwritten by subsequent syncs. The consumer owns it completely after seeding.

Use scaffold: `scaffolds/new-template.md`.

**Do NOT add** a template for harness-internal files that never leave this repo — put
those in `lib/`, `bin/`, or `scripts/` as appropriate.

### When to Add a Linter

**Add** a linter under `scripts/check-*.mjs` when:

- A structural invariant or schema contract needs to be verified on every PR.
- The invariant is not already covered by an existing linter.
- The linter can be expressed as a standalone script with a `--file <path>` argument
  (so `harness lint` can thread it explicitly).

Required linter interface (enforced by `harness lint`):
- Accepts `--file <path>` and `--quiet` flags.
- Exit codes: 0 = valid, 1 = errors found, 2 = bad usage.
- Summary line at the end: `<basename>: N errors, M warnings`.
- Final line: `✅ Linter passed` or `❌ Linter FAILED`.

Use scaffold: `scaffolds/new-linter.md`.

### When to Add a Schema

**Add** a schema under `schemas/*.schema.json` when:

- A structured file format (config, lock, learning entry, CS file, etc.) is read by
  two or more scripts and needs a shared, validated contract.
- You need `check-*` linters or sub-agent briefings to cross-reference field names
  (never guess field names; always derive them from the schema).

Use scaffold: `scaffolds/new-schema.md`.

**Do NOT add** a schema for ad-hoc internal structures used by only one script — use
JSDoc `@typedef` annotations instead.

### When to Add a Test

**Add** tests in `tests/*.test.mjs` (Node built-in test runner) when:

- A new library module is added — test its public API directly.
- A new linter is added — add fixture-based tests (valid fixtures → exit 0; invalid
  fixtures → exit 1 with expected error messages).
- A regression is found — add a test that reproduces the failure before fixing it.

Test hygiene rules:
- Tests must be runnable with `node --test tests/*.test.mjs`.
- No third-party test frameworks. Use `node:test` and `node:assert` only.
- Fixture files live in `tests/fixtures/` and are named after the test file.
- Tests must not write transient files under the repository root; use the OS temp directory (e.g. `os.tmpdir()`) for scratch.
- Brief sub-agents with minimum counts; over-delivery is encouraged.

Use scaffold: `scaffolds/new-test.md`.

### When to Add a Scaffold

**Add** a scaffold under `scaffolds/` when:

- A new category of deliverable will be created repeatedly across multiple CSs.
- The pattern is stable enough to be templated (used at least twice, shape unlikely
  to change significantly).

Scaffolds capture repeatable deliverable shapes; add one only when the pattern is
stable. Where a referenced scaffold does not yet exist, note its expected name in
briefings and back-fill it later.

### When to File a CS vs. Inline a Fix

**File a new CS** when:

- The change is non-trivial (estimated > 2 hours of orchestrator + sub-agent work).
- The change crosses multiple files or requires a dedicated review round.
- The change is a tooling/automation gap surfaced by a harvest learning.
- The change modifies a managed or composed template file (template changes always
  land in their own CS, never piggy-backed onto implementation work).

**Inline** a fix on the current CS when:

- The fix is a direct consequence of a failing self-check or CI error on this branch.
- The fix touches only files already owned by the current CS.
- The fix is small enough to review as part of the current CS's PR without inflating
  its scope.

When in doubt, file a CS. Small, focused CSs are cheaper than scope-inflated PRs.

---

## Pointers

| Topic | Where to look |
|---|---|
| Code, test, git, and documentation conventions | [CONVENTIONS.md](CONVENTIONS.md) |
| Day-to-day procedures (claim, dispatch, sync, harvest) | [OPERATIONS.md](OPERATIONS.md) |
| Review loop (primary model, fallback policy, independence invariant) | [REVIEWS.md](REVIEWS.md) |
| Clickstop lifecycle + agent identification | [TRACKING.md](TRACKING.md) |
| Definition of "learning", categories, harvest procedure | [RETROSPECTIVES.md](RETROSPECTIVES.md) |
| Live coordination (who owns what, blocked tasks) | [WORKBOARD.md](WORKBOARD.md) |
| Current codebase state (last CS closed, key paths) | [CONTEXT.md](CONTEXT.md) |
| Architecture (design decisions, module map) | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Accumulated project knowledge (LRN entries) | [LEARNINGS.md](LEARNINGS.md) |
| The clickstop plans that drive this project | [project/clickstops/](project/clickstops/) |

### authzandentitlements — Project-Specific Pointers

The following pointers are specific to this deployment of the harness in
**authzandentitlements** (repo slug: `henrik-me/authzandentitlements`). They are filled in by `harness sync`
from `harness.config.json` at sync time.

- Agent ID suffix for this repo: `ae`
- Agent ID env-var override: `HARNESS_AGENT_AE_MACHINE`
- Project deploy procedures: see `OPERATIONS.md` local block `id=operations.project-deploy`
- Project review gates: see `REVIEWS.md` local block `id=reviews.project-gates`
- Project conventions: see `CONVENTIONS.md` local block `id=conventions.project`

---

## Local block

The section below is project-local and is preserved across `harness sync`.
Edit only the content **between** the markers. The markers and all content
above are managed by the harness and will be overwritten on the next
`harness sync`. The block ID `instructions.harness` must be listed in
`harness.config.json` under `composed.overrides["INSTRUCTIONS.md"].local_blocks`.

<!-- harness:local-start id=instructions.harness -->
**This repo has no local Node dependencies.** The agent-harness runs via
`npx -y github:henrik-me/agent-harness#v0.16.0 <cmd>` — there is **no `package.json`**,
so **skip the Session-Start `npm ci` step** (it does not apply here; `node --test`
reports 0/0, which is expected).

**Machine prerequisites** (install once per machine): **.NET 10 SDK**, **Node >= 20**,
**Docker** (running), the **`aspire` CLI** (`dotnet tool install -g Aspire.Cli`), and
**`gh`** (GitHub CLI).

**New-agent start here (from a fresh clone):**
1. `git pull`; derive your agent ID (`<machine>-ae`).
2. Run `npx -y github:henrik-me/agent-harness#v0.16.0 startup` (session sanity + queue).
3. Read [CONTEXT.md](CONTEXT.md) — the **CS dependency map + parallelization waves** and current state.
4. Read [ARCHITECTURE.md](ARCHITECTURE.md) — goals, the four-layer authz model, decisions, and the phase roadmap.
5. Claim the first ready clickstop — **CS01 (aspire-foundations)** — and follow the Per-CS loop.

The full plan is repo-resident: strategy/arc in `project/clickstops/**`, the dependency
map + waves in CONTEXT.md, and architecture/goals/roadmap in ARCHITECTURE.md. Session-state
`plan.md` is scratch only (per Planning-locality).
<!-- harness:local-end id=instructions.harness -->
