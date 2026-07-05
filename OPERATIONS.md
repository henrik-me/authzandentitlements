# OPERATIONS

> **File class:** composed — managed core + one project-local block.
> Do **not** edit the managed-core sections directly. Edit only the content
> inside the `operations.project-deploy` local block (see § Local block at the end of this file).
> All managed-core sections are overwritten on every `harness sync`.

Day-to-day procedures for filing, claiming, dispatching, syncing, and
harvesting with the agent harness. This is the canonical operational
reference for all harness-enabled projects.

---

## Filing a clickstop

A clickstop (CS) is the unit of planned work. **File a CS** when the work
involves design decisions, multiple files, or a doctrine/process change —
anything that benefits from a written plan and a plan review. Trivial
dependency bumps, pure `WORKBOARD.md` edits, and one-line doc fixes do not
need a CS (use the workboard-only / maintenance PR path instead). Filing
creates the `planned` plan only; moving it into flight (the
`planned → active` rename, the WORKBOARD row, the branch) is the separate
claim step in § Claim. Follow the steps below rather than reverse-engineering
the shape from an existing CS file.

### Steps

1. **Pick a collision-free id.** Use the next unused `CS<NN>` above every id
   already present under `project/clickstops/{planned,active,done}/`. A
   trailing letter (`CS63a`) marks a sub-task within one arc; when sibling
   orchestrators are active, leave a margin above their in-flight arc.
2. **Create** `project/clickstops/planned/planned_cs<NN>_<slug>.md` with LF
   line endings and no BOM (the text-encoding gate rejects CRLF/BOM).
3. **Author the plan** from the skeleton below.
4. **Get an independent plan review.** Dispatch the `## Decisions` +
   `## Deliverables` to the primary reviewer model (GPT-5.5; see
   [REVIEWS.md](REVIEWS.md)), which MUST differ from every `Plan author
   model(s)`. Iterate until the verdict is `Go` or `Go-with-amendments`.
5. **Pin the attestation.** Compute the 12-char hash of the current
   Decisions+Deliverables with `harness plan-review-hash <file>` and record
   it in a `## Plan review` row. The latest row's hash MUST equal the
   current Decisions+Deliverables hash, and its verdict MUST be `Go` or
   `Go-with-amendments`.
6. **Validate** with `harness lint` — it runs `check-clickstop` (structure),
   `check-clickstop-plan-review` (attestation), and `check-text-encoding`
   (LF/BOM).
7. **Open a content PR** adding the file, with the `## Model audit` +
   `## Review log` review evidence ([REVIEWS.md](REVIEWS.md) § 2.8). Filing
   does not claim the CS.

### Required structure

Mechanically enforced by `scripts/check-clickstop.mjs` and
`scripts/check-clickstop-plan-review.mjs`:

- **Header fields (all required):** `**Status:** planned`, `**Owner:**`,
  `**Branch:**`, `**Started:**`, `**Closed:**`, `**Depends on:**`. `Status`
  must read `planned` while the file lives in `planned/`. (Filing agents also
  add a `**Filed by:**` line by convention — it carries useful provenance but
  is not one of the fields `scripts/check-clickstop.mjs` enforces.)
- **`## Plan review`** — present, with the 8-column table and at least one
  row: `Round | Reviewer model | Plan author model(s) | Reviewer agent |
  Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200
  chars)`. Reviewer model ∉ author models; ISO-8601 UTC timestamps; the
  latest hash/verdict stay fresh per step 5.
- **`## Plan-vs-implementation review`** — include the placeholder now; it is
  only *enforced* once the file reaches `active/` or `done/` at close-out.

The remaining sections are canonical convention — but `## Decisions` and
`## Deliverables` are required in practice because the plan-review hash is
computed over their bodies.

### Skeleton

```markdown
# CS<NN> — <title>

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** <who filed it, when, and the surfacing context>
**Depends on:** <none | CS refs>

## Goal
## Background
## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|

## Deliverables
## User-approval gates
## Exit criteria
## Risks + open questions
## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings
## Plan-vs-implementation review

> _(filled at close-out per the gate)_
```

---

## Claim

The claim workflow moves a planned Clickstop (CS) into flight and establishes
a content PR on the repo. **One CS active per orchestrator** — the WORKBOARD's
Active Work table is the live lock, keyed on the Owner. An orchestrator may not
claim a second CS while it already owns an Active row, but different
orchestrators run concurrently and may each hold their own Active CS.

### Three-PR shape

Every CS produces exactly three PRs in sequence:

1. **Workboard-claim PR** — branch `cs<NN>/claim`; touches only
   `WORKBOARD.md` and the clickstop file rename (`planned → active`).
   Label: `workboard-only`. *(CS01–CS14: user-reviewed small PR.
   Public protected phase: bot auto-approved via Decision #23 when the PR
   passes the workboard-only validation gate.)*

2. **Content PR** — branch `cs<NN>/content`; all implementation work lives
   here. Standard review loop (GPT-5.5 + user). Squash-merge only.

3. **Close-out PR** — branch `cs<NN>/close-out`; touches only
   `WORKBOARD.md` (Active Work row removed for this CS), the clickstop
   rename (`active → done`), and any close-out updates to `CONTEXT.md` /
   `LEARNINGS.md`. The `done/` directory is the historical record;
   WORKBOARD never carries a "recently completed" log (LRN-102). Label:
   `workboard-only`. Same auto-merge rules as the claim PR. **Must be
   preceded by the plan-vs-implementation review gate (see
   [§ Plan-vs-implementation review (close-out gate)](#plan-vs-implementation-review-close-out-gate)).**

**Auto-merge branch patterns.** A `workboard-only`-labelled PR auto-merges only
when its branch matches one of `cs<NN>/(claim|close|close-out)`,
`workboard/cs<NN>-(claim|close|close-out)`, `docs/file-planned-cs<NN>(-<slug>)?`
(the planned-CS filing PR), or `workboard/maint-[A-Za-z0-9][A-Za-z0-9._-]*` (an
ad-hoc workboard-allowlist maintenance PR — see below), where `<NN>` is the CS
number plus an optional lowercase suffix letter (e.g. `cs64b`). A workboard-scoped
PR whose branch does **not** match — notably a `workboard/cs<NN>-pause` PR —
should still carry the `workboard-only` label (so the review-evidence gates skip),
but its `validate-and-approve` job then **fails** the branch-name check, so an
admin must squash-merge it (`gh pr merge <n> --admin --squash`). Match an eligible
pattern exactly (the filing branch is `docs/file-planned-cs<NN>…`, not
`docs/file-cs<NN>…`) to keep auto-merge.

**Maintenance-PR branch pattern.** The `workboard/maint-<name>` pattern
(`workboard/maint-[A-Za-z0-9][A-Za-z0-9._-]*`) covers ad-hoc workboard-allowlist
**maintenance** PRs — a standalone `CONTEXT.md` or `LEARNINGS.md` correction, say
— that are not claim/close/close-out or CS-filing PRs and so would otherwise fail
the branch-name check. It is deliberately bounded: anchored and slash-free (the
character class after `maint-` excludes `/`, so it cannot broaden into a
`workboard/*` wildcard), and it requires at least one name character after
`maint-`. The `is_allowed()` path allowlist still constrains **which** files such
a PR may change, so a `workboard/maint-context-typo` PR that touches only
allowlisted paths auto-merges like a claim/close PR while the auto-approve surface
stays tightly bounded.

Every active/done CS file must include explicit `## Tasks` rows for:

- **Close-out: docs + restart state** — update `WORKBOARD.md`, `CONTEXT.md`,
  managed/composed process templates and rendered roots, plus any
  relevant feature docs so a fresh agent can restart from the actual state.
- **Close-out: learnings + follow-ups** — file or disposition learnings in
  `LEARNINGS.md` and create planned follow-up CSs for unresolved issues.

`check-clickstop.mjs` enforces these rows for active CS files and for done CS
files closed on or after CS15a's close-out enforcement date.

**Directory-form CS close-out — `git mv` the whole directory (CS70 / LRN-164).** When a CS
plan lives in **directory form** (`<state>/<state>_cs<NN>_<slug>/<state>_cs<NN>_<slug>.md` —
the plan file sits inside a per-CS directory that may hold sibling artifacts), the
`active → done` rename in the close-out PR MUST be a **directory-level** rename of the entire
CS directory, never a per-file rename of just the plan file:

```bash
git mv project/clickstops/active/active_cs<NN>_<slug>/ \
        project/clickstops/done/done_cs<NN>_<slug>/
```

A per-file rename silently drops every sibling file in the directory — the failure that lost
`sub-invaders-bootstrap-summary.md` during the CS16 close-out
([agent-harness#290](https://github.com/henrik-me/agent-harness/issues/290)). This is
mechanically guarded: `check-clickstop.mjs` fails if any file ever seen under
`active_cs<NN>_<slug>/` is absent under `done_cs<NN>_<slug>/` once the CS reaches `done/`,
unless its basename is declared in an optional `.harness-closeout-allow-drop` file inside the
`done_cs<NN>_<slug>/` directory (one basename per line; `#` comments and blank lines ignored).

### Claim steps

**What:** move a planned CS into flight — run the preflight + harvest gate, cut
the `cs<NN>/claim` branch, `git mv` the CS file `planned → active`, and add the
WORKBOARD Active Work row. **When:** at the start of a CS, from an up-to-date
`main`, before any implementation work. **How:** run `npx -y github:henrik-me/agent-harness#v0.17.0 claim CS<NN>`
(dry-run by default; `--apply` executes the branch cut + rename + WORKBOARD
edit). It NEVER commits and NEVER pushes — you own the commit message
(`Claim CS<NN>` with the `Co-authored-by: Copilot` trailer) and the
`workboard-only`-labelled claim PR (user reviews; squash-merge — see
[§ Three-PR shape](#three-pr-shape)). The full preflights and executable steps
live in `npx -y github:henrik-me/agent-harness#v0.17.0 claim --help`; use the directory form for
artifact-bearing CSs (see
[TRACKING.md § Clickstop lifecycle](TRACKING.md#clickstop-lifecycle)).

**Opening the claim PR — label at creation (CS71).** Open the workboard-only
claim PR with `--label workboard-only` supplied in the `gh pr create` command
itself: `gh pr create --base main --label workboard-only --title "..." --body-file ...`.
Do **not** add the label post-hoc via `gh pr edit --add-label` — that fires a
separate `labeled` event and creates a race (PR #305 green vs PR #306 red on
the same command; see CS71 Background). Since CS71, evidence gates are also
**path-derived** (a correctly-shaped workboard PR is green without the label),
but the label is **still required** for `workboard-auto-approve.yml` to auto-merge.

### Pre-claim harvest gate (CS04+)

Run `harness harvest` before claiming. `harness claim CS<NN>` (CS64) invokes
it automatically as part of the preflight gate. It surfaces stale `open`
learnings tagged `process` or `architectural`, or learnings tagged with the
`claim_area` metadata for the current CS area. Resolve stale learnings
before the workboard-claim PR lands.

### Pre-claim planning-locality self-check (CS35 C35-11)

Before claiming any CS, verify no strategic planning content lives outside
the canonical `project/clickstops/{planned,active,done}/**` arc:

1. Run `npx -y github:henrik-me/agent-harness#v0.17.0 lint` — must exit 0 (it includes the
   planning-locality check).
2. If the orchestrator's session-state plan file (`~/.copilot/session-state/<id>/plan.md`)
   contains anything beyond (a) which CS this session is currently executing
   and (b) ephemeral todos for that one CS, externalize the strategic content
   into `project/clickstops/planned/planned_csNN_<slug>.md` BEFORE claiming.
   Session storage is non-durable; any agent restart, model swap, or handoff
   must succeed from the repo alone (per Decision C35-11).
3. Issues filed by the agent are forbidden in the harness repo
   (Decision C35-13). GitHub issues in `henrik-me/agent-harness` are an
   INBOUND channel from external contributors / the user; the agent
   reads them as input to file CSs but never opens them.

   **Scope clarification (CS55 / LRN-137):** C35-13 applies to the
   harness repo only. Cross-repo handoff issues filed into OTHER
   repositories (e.g. `henrik-me/sub-invaders`) are governed by Hard
   Rule § 6 in `INSTRUCTIONS.md` / `.github/copilot-instructions.md` *(if your consumer syncs them)*
   and the `## Cross-repo procedures` section below. In those repos,
   the orchestrator MUST file an issue (rather than commit/push/PR
   directly) and is expected to create exactly one tracking issue
   labeled `harness-orchestrator` per cross-repo workstream.

### Plan-vs-implementation review (close-out gate)

`harness close-out CS<NN>` (CS64) enforces this gate as Phase 1 of its
preflight: it refuses to proceed unless the active CS file's
`## Plan-vs-implementation review` section is populated with **Reviewer:**,
**Date:**, and **Outcome:** GO. `--apply` then performs the `active → done`
rename and the WORKBOARD row removal, and refuses to mark the close-out
PR-ready until `CONTEXT.md` has also been updated (freshness gate). The
verb NEVER commits — you own the commit message and the PR.

**Opening the close-out PR — label at creation (CS71).** Same rule as the claim
PR: open with `--label workboard-only` in the `gh pr create` command itself;
do **not** add it post-hoc via `gh pr edit --add-label` (that fires a separate
`labeled` event and creates the same race as the claim PR). Since CS71, evidence
gates are also **path-derived** (a correctly-shaped close-out PR is green without
the label), but the label is **still required** for `workboard-auto-approve.yml`
to auto-merge.

This gate is **mandatory** before opening the close-out PR and before
the `active → done` rename. Run it against the merged content HEAD (or the
content diff), not a half-migrated close-out worktree.

**Reviewer:** GPT-5.5 (rubber-duck). Fallback: Claude Sonnet 4.6, subject
to the independence invariant in [REVIEWS.md](REVIEWS.md) (non-high-risk
only; user waiver always allowed).

**Inputs the reviewer must consume:**

- The active CS file (all deliverables, tasks table, sub-agent reports).
- The actual diff against the base branch:
  `git diff main..cs<NN>/content`.
- The test count delta (tests before vs. after).
- Any sub-agent final reports recorded in the CS file.

**Required outputs the reviewer must produce:**

- **Per-deliverable outcome table** — for each deliverable listed in the CS
  plan, one of: `match` | `diverged` | `added` | `dropped`, with a rationale
  sentence for every non-`match` entry.
- **Test-coverage assessment** — `sufficient` OR `gaps` with a specific list
  of untested scenarios.
- **Overall outcome** — `GO` | `NEEDS-FIX`.

**Recording the review:**

The orchestrator records the review verbatim in the active CS file's
`## Plan-vs-implementation review` section **before** the `active → done`
rename. Renaming first leaves a `done/` file with an unfilled PVI section
that `check-clickstop` correctly rejects. The section must contain:

```
**Reviewer:** <model name + rubber-duck | fallback reason>
**Date:** <ISO 8601 timestamp>
**Outcome:** GO | NEEDS-FIX

<prose summary — per-deliverable table + coverage assessment>
```

> **Field labels are matched verbatim by `check-clickstop.mjs`** (case-sensitive,
> bold-prefixed): `**Reviewer:**`, `**Date:**`, `**Outcome:**`. No aliases —
> e.g. `**Verdict:**` instead of `**Outcome:**` will fail the linter. Copy the
> code block above as-is when recording the review.

**Blocking behaviour:**

A `NEEDS-FIX` outcome blocks close-out. Fix the gap on the `cs<NN>/content`
branch and re-run the gate before proceeding.

**Mechanical enforcement:**

`check-clickstop.mjs` enforces the presence of the
`## Plan-vs-implementation review` section and its required content for all
`done/` files. The linter is wired into `harness lint` and runs on every PR.

### Plan review attestation procedure (CS35b)

This procedure is the **planning-phase counterpart** of the close-out gate
above. Per CS35b decisions C35b-1 through C35b-15, every clickstop file in
`project/clickstops/planned/` and `project/clickstops/active/` MUST carry a
`## Plan review` H2 section recording one or more independent plan reviews.
Done files are exempt — the close-out gate above already covers that surface.

**Reviewer:** GPT-5.5 (rubber-duck). Fallback rules from [REVIEWS.md](REVIEWS.md)
apply (independence invariant per C35b-4: reviewer model MUST NOT appear in
the row's `Plan author model(s)` column or in any earlier row's
`Plan author model(s)`).

**Inputs the reviewer must consume:**

- The full plan file: Background, Decisions, Deliverables, Sub-agent fan-out,
  Exit criteria, Risks + open questions.
- Any cross-CS dependencies the plan declares.

**Required verifications (per [REVIEWS.md § 2.6c](REVIEWS.md#26c-plan-review-scope--fact-claim-verification-lrn-139--lrn-158)):**

Before recording a `Go` (or `Go-with-amendments`) verdict, the reviewer
MUST have affirmatively verified every factual claim the plan makes about
the repository at the analyzed HEAD — across **all** reviewer-consumed
sections enumerated above (Background, Decisions, Deliverables,
Sub-agent fan-out, Exit criteria, Risks + open questions, and any
cross-CS dependencies), not only the hashed Decisions+Deliverables. The
plan-review hash attests only that the reviewer saw a particular
Decisions+Deliverables body; F1–F6 attest that the reviewer verified the
plan's factual premises across the whole reviewer-consumed surface.
Specifically:

- **F1** every `--flag` named in the plan exists in the CLI surface (or
  is explicitly described as not-yet-existing — for plans whose
  deliverables include adding a new flag);
- **F2** every `path:line` citation actually contains what the plan asserts
  at the analyzed HEAD (line numbers drift across snapshots/syncs/edits);
- **F3** doctrine-strength claims (`required`, `mandatory`, `enforces`,
  `recommended`, `optional`) match the cited source verbatim or via a
  documented synonym;
- **F4** LRN/CS scope summaries stay within the source entry's
  Problem/Finding scope;
- **F5** cross-doc claims are mutually consistent;
- **F6** every **state-of-the-world claim** (release/tag/PR/issue/label
  state, branch protection, ruleset config, etc.) is verified at
  plan-review time via a non-mutating CLI probe — `gh release list --repo <owner>/<repo> --limit N`,
  `gh api repos/<owner>/<repo>/releases --jq 'map(select(.tag_name=="<tag>"))'`
  (both published AND draft), `git ls-remote origin refs/tags/<tag>`,
  `gh pr view <num> --repo <owner>/<repo>`, `gh issue view <num> --repo <owner>/<repo>`,
  `gh label list --repo <owner>/<repo>`, etc. — and the probe is recorded in
  the plan's Background or Constraints so subsequent reviewers can audit
  the same premise.

Inherited findings (line numbers from another snapshot, tag/release state
assumed from prior CS plans, Copilot citations from a sibling-repo PR)
MUST be re-verified against the current HEAD before being accepted as a
plan premise. Returning `Go` on an unverified inherited citation is a
process bug — see REVIEWS.md § 2.6c for the CS54-T1 and CS70 source
incidents and the full F1–F6 table.

**Reviewer-prompt requirement.** Every plan-review dispatch MUST include
language equivalent to the F1–F6 verification clause carried in the
canonical reviewer preamble below (`## Reviewer dispatch — canonical
preamble`), which references § 2.6c. The orchestrator MUST NOT issue a
plan-review dispatch that omits this clause; if a returned `Go` verdict
shows no evidence the reviewer ran F1–F6 (no CLI-probe output for any
state-of-the-world claim, no file-open for any `path:line` citation), the
orchestrator MUST re-dispatch.

**Required outputs the reviewer must produce:**

- A verdict from the enum `Go` | `Go-with-amendments` | `Needs-Fix` (C35b-5).
- A findings recap ≤ 200 characters suitable for the table cell.

**Recording the review:**

The orchestrator records the review verbatim in the plan file's
`## Plan review` section, placed after `## Decisions` and before
`## Deliverables` (per C35b-1). Section template (paste-ready, fill the
eight cells; compute the hash via `harness plan-review-hash <file>`):

```
## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | <reviewer-model-id> | <author-model-id-1,author-model-id-2,...> | <agent-id (or "rubber-duck dispatched")> | <12-char-hash from `harness plan-review-hash <file>`> | YYYY-MM-DDThh:mm:ssZ | Go | <short summary, ≤200 chars> |
```

Subsequent amendment rounds append `R2`, `R3`, ... rows below `R1`. The
latest row's `Reviewed sections hash` MUST equal the SHA-256-prefix-12 of
the file's current `## Decisions` + `## Deliverables` bodies (per C35b-3 —
the linter computes this on every run via `lib/plan-review-hash.mjs`). Once
a `## Decisions` or `## Deliverables` row is covered by a recorded plan-review
hash, factual errors found later must be corrected in the implementation and
recorded as a dated `## Notes` deviation; never edit the hashed section just
to make the plan match, because that invalidates the attestation.

**Blocking behaviour:**

A `Needs-Fix` latest verdict blocks merge. Apply the requested amendments
on the same branch, re-dispatch the reviewer, and append a new attestation
row with the post-amendment hash. The plan-vs-implementation review ladder
in [REVIEWS.md](REVIEWS.md) (3-round cap, escalate on R3 Needs-Fix) applies
identically to the planning-phase ladder.

**Strictness asymmetry (C35b-9 / C35b-10 / C42-7):**

- `harness lint` (standalone, pre-PR convenience) ran the linter with
  `--strict=false` in v0.4.0 (warn-only on missing-section). v0.5.0 (CS42)
  flipped the default to `true`; standalone lint now ERRORS on missing
  section by default. Consumers mid-migration can pass `--strict false`
  explicitly.
- The PR-time A6 gate dispatched by `harness pr-evidence` (CS36) ALWAYS
  runs in `--mode=pr-evidence`, which is STRICT regardless of `--strict`.
  The v0.4.0 asymmetry between local warn and PR strict has been collapsed
  to "always strict by default" in v0.5.0.
- Schema / independence / hash / verdict violations are ALWAYS errors,
  regardless of mode or `--strict`. Only the "section entirely absent"
  case is governed by the warn-vs-strict toggle.

**Mechanical enforcement:**

`scripts/check-clickstop-plan-review.mjs` (registered as
`check-clickstop-plan-review` in `harness lint` per CS35b decision C35b-8)
parses the table, validates the schema, enforces independence, verifies
hash freshness, and gates on the latest verdict. The CS36 PR-evidence
aggregator dispatches the same script in strict pr-evidence mode (A6).

**Honor-system caveat (C35b-14):**

The linter cannot verify the claimed reviewer model actually ran. As with
B1 commit trailers, this is honor-system attestation: the schema enforces
deliberation; orchestrator discipline + the close-out plan-vs-implementation
review catch lies. Future CS may add cryptographic evidence; this is
documented in [LEARNINGS.md](LEARNINGS.md).

### Enforcement model

**CS01–CS14 (private repo, discipline-only):** GitHub branch protection
requires GitHub Pro on private repos (see [LRN-001](LEARNINGS.md#lrn-001)).
All PRs are opened, reviewed, and squash-merged through the normal review
loop. The discipline replaces the missing mechanical enforcement.

#### Required review status checks (review-gates)

Content PRs MUST pass four PR-side status checks before merge:

| Check | What it verifies |
|---|---|
| `review-log-evidence` | `## Review log` contains at least one real `Go` / `Conditional Go` row by GPT-5.5, or by an approved fallback with `## Model audit` fallback rationale populated; template placeholders fail the gate. |
| `copilot-review-attached` | The configured Copilot PR reviewer (default `copilot-pull-request-reviewer[bot]`) has submitted a review; when missing, the workflow posts `@copilot review` as a best-effort trigger, and comment-permission failures leave the gate failed with an actionable error. |
| `independence-invariant` | `## Model audit` has populated implementer/reviewer model rows and rejects implementer/reviewer model overlap except the GPT-5.5 allowance for non-HIGH-RISK CSs. |
| `review-threads-resolved` | Every GitHub review thread on the PR is resolved. |

The `review-gates.yml` workflow runs on every PR except PRs labeled
`workboard-only`. **The `workboard-only` bypass is confined to its path
allowlist (CS63 C63-7):** a `validate-workboard-only-scope` job (and the
`pr-evidence` skip-reason check) rejects a `workboard-only`-labelled PR whose
diff touches any file outside `WORKBOARD.md` / `CONTEXT.md` / `LEARNINGS.md` /
`project/clickstops/`, so the label cannot bypass review on content. Genuine
workboard-only claim/close-out PRs are already constrained by
the workboard-only validation path. Configure the gates under
`harness.config.json → reviews`: `enforce_gates` controls workflow/ruleset
installation, `require_copilot_review` lets consumers without Copilot reviews
skip only the Copilot attachment gate, and `copilot_reviewer_slug` / `high_risk_clickstops`
customize the reviewer login and risk list. `harness init --enable-review-gates`
and `harness sync --mode=apply` inject the four contexts into
`infra/main-protection-ruleset.json` `required_checks`; `sync --mode=check`
fails when `reviews.enforce_gates=true` and the contexts are missing.

**Public protected phase (CS15a+ in this repo):** The Ruleset authored and
applied during CS15a enforces PR-required, ≥1 approving review, squash-only,
linear history, deletion/non-fast-forward protection, required status checks,
and conversation resolution. Repository admins have an explicit bypass actor
for owner override (LRN-080). Decision #23 activates the
`workboard-auto-approve.yml` bot: it verifies path-restriction +
`workboard-only` label + actor allowlist, submits the approval, and
auto-merges. The global review-required rule stays in force; the bot's review
satisfies it for eligible workboard-only PRs.

#### Consumer structural PR gate (harness-pr-check, CS63a)

Fresh `harness init` also installs `.github/workflows/harness-pr-check.yml`
(default-on; opt out via `harness.config.json → pr_check.enabled: false`). On
every PR it runs `harness lint` plus a file-class drift classifier
(`scripts/check-managed-drift.mjs`) that **fails the PR when a `managed` or
`composed` template file has been diverged** from its rendered template —
shipping the structural-integrity protection the harness enforces on itself as a
real consumer merge gate. `seeded` files are consumer-owned and never fail the
gate. An emergency managed edit can land via a `harness-managed-edit-ack` PR
label **plus** a `Harness-managed-edit:` justification line in the body (the
override is surfaced in the gate output, never silent). The workflow reads the
harness ref from the **base-branch** config and declares least-privilege
permissions, defeating fork-PR ref injection.

### Workboard-first for out-of-CS work

Rule: before starting any out-of-CS work (hotfix, single-file follow-up, doc
edit, post-CS cleanup, or other user-visible one-off), the orchestrator must
update `WORKBOARD.md` — or the consumer repo's equivalent live coordination file — so
the user can see the work in progress before the first implementation step.
This is in addition to any planned-CS-file flow.

Use the existing `## Active Work` table shape: `CS-Task ID`, `Title`, `State`,
`Owner`, `Branch`, `Last Updated`, and `Blocked Reason`. Record a short title,
the branch, an in-progress state such as `🟢 Active`, the owner agent, the date,
and the user-facing reason in `Title` (or `Blocked Reason` when blocked). Until
the workboard schema grows a dedicated out-of-CS identifier, use the nearest
CS-shaped tracking ID with a lowercase suffix (for example, `CS02h`) rather than
inventing an arbitrary ID that `check-workboard.mjs` will reject.

Example Active Work row for a downstream hotfix:

```
| CS02h | Hotfix torpedo-collision regression — restore user-visible gameplay correctness | 🟢 Active | yoga-si | hotfix/torpedo-collision | 2026-05-14 | — |
```

#### Workboard-only PR admin-bypass fallback

**The zero-secret default is maintainer admin-override.** The sanctioned way to
merge a `workboard-only` PR — whether or not its branch matched an auto-merge
pattern — is for a maintainer to squash-merge it directly with
`gh pr merge <n> --admin --squash`. This needs **no** secret, App, or PAT — but
it does require the repo's `main-protection` ruleset to grant repo admins an
explicit bypass actor (`actor_type: RepositoryRole`, `actor_id: 5`,
`bypass_mode: always`); GitHub rulesets do **not** exempt admins automatically
(per [LRN-080](LEARNINGS.md#lrn-080)). The self-host ruleset carries this bypass;
the harness-generated minimal ruleset (`minimalReviewRuleset()` /
`infra/main-protection-ruleset.json`) ships with `bypass_actors: []`, so a
consumer that wants this zero-secret admin path must first add the repo-admin
bypass to its `main-protection` ruleset (verify with
`gh api repos/<owner>/<repo>/rulesets/<id>`). The `validate-and-approve` workflow only
*auto-approves* eligible PRs — it is a convenience, not a prerequisite for
merging. Treat the App/PAT automation below as **optional** — worthwhile for
higher-volume or multi-maintainer setups that want hands-off merges — not as a
required or intended path.

Consumer repos that want that hands-off automation but have not installed the G3
workboard GitHub App may instead configure a per-repo secret named
`WORKBOARD_MERGE_TOKEN`. The token should be a fine-grained PAT with repository
permissions `contents: write` and `pull-requests: write`; the token owner must
also be allowed to bypass the `main-protection` ruleset (typically by being a
`RepositoryAdmin` bypass actor, per [LRN-080](LEARNINGS.md#lrn-080)). If you
manage ruleset bypass actors via `gh`/API, refresh your local auth first with
`gh auth refresh -s admin:org`; otherwise create the fine-grained PAT in GitHub's
developer settings UI and add it to the consumer repo as the
`WORKBOARD_MERGE_TOKEN` Actions secret.

The automation degrades to that default gracefully. When neither the App nor the
PAT is configured, the workflow keeps running the label/branch/actor/path
validation and then either uses the existing GitHub App path (if
`WORKBOARD_BOT_APP_ID` + `WORKBOARD_BOT_PRIVATE_KEY` are configured) or logs
`validation-only` — signalling that the maintainer finishes the merge with the
zero-secret admin-override above. The PAT cannot expand the workboard-only
surface: the workflow uses it only after the same actor allowlist,
same-repository, immutable-head, and path-allowlist gates pass, and the admin
merge re-checks the PR head plus reported non-workboard status checks before
invoking `gh pr merge --admin`.

---

## Dispatch

Branch from main immediately after the claim PR merges:

```
git checkout -b cs<NN>/content
```

All implementation work happens on this branch. Sub-agents may be dispatched
per the parallelisation table in the active CS plan. See § Sub-agent dispatch
for the full briefing and reporting model.

---

## Handoff

If you need to leave a CS mid-flight:

1. Run `harness status` (CS64) and capture its one-screen snapshot in the
   handoff note — it lists the current active CS, the WORKBOARD Active Work
   rows, and the in-flight `planned`/`active` arc, which is the exact context
   another orchestrator (or a future you) needs to resume.
2. Update `WORKBOARD.md`: set `state = ⏸ Paused` (or `🔴 Blocked`) with a
   brief reason and the `last-updated` timestamp.
3. Commit on the content branch and push: "WIP: <brief reason>" (this commit
   will be squash-merged later; it exists only to preserve work-in-progress
   state).
4. Note the `reclaimable` threshold in the WORKBOARD row (default: 7 days
   with no update). After that threshold, another orchestrator may pick it up
   by updating the WORKBOARD row with the new agent ID.

---

## Cross-repo procedures

This section governs orchestrator behaviour when work crosses the boundary
of `henrik-me/agent-harness` into other repositories (e.g. consumer repos
such as `henrik-me/sub-invaders`). It is the operational complement to
Hard Rule § 6 in `INSTRUCTIONS.md` / `.github/copilot-instructions.md` *(if your consumer syncs them)*.

### Handoff pattern: issue-only, never direct PR

**Rule:** The harness orchestrator MUST NOT commit, push, open branches,
or create pull requests in any repo other than `henrik-me/agent-harness`.
The orchestrator files a GitHub issue and lets the consumer-repo agent
own the PR, validation, and merge. There is no escape hatch — even
urgent cross-repo work routes through an issue. (The human user can
still act directly outside the orchestrator at any time.)

**Pre-flight — verify the target artifact exists before filing an "update file X"
issue (CS70 / LRN-165).** Before filing a cross-repo issue whose deliverable is
"update / annotate / add file `X` in consumer repo `Y`", the orchestrator MUST first
verify **either** (a) that file `X` already exists in `Y` (e.g.
`gh api repos/Y/contents/<path>`, `git ls-remote`, or a clone check), **or** (b) that a
harness contract produces it in consumers (a `seeded` / `managed` / `composed` file under
`template/**`, or a scaffold emitted by `harness init` / `harness sync`). If **neither**
holds, `X` is a phantom target: the work does **not** belong in a cross-repo issue — it
belongs in a **harness-side CS**. Filing a consumer issue to "update a file that does not
exist and that no harness contract emits" routes work against a phantom artifact — exactly
the `sub-invaders-bootstrap-summary.md` misrouting
([sub-invaders#91](https://github.com/henrik-me/sub-invaders/issues/91) →
[agent-harness#290](https://github.com/henrik-me/agent-harness/issues/290); see LRN-165).

**Status questions (e.g. "is SI updated to v0.6.0?"):**

1. Read-only inspection first: `gh pr list --repo OWNER/NAME`,
   `gh issue list --repo OWNER/NAME`, `gh api repos/OWNER/NAME/...`.
2. If a tracking issue already exists for the work in question
   (any state: open or closed within the relevant window), DO NOT
   file a duplicate; report the existing URL.
3. If no tracking issue exists, idempotently create exactly ONE issue
   per workstream using the procedure below.

**Issue-creation procedure (idempotent, non-destructive / non-overwriting (no `--force`)):**

1. **Pre-create existence check (idempotency guard).** Before creating,
   search for an existing tracking issue in the target repo to avoid
   duplicates. Use the `[harness:csNN]` title prefix as the stable
   identifier:

   ```
   gh issue list \
     --repo OWNER/NAME \
     --label harness-orchestrator \
     --state all \
     --search "[harness:csNN] <title terms> in:title"
   ```

   If exactly one issue matches (open or closed within the relevant
   window), do NOT create a duplicate; reuse the existing URL and
   report it (idempotency: re-asking the same status question must
   return the same issue). If multiple matches exist, that is a
   coordination drift — surface it as an escalation rather than
   creating a third.

2. **Label preflight (D55-3).** Ensure the routing label exists in the
   target repo. Invoke:

   ```
   gh label create harness-orchestrator \
     --repo OWNER/NAME \
     --color 0E8A16 \
     --description "Filed by harness orchestrator"
   ```

   Do NOT pass `--force`. If `gh label create` exits non-zero AND its
   stderr contains an "already exists" indication, treat as success
   (the label is already there with whatever color/description the
   consumer chose — do not overwrite). Any other non-zero exit
   (e.g. HTTP 403, network failure) is a real failure to escalate.

3. **Title convention:** prefix with `[harness:csNN]` where `csNN` is
   the originating CS that motivates the cross-repo handoff. Example:
   `[harness:cs55] Adopt v0.6.x cross-repo handoff doctrine`. The
   `[harness:csNN]` prefix is the stable identifier used by step 1's
   pre-create search; it prevents collision with future cross-repo
   handoff issues. (CS55 establishes this convention; CS56's `harness
   cross-repo open-issue` CLI is the supported handoff path — it
   applies the `harness-orchestrator` label and performs an idempotent
   exact-title search programmatically. **Two important caveats:** (a)
   the CLI does NOT enforce the `[harness:csNN]` prefix on `--title`
   (the prefix remains doctrine that operators must apply themselves);
   and (b) the CLI's idempotency only searches **open** issues
   (`gh issue list --state open`), so step 1's all-state pre-create
   check for relevant closed issues remains an operator responsibility
   when reusing a recently-closed tracking issue is desired.)

4. **Required body fields** (markdown):
   - **CS reference:** the originating harness CS (e.g. `CS55`) and a
     link to its file under `project/clickstops/done/` or `active/`.
   - **Target repo + kind of work:** which consumer repo, and a short
     classification (e.g. pin-bump, doctrine adoption, schema sync).
   - **Context:** why this issue was filed (link to harness merge
     commit SHA and/or release tag, e.g. `v0.6.x`).
   - **Requested action / ask:** the concrete change requested in the
     consumer repo, written as a checklist where possible.
   - **Acceptance criteria:** how the consumer agent will know the
     work is complete.
   - **Verification steps:** which harness checks / lint commands to
     run on the consumer side (e.g. `npx -y github:henrik-me/agent-harness#v0.17.0 lint`).
   - **Relevant LRNs / docs:** links to applicable `LEARNINGS.md`
     entries and the harness `OPERATIONS.md` / `INSTRUCTIONS.md` *(if your consumer syncs it)*
     sections that govern the handoff.
   - **Harness PR / tag links:** the merged harness PR and tag (if
     any) that supply the artefact the consumer will adopt.
   - **Coordination:** confirmation that the harness orchestrator
     will not push directly; consumer-repo agent owns the PR.

5. **Required label:** `harness-orchestrator` (always present as the
   uniform routing default per D55-3). Supplemental labels (e.g.
   `harness-sync`, `release-blocker`) are permitted as additions and
   never replace or remove the default.

6. **Record the URL** in the active CS file's Notes section. The
   close-out PR carries it forward into the done CS file.

**Exit criteria for a cross-repo handoff:** exactly one open tracking
issue exists in the target repo with the `harness-orchestrator` label
and `[harness:csNN]` title prefix; the close-out PR diff records its
URL; the orchestrator has neither committed nor opened a PR in the
target repo. (A consumer-repo agent may close the issue once the
consumer-side PR merges; that closure is the consumer's signal, not
the orchestrator's prerequisite for harness close-out.)

### Cross-repo pin-bump PR body checklist (CS54)

When the consumer-repo agent opens a cross-repo PR in response to a
harness-filed issue (typically a harness pin bump in a consumer repo
such as `henrik-me/sub-invaders`), the PR body MUST include the
canonical evidence sections at PR-open time, NOT relying on the
consumer's `.github/pull_request_template.md` to inject them. Two
reasons (per LRN-134):

1. Consumer PR templates can lag the harness version (the template is
   not in the managed file class by default, so `harness sync` does
   not auto-refresh it).
2. Since v0.6.0 the strict-flip default (`--strict-agent-columns`)
   requires the new `Implementer agent` / `Reviewer agent` rows in
   `## Model audit`; a pre-v0.6.0 template would silently produce an
   A3 hard-fail on `read-only-gates`.

This checklist is consumer-side doctrine but the harness orchestrator
MUST include it verbatim in every cross-repo handoff issue body
(under "Verification steps" / "Acceptance criteria") so the consumer
agent has a single source of truth.

**Required PR body sections (in this order):**

1. `## Summary` — one paragraph describing the cross-repo change.
2. `## Changes` — bulleted per-file enumeration of the consumer-side
   diff.
3. `## Testing` — what was run to verify the consumer-side change works
   (lint, tests, manual smoke).
4. `## Model audit` — `| Field | Value |` table with the required rows:
   - `Implementer models` (model IDs that materially produced the
     change)
   - `Reviewer model` (rubber-duck reviewer model)
   - `Implementer agent` (the **consumer-side** agent that authored the
     PR — NOT the harness orchestrator. The orchestrator only files the
     handoff issue and does not commit to the consumer repo per the
     doctrine above; the Model audit must record the actual PR author)
   - `Reviewer agent` (the reviewer's identity, e.g. `rubber-duck`)
   - Optional `Fallback rationale` when the reviewer model is an
     approved fallback (e.g. `sonnet-4.6` because GPT-5.5 was
     unavailable per § 2.2), not for implementer/reviewer overlap
     (overlap is enforced separately by the `independence-invariant`
     gate and is normally merge-blocking).
5. `## Review log` — 6-column table: `timestamp | analyzed_head |
   actor | model | verdict | evidence_link`. At least one `Go` (or
   `Conditional Go`) row at the current PR HEAD before merge. The
   `model` column MUST be the bare reviewer-model identifier (e.g.
   `gpt-5.5`); decorations like `gpt-5.5 (R2)` are not permitted —
   put round / role annotations in the `actor` column instead (see
   REVIEWS.md § 2.8).
6. Plan link to the originating harness CS file.

**Pre-open self-check:** before `gh pr create`, draft the body file
locally (UTF-8, LF, no BOM) and grep for `^## Model audit`,
`^## Review log`, `Implementer agent`, `Reviewer agent`. If any
missing, fix before opening — amending after `read-only-gates` fails
is more expensive than fixing before open.

**Sequencing rule (PR body push triggers re-attest):** If the
body is amended via `gh pr edit --body-file` after R1, the commit
SHA does NOT change — A4 stale-diff currency is unaffected because
A4 compares the latest Go row's `analyzed_head` against the actual
commit SHA. However, **review-evidence currency** is affected: the
Review log table itself, Copilot review provenance, and reviewer
narratives are PR-body artefacts that the rubber-duck and Copilot
reviewers may not have seen at R1. Use the narrow re-attest pattern
(next section) to refresh the Review log + Copilot provenance at the
post-body-push state. Adds a new Review log row at the unchanged
commit SHA — the timestamp shifts forward; the `analyzed_head` is
identical to the prior row.

**Idempotency note:** the issue-creation rules above (one open issue
per workstream, `[harness:csNN]` title prefix) apply unchanged. The
PR-body checklist is per-PR; the issue-creation guard is
per-workstream.

### Adopting the strict PR template in an existing consumer (CS54b)

The harness ships its PR template as a **composed** file
(`.github/pull_request_template.md`, rendered from
`template/composed/.github/pull_request_template.md`). Since v0.6.0
the shipped template already carries the strict `## Model audit`
(with `Implementer agent` / `Reviewer agent` rows + optional
`Notes`) and the 6-column `## Review log`, so a **fresh**
`harness init` seeds a consumer with the strict schema
automatically.

An **existing** consumer can still carry a stale, pre-strict copy
(the SI PR #79 failure mode: a pre-v0.6.0 template silently produces
an A3 hard-fail on `read-only-gates`). The harness does **not**
auto-rewrite a consumer's `.github/pull_request_template.md` unless
the consumer has opted the file into the composed flow — it is
consumer scaffold, and silently overwriting it could clobber local
customisations. Adoption is therefore **opt-in**:

1. **One-time copy (recommended — simple and reliable).** Copy
   `template/composed/.github/pull_request_template.md` from the
   pinned harness version over the consumer's
   `.github/pull_request_template.md` and commit it. This immediately
   adopts the strict schema; the file stays consumer-owned (re-copy
   on future harness bumps if desired).

2. **Reclassify for an ongoing harness-seeded evidence block
   (advanced).** Register `.github/pull_request_template.md` under the
   consumer's `harness.config.json` `composed.files`, with a
   `composed.overrides` entry that sets `"_inherited_class": "managed"`
   **and** `"local_blocks": ["pull-request.review-evidence"]`
   (mirroring how the harness itself ships the file). With that hint,
   `harness sync` runs the inherited-managed merge, which **preserves
   the consumer's existing content as-is** and, when the
   `pull-request.review-evidence` block is absent, **appends a seeded
   copy of it at end-of-file** (the strict `## Model audit` +
   `## Review log` placeholders, with a sync warning to relocate the
   block to your preferred position); an already-present block is
   preserved as consumer-owned. This path therefore **adds** the strict
   evidence sections to the current file rather than replacing it with
   the canonical template layout — use the one-time copy above if you
   want the full canonical template. Without the
   `"_inherited_class": "managed"` hint, the first sync of a file whose
   content does not already match the template fails closed
   (`EMERGE_LEGACY_UNMAPPED`).

Until a consumer adopts the strict template by either path, the
**inline-sections fallback** in the pin-bump checklist above remains
the safety net: author the canonical `## Model audit` + `## Review
log` sections directly in each PR body at open time rather than
relying on the (possibly stale) template to inject them.

### Narrow re-attest after trivial commits (CS54)

When a content PR receives small follow-on commits in response to
Copilot inline findings (typical: doc-only or 1-2 line code cleanups,
no behaviour change), a full rubber-duck re-review on every new HEAD
is overkill. The "narrow re-attest" pattern (per LRN-135) is the
cheap mitigation that keeps A4 (stale-diff currency) green without
re-paying the full GPT-5.5 round-trip.

**Three preconditions:**

1. The delta is genuinely trivial: ≤ 20 lines, doc-only or 1-2 line
   code cleanups responding to Copilot inline findings, no behaviour
   change.
2. R1 was a full-diff review at a prior HEAD, and that R1's `Go` row
   is still present in the Review log table.
3. The reviewer model and reviewer agent stay the same as R1; only
   the `timestamp` + `analyzed_head` (and optional one-paragraph
   delta summary) change.

**Dispatch shape (sync, ≤ 1 min):** brief the same rubber-duck model
with: "R1 already cleared the diff; only re-verify the trivial delta
from `<prev-head>` to `<new-head>` is innocuous; return `Go` or
`Needs-Fix`. Do NOT re-review the diff." Append the result as a new
Review log row with the new `analyzed_head`, the same model, the
same actor annotated `(narrow R2)` / `(narrow R3)`, and a
one-paragraph summary.

**Not a substitute for full re-review when the delta is substantive
(e.g. new test coverage, refactored module).** When in doubt, run a
full review.

Cross-refs: REVIEWS.md § Plan review (recommended mitigation when CS
plan delta is doc-only); REVIEWS.md § PR-evidence gates (A4
stale-diff currency); LRN-125 (Copilot review chase analogue — body
push triggers another review cycle).

---

## Sub-agent dispatch

The orchestrator (Claude Opus 4.8) dispatches sub-agents for parallelisable
sub-tasks per the parallelisation table in the active CS plan. Sub-agents
must be **briefed with structured context** and must **report back with a
structured report**. Both requirements are non-negotiable — without them the
orchestrator loses observability and the work loses traceability.

`harness dispatch` (CS64) emits the canonical sub-agent briefing preamble
verbatim from the harness-owned managed [`DISPATCH-PREAMBLE.md`](DISPATCH-PREAMBLE.md)
(see [§ Mandatory briefing preamble](#mandatory-briefing-preamble-copy-verbatim-into-every-dispatch)) —
the CRITICAL PREFLIGHT block + File ownership + Required reading +
Conventions + Self-checks + Reporting independence + Mandatory report shape.
Paste its output as the first thing in every sub-agent prompt to satisfy the
"verbatim paste, not reference" discipline that LRN-068 captures. The verb is
deterministic and read-only.

### Models

| Role | Model |
|---|---|
| Orchestrator | Claude Opus 4.8 (fallback Claude Opus 4.7) |
| Coding, unit-test & implementation sub-tasks (code/docs/config) | Claude Opus 4.8 (fallback Claude Opus 4.7) |
| Local review (primary) | GPT-5.5 |
| Local review (fallback, non-high-risk) | Claude Sonnet 4.6 (independence invariant — see REVIEWS.md) |

### Briefing template

Every sub-agent prompt includes the following sections **in this order**.
Quote directly-relevant conventions verbatim so the sub-agent does not
need to chase pointers.

#### 1. Identity + scope

State the agent role (e.g. `"mechanical sub-task on CS06"`), the CS being
contributed to, the **exact files owned by this sub-agent**, and explicit
boundaries (what NOT to touch).

Each sub-agent owns a **disjoint file set**. Overlapping write scope causes
silent file races: the later writer wins and the earlier agent's work is lost
with no error. Non-overlapping ownership is the only safe parallel model.
See **Explicit file ownership** below ([LRN-016](LEARNINGS.md#lrn-016)).

#### 2. Hard no-commit preflight ([LRN-021](LEARNINGS.md#lrn-021))

The briefing's **first paragraph** must be a `CRITICAL PREFLIGHT` block
requiring the sub-agent to:

- Record the current HEAD SHA: run `git log --oneline -1` at the start and
  include the result in the report.
- Verify at report time that the SHA is unchanged: `git log --oneline -1`
  in the final response must match the preflight SHA.
- Include `git status --short` in the final response showing only untracked
  or modified files — never staged or committed changes.
- State literally: "No commit was created."

**No commit / push / rebase / reset / gh pr** is permitted by any sub-agent.
The orchestrator commits at the end of each CS. This invariant has been
validated across 18+ sub-agent dispatches with zero violations after
standardization in [LRN-021](LEARNINGS.md#lrn-021).

#### 3. Required reading

List paths explicitly — do not say "read whatever you need":

- `INSTRUCTIONS.md` *(if your consumer syncs it)*, `CONVENTIONS.md`, the active CS file, the cs-plan.
- All ADRs that touch the deliverables area. When briefing
  a schema-author sub-agent, cross-check every ADR: ADR constraints
  frequently exceed what the cs-plan deliverables list restates (validated
  in [LRN-007](LEARNINGS.md#lrn-007) — omitting ADR 0002 cost three sub-agents a re-dispatch cycle).
- Relevant done CS files for prior art and conventions.

#### 4. Explicit file ownership ([LRN-016](LEARNINGS.md#lrn-016))

List every file the sub-agent may **write** and every file it may only
**read**. If two parallel sub-agents need the same file, designate one as
owner (may write) and the other as reader (must NOT write).

This rule was discovered empirically in CS03: `cs03-sync` wrote stubs for
`lib/templating.mjs` and `lib/lock.mjs` so its own code could `import` them.
The dedicated owners (`cs03-templating`, `cs03-lock`) reported rich APIs but
their work was silently overwritten by the stubs. The stubs — not the rich
APIs — were what remained on disk. `tests/lock.test.mjs` was lost entirely.

Verify disk state after each parallel-dispatch wave before declaring a wave
complete — see **Post-completion verification** below.

#### 5. Conventions to follow

Quote each convention verbatim in the briefing. Required conventions:

**Schema is source of truth** ([LRN-039](LEARNINGS.md#lrn-039))
Any code that reads `harness.config.json`, `.harness-lock.json`, or any
other structured config file must read `schemas/*.schema.json` first. Do not
guess field names from intuition. Guesses that match happen to work in unit
tests (because fixtures are authored against the same guess) but fail
integration silently. Before writing any field access: open the schema,
find the exact path, write the access from the schema.

**`requireValue` arg guard** ([LRN-040](LEARNINGS.md#lrn-040))
Every CLI flag that takes a value (e.g. `--file <path>`, `--config <path>`)
must guard the next token before consuming it. The guard must (a) verify
`args[i+1]` exists and (b) reject tokens that start with `-`, exiting with
code 2 and a usage message. Bare `if (args[i+1])` silently consumes the next
flag as a value, producing confusing errors like "file not found: --quiet".
The canonical guard is `requireValue(args, i, flagName)`.

**Test minimums, not exact counts** ([LRN-037](LEARNINGS.md#lrn-037))
Briefings specify a *minimum* test count. Over-delivery (writing more tests
than the minimum) is a signal of good engineering, not scope creep. It
catches edge cases the briefing did not enumerate. In CS05, delivering 12
tests against a 10-test minimum caught real `resolveLinks` contract drift.
Never specify exact counts — they create artificial pressure to stop at the
minimum and suppress coverage of discovered edge cases. The orchestrator
celebrates over-delivery on tests.

**Aggregator config single-source** ([LRN-038](LEARNINGS.md#lrn-038))
Aggregator commands (e.g. `cmdLint`) that read config AND thread it to child
subcommands must resolve the config path **exactly once** into a single
variable, then use that variable everywhere — both for local config reads and
for threading to children. Two resolution paths that agree for the happy-case
default diverge silently when a non-default `--config` or `--cwd` is passed.

**Linter explicit `--file`** ([LRN-032](LEARNINGS.md#lrn-032))
A `harness <subcommand>` wrapper that invokes a linter script must construct
the consumer-cwd-relative file path explicitly and pass it as `--file <path>`
to the script. Never let the script infer the path from `import.meta.url` or
`process.cwd()` — when the script runs as an installed package dependency,
those paths resolve inside the harness package directory, not the consumer
repo. Fix: `path.join(cwd, 'LEARNINGS.md')` as the explicit `--file` value.

**Windows `spawnSync`** ([LRN-029](LEARNINGS.md#lrn-029))
On Windows, `npm`, `npx`, and other ecosystem wrappers are `.cmd` batch
files, not executables. `spawnSync` without `{ shell: true }` attempts to
spawn the wrapper as a binary and returns EINVAL. Using `'npm.cmd'` as the
command name is not a reliable workaround. Use `{ shell: true }` for all
npm script invocations: `spawnSync('npm', args, { shell: true })`.

**`--help` re-forwarding** ([LRN-030](LEARNINGS.md#lrn-030))
A global CLI parser that intercepts `--help` must check whether a known
subcommand is also present in the argv slice before acting. If both `--help`
and a known subcommand name are present, `--help` belongs to the subcommand
— forward it to the subcommand's argv. Consuming `--help` globally when a
subcommand is present causes `harness sync --help` to print global help
instead of sync-specific flag docs.

**ESM `.mjs` only**
All harness scripts use ESM (`import`/`export`) and the `.mjs` extension.
No CommonJS `require()`. No `.cjs` files. Node.js 20+.

**LF line endings / UTF-8-BOM**
The `create` tool on Windows writes CRLF regardless of `.editorconfig`
settings ([LRN-006](LEARNINGS.md#lrn-006)). Files may also carry a UTF-8
BOM ([LRN-018](LEARNINGS.md#lrn-018)). After creating any text file on
Windows, run an explicit normalization step: strip the BOM if present and
replace `\r\n` with `\n`. All parsers that compare content must normalize in
their read step.

#### 6. Deliverables

List explicitly:

- Files to create (with purpose and minimum line / test counts).
- Files to edit (with what change is required).
- Exit criteria: the precise self-check the agent can run to verify "done".

#### 7. Self-checks before reporting

- In a freshly-created git worktree or checkout, run `npm install` in that
  checkout before dependency-backed harness linters (`harness lint`,
  `harness plan-review-hash`, schema/doc checks, etc.); `node_modules` is
  gitignored and per-checkout, not shared from the parent worktree.
- Run `node --test` and report the test count and delta.
- Run any existing linters that cover the deliverables area.
- Verify JSON schema conformance for any `.json` files created.
- Run `git status --short` — only untracked / modified files; nothing staged.
- Run `git log --oneline -1` — HEAD must match the preflight SHA.

#### 8. Decision authority and escalation

State what the sub-agent may decide independently (e.g. internal variable
names, helper-function structure, fixture design) versus what must be
escalated to the orchestrator:

- Adding or removing npm dependencies.
- Schema field additions, renames, or type changes.
- Anything that crosses CS boundaries or touches files outside the declared
  ownership set.
- Any surprising finding that materially changes the approach.

#### 9. Findings to surface

Every uncertainty, decision, deviation, or surprise must appear in the report
as a `LEARNINGS CANDIDATES` entry. The orchestrator decides whether to elevate
to `LEARNINGS.md`. **No silent decisions.** Silent decisions are the primary
source of drift between what a sub-agent reports and what lands on disk.

### Mandatory briefing preamble (copy verbatim into every dispatch)

The orchestrator MUST paste the canonical preamble block verbatim into every sub-agent
dispatch prompt — including small or seemingly "obvious" ones. This is not
a style preference; it is the discipline that prevents individual requirements
(preflight SHA recording, BOM check, file-ownership scope, report-shape
completeness) from being silently omitted. When orchestrators re-draft the
preamble from memory or reference this section by hyperlink only, individual
steps are routinely forgotten. LRN-068 demonstrates how silently-lost process
steps are not surfaced until a downstream sub-agent raises them as
escalations — if the preamble itself is incomplete, that catch also fails.

A hyperlink to this section is NOT sufficient. Sub-agents operating under
tight context or fast-path prompting will skip non-pasted references.
Verbatim paste, not reference, is the mechanism that makes the discipline
reliable.

After pasting the block, append the task-specific sections: **Identity +
scope** (agent role, CS, exact owned files, what NOT to touch), **Required
reading** (explicit paths for this CS), **Deliverables**, **Decision
authority**, and any additional task-specific conventions. Do not modify the
pasted block itself.

The preamble text itself is **not** duplicated in this document. It is the
harness-owned managed file [`DISPATCH-PREAMBLE.md`](DISPATCH-PREAMBLE.md), from
which `npx -y github:henrik-me/agent-harness#v0.17.0 dispatch` machine-extracts and emits it verbatim (CS86
C86-2). Run `npx -y github:henrik-me/agent-harness#v0.17.0 dispatch` and paste the emitted block — do not
hand-copy or reconstruct it. Per-language variants are emitted by
`npx -y github:henrik-me/agent-harness#v0.17.0 dispatch --language-profile <name>` (see § Language profiles
below).

### Subcommand authoring: never `git checkout` the consumer working repo ([LRN-124](LEARNINGS.md#lrn-124))

Harness subcommands run inside the consumer's (or self-host's) working repo,
which routinely carries **uncommitted, unstaged tracked edits**. Several git
verbs are destructive on such a repo: `reset --hard`, `restore`, `checkout -f`,
and `stash` can discard or stash away dirty tracked edits, and `clean` removes
untracked files; meanwhile `git checkout <commit-or-tag>` and
`git switch --detach <ref>` detach HEAD. The LRN-124 working-tree-loss
signature combined a detached HEAD with reverted tracked edits and no error.
CS47's bisection (`tests/cs47-detached-head-bisect.test.mjs`) proved no current
subcommand does any of this; this rule keeps it that way for new subcommands.

When a subcommand must read content at a specific ref, use, in preference order:

1. **`git show <ref>:<path>`** — read-only; never touches HEAD or the worktree. The default for inspecting tagged/committed file content.
2. **`git worktree add --detach <unique-tmpdir> <ref>`** — for multi-file scoped operations; clean up with `git worktree remove --force <path>` then `rmSync(<path>, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 })` (Windows EPERM/EBUSY-hardened).
3. **`try { prev = git symbolic-ref HEAD; git stash push --include-untracked; ... } finally { restore prev + stash pop }`** — last resort only; the `stash` is mandatory, because restoring the branch ref alone does NOT restore dirty tracked-file contents.

> **Caveat:** approaches 2–3 use the `worktree`/`stash` verbs that the CS47
> trace guard flags as mutating — at the argv level it cannot prove they are
> scoped to an isolated tmpdir rather than the primary worktree. A subcommand
> that reaches for them trips the bisection suite and must be allow-listed with
> an explicit rationale documenting why the operation cannot lose consumer
> edits. Prefer approach 1 (`git show`) wherever possible.

Any new subcommand that reaches a git ref is covered automatically: the CS47
bisection enumerates the live `COMMAND_REGISTRY`, so a new subcommand that is
neither exercised nor allow-listed (with rationale) fails the suite.

#### Language profiles

Per-language dispatch profiles (`node` default, `dotnet`) live in
[`DISPATCH-PREAMBLE.md`](DISPATCH-PREAMBLE.md) and are emitted by
`npx -y github:henrik-me/agent-harness#v0.17.0 dispatch --language-profile <name>`, which splices the
selected profile's conventions + self-checks into the language-agnostic core.
`node` is the default (`dispatch.language_profile` in `harness.config.json`, or
the `--language-profile` override). Run the command and paste the emitted block
rather than copying the profile text here.

### Canonical reviewer preamble (CS35 C35-1)

The canonical rubber-duck reviewer-dispatch preamble is maintained in its
authoritative home, [REVIEWS.md § 2.9 Canonical reviewer preamble](REVIEWS.md#29-canonical-reviewer-preamble-cs35-c35-1)
(relocated there in CS86). For content PRs on CS52+, prefer `harness review <pr>`
(see [§ Reviewer dispatch via `harness review`](#reviewer-dispatch-via-harness-review-cs52));
it composes the same guardrailed prompt. The harness CLI still does not call an
LLM API; the orchestrator dispatches the emitted prompt and paste-protocols the
structured reviewer output.

## Reviewer dispatch — canonical preamble

This canonical reviewer preamble now lives in
[REVIEWS.md § 2.9 Canonical reviewer preamble](REVIEWS.md#29-canonical-reviewer-preamble-cs35-c35-1); this stub heading
is retained so existing `#reviewer-dispatch--canonical-preamble` links keep
resolving.

### Post-review validation (CS40 — `harness review-output`)

After the dispatched reviewer returns its markdown output, the orchestrator
MUST validate the output's content shape via `harness review-output` before
recording the verdict in the active CS file's `## Plan-vs-implementation
review` table or in the PR body's `## Review log`. This closes #145 gap #3
(PR #28's reviewer summary-passed YAML / package.json without per-file
enumeration; the linter would have caught that).

The invocation (`--review-output`, `--round`, `--base`/`--head`,
`--prev-head` for `Rn`, the `--repo`/`--pr`/`--reviewer-model` independence
guard, and `--update-pr`), the validated predicates (Analyzed-HEAD line,
per-file enumeration match, finding-row + verdict grammar), and the exit codes
all live in `npx -y github:henrik-me/agent-harness#v0.17.0 review-output --help`. The aggregator
`harness pr-evidence` does NOT include this gate (per C40-8 — it requires the
reviewer-output file, which is unavailable in CI); this is a standalone
orchestrator-side step.

### Sub-agent report shape (mandatory)

Every sub-agent reports back with **exactly** this structure. A report
missing any field is rejected; the orchestrator re-dispatches with the missing
fields explicitly listed.

#### Reporting independence (CS48 / issue #142)

**Self-review carries zero review weight.** Any implementer self-review of
the diff is a debugging aid, not a review-of-record. The orchestrator MUST
dispatch a separate reviewer sub-agent (per REVIEWS.md § Phase 2) whose model
differs from every implementer model used in the CS. The `harness review <pr>` CLI obtains the rubber-duck review; do not
pre-empt that step or present implementer self-review as review evidence.

Required final report field: `IMPLEMENTER MODEL USED` (the model-id(s)
materially used for the sub-agent's work), so the orchestrator can update the
CS sub-agent ledger and the PR-body `## Model audit` table.

```
STATUS: complete | partial | blocked
SUMMARY: <one paragraph>
IMPLEMENTER MODEL USED: <model-id(s) materially used for this work; used by the CS sub-agent ledger and PR-body ## Model audit>
FILES CHANGED:
  - <path> (created | edited | deleted) — <one-line why>
SELF-CHECKS RUN:
  - <check name>: pass | fail (<details if fail>)
DECISIONS MADE:
  - <decision> — rationale
ESCALATIONS (orchestrator action required):
  - <issue> — recommended path
LEARNINGS CANDIDATES:
  - <category>: <problem>: <finding>: <evidence>
NEXT STEPS (if partial/blocked):
  - <what's needed to complete>
```

### Per-CS sub-agent ledger

The active CS file's `## Tasks` table records each dispatched sub-agent.
The `Notes` column uses a fixed format (per
[TRACKING.md § CS file structure](TRACKING.md#cs-file-structure)):

```
agent-id=<id> | role=<short role> | report-status=<value> | learnings=<N>
```

**`report-status` lifecycle:**

| Value | Meaning |
|---|---|
| `pending` | Slot reserved at claim time, not yet dispatched (initial value). |
| `dispatched` | Sub-agent invoked; awaiting completion notification. |
| `complete` | Sub-agent reported back successfully (matches `STATUS: complete`). |
| `partial` | Sub-agent reported partial completion; orchestrator decides next step. |
| `blocked` | Sub-agent cannot proceed; orchestrator escalates or re-dispatches. |

`learnings` is the integer count of learning candidates surfaced. Use `0`
for "none surfaced"; `-` is invalid.

Example row:

```
| Author harness.config.schema.json | done | sub-agent | agent-id=cs02-schema-config | role=schema-author | report-status=complete | learnings=1 |
```

### Post-completion verification

After each parallel-dispatch wave the orchestrator verifies disk state before
declaring the wave complete ([LRN-017](LEARNINGS.md#lrn-017)):

- `git status --short` — only the expected files appear; nothing unexpected.
- Per-file size check — compare reported line/byte counts against actual
  on-disk counts. A sub-agent's report describes what it *intended* to leave;
  file races leave stubs that pass their own unit tests but have none of the
  rich APIs the report claims.
- Spot-check claimed APIs — `grep` for key exported symbols or function names.

If the on-disk state contradicts the report, the work was lost to a file race.
Re-dispatch with a recovery briefing OR accept the simpler version with an
explicit deferral note in the CS file.

### Review fix-round heuristic ([LRN-047](LEARNINGS.md#lrn-047))

When GPT-5.5 review surfaces findings after a dispatch wave:

- **(# findings) × (# affected files) ≤ ~6:** handle inline by the
  orchestrator in the same session.
- **> ~6:** dispatch a dedicated fix-round sub-agent
  (e.g. `cs<NN>-fixes-r1`).

Budget **≥3 review rounds** for any user-facing CS surface (CLI flags, help
text, platform portability). Even "thin wrapper" CLIs generate 5–10 findings
per round ([LRN-031](LEARNINGS.md#lrn-031)). Engine code with strict safety
invariants may require 5–8 rounds ([LRN-024](LEARNINGS.md#lrn-024)).

### Progress observability

- `background` sub-agents notify on completion; use `read_agent`
  (with `wait: true` once notified) to retrieve the structured report.
- Use `list_agents` to poll only when actively blocked on a result.
- The orchestrator does **not** dispatch sub-agents speculatively — every
  dispatch maps to a parallelisation-table entry in the active CS plan.

### Orchestrator availability invariant

The orchestrator must remain available to receive and act on user instructions
at all times. Treat delegation as the default: any task the orchestrator could
plausibly delegate to a sub-agent — including out-of-CS hotfixes, one-off doc
edits, single-file follow-ups, and post-CS cleanups — means the orchestrator
should delegate unless (a) the work is so small that dispatch overhead exceeds
the work, (b) the orchestrator must serialize the change with imminent
sub-agent dispatch, or (c) the user explicitly asked the orchestrator to do it
directly.

When in doubt, dispatch. The orchestrator's primary job is coordination,
triage, user responsiveness, and review-loop steering; implementation work is
secondary when it would block those responsibilities.

### Sub-agent progress reporting

**Progress reporting (required):** every dispatch must require the sub-agent to
emit a one-line update after each owned-file commit, or after each owned-file
edit batch when the briefing prohibits commits, and after any tool invocation
that takes more than 5 minutes. Each update states the current subtask,
approximate completion percentage, and blockers if any.

Silence longer than 15 wall-minutes without an update is a stall signal. The
orchestrator should check the agent, re-brief, re-dispatch, or escalate rather
than letting a silent background task consume the coordination slot invisibly.

### Reviewer dispatch via `harness review` (CS52)

For content PR review rounds, run the combined review orchestrator instead of
hand-stitching the rubber-duck prompt, Copilot engagement, polling, and PR-body
evidence updates.

**What:** validate the target PR, refuse workboard-only or fork PRs, enforce the
reviewer-model independence invariant, emit the manual MVP rubber-duck prompt,
optionally trigger/poll Copilot, and idempotently update `## Review log` plus
`## Model audit`. **How:** run `npx -y github:henrik-me/agent-harness#v0.17.0 review --help` for the full
flag set (`--dry-run` to preview the round, `--no-poll` to dispatch only,
`--rubber-duck-only` for local review without Copilot, `--copilot-only` for a
Copilot retry after a valid local Go row exists, plus `--model` / `--round`) and
the operationally-meaningful exit codes (`0` = Go / dispatch accepted, `1` =
No-Go / unresolved Blocking finding, `2` = usage / policy / transport failure).

Do not merge a content PR until the latest row for the current HEAD has a Go
verdict and Copilot review evidence satisfying the A5/A16 ordering gates in
REVIEWS.md.

---

## Copilot engagement procedure (CS35 C35-10, updated CS37 + CS41)

GitHub Copilot review engagement on a content PR (gate A16 in REVIEWS.md
PR-evidence list) is performed locally by the orchestrator using
`harness copilot-engage` (CS41). The CI workflow only VERIFIES
the engagement happened (PR-evidence gate dispatched by
`harness pr-evidence` via `scripts/check-copilot-review.mjs` from CS37);
CI never mutates the PR.

**Spike outcome (CS37, ADR-0004):** the `requestReviews` GraphQL mutation
REJECTS the Copilot reviewer ID with "Could not resolve to User node"
because the Copilot reviewer is `__typename: Bot`, not `User`. The
documented engagement primitive is therefore the REST-backed
`gh pr edit --add-reviewer` invocation that `harness copilot-engage`
wraps — NOT a GraphQL mutation. See the project's ADR-0004 (the Copilot
GraphQL spike) for the full transcript.

### Recommended invocation (CS41+):

Run `npx -y github:henrik-me/agent-harness#v0.17.0 copilot-engage <pr-number>` to request the Copilot
review and poll for a completed review at the PR head. **How:** the full flag
set (`--repo`, auto-detected from the git remote when omitted; `--head`,
`--no-poll`, `--poll-timeout`, `--submitted-after`, `--cache-dir`) and exit
codes live in `npx -y github:henrik-me/agent-harness#v0.17.0 copilot-engage --help`. Key doctrine the
help encodes: by default the poll HEAD is the PR's GitHub `headRefOid` (not the
local checkout, with a warning when they differ); the `--submitted-after` floor
enforces the A5 ordering doctrine so a stale Copilot review predating the latest
local Go cannot satisfy the gate; `--no-poll` returns immediately after the
request (CI split); and fork PRs (`isCrossRepository == true`) exit 2 with the
maintainer-rerun hint (ADR4-6). The engage primitive is the REST-backed
`gh pr edit --add-reviewer copilot-pull-request-reviewer` — never a
`requestReviews` GraphQL mutation, because the Copilot reviewer is a `Bot`, not
a `User`. The CLI resolves its Bot identity via the
`node(id: $id) { ... on Bot { databaseId login } }` GraphQL fragment with the
hardcoded Copilot Bot node ID `BOT_kgDOCnlnWA` (7-day identity cache per C41-2),
required because `user(login: 'copilot-pull-request-reviewer')` returns `null`.
See [LRN-009](LEARNINGS.md#lrn-009) and the project's ADR-0004 § ADR4-2.

The poll predicate is identical to the A5+A16 gate
(`scripts/check-copilot-review.mjs`) so "engage CLI says satisfied" =
"PR-evidence gate says satisfied".

Windows authoring reminder: the harness repo stays LF-clean and BOM-free.
Normalize any PR-body or review-log scratch text before writing tracked files;
`scripts/check-text-encoding.mjs` already respects `.gitignore`, so transient
ignored scratch paths such as `.tmp/` are skipped.

### Manual fallback (only if `harness copilot-engage` is unavailable):

1. Request a Copilot review with the maintainer's `gh` auth:
   ```
   gh pr edit <pr-number> --add-reviewer copilot-pull-request-reviewer
   ```
2. Wait 3–5 minutes; Copilot's review pipeline is asynchronous (typically
   delivers within ~3 minutes per spike S3).
3. Verify the review was submitted AND is on the current HEAD:
   ```
   gh api graphql -f query='
     query($owner: String!, $name: String!, $pr: Int!) {
       repository(owner: $owner, name: $name) {
         pullRequest(number: $pr) {
           headRefOid
           reviews(last: 20) {
             nodes {
               state
               submittedAt
               commit { oid }
               author { __typename ... on Bot { login } ... on User { login } }
             }
           }
         }
       }
     }' -F owner=<owner> -F name=<repo> -F pr=<pr-number>
   ```
   The CS37 verifier `scripts/check-copilot-review.mjs` runs the same
   query and enforces A5 + A16 (state, currency, ordering vs local Go).
4. Address every Blocking finding before merge per REVIEWS.md § 2.7.

Decision authority: step (1) requires maintainer credentials; the
harness CLI MUST run engagement only under the maintainer's `gh` auth,
never under a CI `GITHUB_TOKEN` (which is read-only on fork PRs anyway
per Decision C35-9).

### A5 ordering doctrine (PR #172 reconfirmation, CS40):

Each new HEAD requires a NEW `R` row in the PR body's `## Review log`
section. The latest local Go row's timestamp must be BEFORE the
most-recent Copilot review's `submittedAt`. If you add a Go row AFTER
Copilot has reviewed, you MUST re-engage Copilot (re-run
`harness copilot-engage <pr>`) so a new review lands on the new HEAD.
Wait ~3–4 minutes for the new review then re-run failed CI jobs. The
A5+A16 gate enforces this strict ordering mechanically.

CI implication (ADR4-8): an engage-and-verify workflow run will always
fail the verify step on first execution because the review is delivered
asynchronously after the workflow completes. CS38a CI splits engage
and verify into separate jobs/events (e.g. engage on `pull_request`,
verify on a later `pull_request_review` or scheduled rerun).

Fork PR caveat (ADR4-6): on `pullRequest.isCrossRepository == true`, the
`check-copilot-review` gate exits 2 with a maintainer-rerun hint —
forks cannot self-engage Copilot under their own token. `harness copilot-engage`
mirrors this exit-2 behavior on fork PRs.

### Troubleshooting (CS45):

If `harness copilot-engage` exits with `cache-write-failed` (exit code 5),
the most common cause is a read-only `$HOME/.cache/` (e.g. hardened CI
runner, sandboxed home directory). Override the cache directory with
`--cache-dir <writable-path>` to redirect identity-cache writes to a
location the process can write to.

---

## PR-evidence aggregator (CS36)

`harness pr-evidence` is the **single entry point** that runs the mechanical
PR-state evidence gates against an open PR's commit graph and body markdown.
It exists as a separate subcommand (not folded into `harness lint`) because
PR-state checks need PR context (`--base`, `--head`, `--pr-body`) that
default `harness lint` runs do not have (per CS35 decision C35-17).

### Gates registered

| Gate | Predicate script | Owns |
|---|---|---|
| B1 | `scripts/check-pr-commits.mjs` | Every commit in `<base>..<head>` carries the `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer. |
| A3 | `scripts/check-review-evidence.mjs` | PR body's `## Model audit` rows have no implementer-vs-reviewer model overlap. |
| A4 | `scripts/check-review-evidence.mjs` | PR body's `## Review log` latest `Go` row's `analyzed_head` equals `--head`. |
| A5+A16 | `scripts/check-copilot-review.mjs` | (CS37) Copilot review verifier — confirms `copilot-pull-request-reviewer` (`__typename: Bot`) submitted a review at the current HEAD with state in `{COMMENTED, APPROVED, CHANGES_REQUESTED}` AND submitted-at is after the latest local Go (A5 ordering, ADR4-5). Conditional dispatch: requires `--repo` + `--pr`; skipped with notice otherwise. Forks exit 2 with maintainer-rerun hint per ADR4-6. |
| A6 | `scripts/check-clickstop-plan-review.mjs` | Diff-scoped: any planned/active CS file in the PR diff carries a fresh `## Plan review` row with verdict in `{Go, Go-with-amendments}` (predicate from CS35b, `--files <csv>` invocation per CS36 C36-11). |

A3 and A4 share a single script because they parse the same PR body. A6
re-uses the CS35b predicate; the aggregator computes the diff-scoped file
list (`git diff --name-only $base..$head -- project/clickstops/{planned,active}/`)
and threads it via `--files` so that pre-arc grandfathered files cannot
fail unrelated PRs ([LRN-108](LEARNINGS.md#lrn-108)). A5+A16 is a single
script because both gates share the same GraphQL fetch — exposing them as
two scripts would double the API spend without adding signal (per ADR4-3).

### Canonical local invocation (orchestrator pre-PR sanity check)

```sh
PR_BODY=$(mktemp)
gh pr view <num> --json body --jq .body > "$PR_BODY"
npx -y github:henrik-me/agent-harness#v0.17.0 pr-evidence \
  --base "$(gh pr view <num> --json baseRefOid --jq .baseRefOid)" \
  --head "$(gh pr view <num> --json headRefOid --jq .headRefOid)" \
  --pr-body "$PR_BODY"
```

Exits 0 when all gates pass, 1 on any gate failure, 2 on bad usage.

### Canonical CI invocation (CS38a wiring)

The harness ships a managed workflow template at
`template/managed/.github/workflows/pr-evidence-lint.yml` (added by CS38a).
Consumers opt in via `harness init --enable-review-gates` (writes the
`review_gates` block in `harness.config.json`, migrates
`.github/pull_request_template.md` from the `managed` to the `composed`
file class so consumers can keep custom prose, and prints branch-protection
instructions per C38a-7/8) and the next `harness sync` lands the workflow
in the consumer repo.

The workflow is split into TWO jobs per the project's ADR-0004 (ADR4-8):

- **`read-only-gates`** runs on `pull_request` (`opened`, `synchronize`,
  `reopened`, `edited` per [LRN-100](LEARNINGS.md#lrn-100)) with
  `permissions: { contents: read, pull-requests: read }`. Computes
  `--skip-reasons` from the event payload (workboard-only label,
  `[bot]`-suffix login, fork detection via `head.repo != base.repo`),
  then invokes `node "$HARNESS_DIR/bin/harness.mjs" pr-evidence` with
  `--base $PR_BASE_SHA --head $PR_HEAD_SHA --pr-body /tmp/pr-body.md
  --repo $GH_REPO_FULL --pr $PR_NUM`. This job NEVER mutates the PR.
- **`mutation-engage`** runs on `workflow_dispatch` only, with
  `permissions: { contents: read, pull-requests: write }`. Calls
  `gh pr edit "$PR_NUM" --add-reviewer copilot-pull-request-reviewer`
  per ADR4-2. Engagement and verification MUST live on separate events
  because Copilot delivers reviews asynchronously (~3 min); a single-run
  engage-and-verify will always fail the verify step the first time.

The workflow uses the canonical clone-then-run pattern from
`.github/workflows/harness-checks.yml` — clone the harness repo and invoke
`bin/harness.mjs` directly (NOT `npx harness@<ref>` — `harness` is a private
package and npm 10.8.x's GitFetcher regression makes `npx` invocation flaky). The derive-ref step validates the resolved
ref against the allowlist `^[a-zA-Z0-9._/-]+$` (CS12 R1 — shell-injection
hardening) and uses environment-variable indirection for all interpolation.

CI step is OPT-IN per repository (consumers list
`pr-evidence-lint / read-only-gates` in their branch ruleset's required
status checks). The instruction block emitted by `harness init
--enable-review-gates` is intentionally manual: the harness CLI does not
assume maintainer authority to apply branch rulesets remotely.

### Skip-reasons matrix (CS35 C35-19 / CS36 C36-5)

The aggregator centralises skip semantics so individual gate scripts do not
duplicate skip logic. The caller (CI workflow or orchestrator) computes
skip applicability and passes via `--skip-reasons <csv>`:

| Skip reason | B1 | A3 | A4 | A6 | Notes |
|---|---|---|---|---|---|
| `workboard-only` | skip | skip | skip | skip | Short-circuits to exit 0; used for workboard-only PRs (claim/close-out) per CS35-7. |
| `bot-author` | skip | skip | skip | run | A6 still runs because plan attestation is not author-dependent. |
| `fork-source` | run | run | run | run | Read-only gates remain in force; A16 (CS41) is the gate this reason will skip. |

**Path-derived skip (CS71).** Since CS71, the `workboard-only` evidence-gate
skip is also **path-derived**: a PR whose entire diff is confined to the
workboard path allowlist (`WORKBOARD.md`, `CONTEXT.md`, `LEARNINGS.md`,
`project/clickstops/(planned|active|done)/`) skips the `review-gates` and
`pr-evidence-lint` evidence gates **regardless of label presence**. The
`workboard-only` label is therefore **not required to keep gates green** — a
correctly-shaped, unlabelled workboard PR produces a green first run. The label
**is still required** for `workboard-auto-approve.yml` to auto-merge; a
correctly-shaped, unlabelled workboard PR is green yet will not auto-merge
until labelled (intended).

The harness MUST NOT call `gh pr view` or any other authenticated API to
determine skip applicability — caller computes and passes the CSV. This
keeps `harness pr-evidence` callable from forked PR contexts where the
runner has only `read` permissions (per CS35 C35-9).

### Output modes

- Default: human-readable per-gate sections + a summary line listing
  pass/fail counts.
- `--quiet`: suppresses per-gate output; prints only the summary line.
  Suitable for CI logs that want to surface failure detail only via
  `actions/upload-artifact` of the gate-specific stderr streams.
- `--json`: emits a structured `{gates: [{name, status, exitCode}]}`
  payload to stdout. Suitable for downstream tooling (e.g. PR comment
  renderers added in a future CS).

### Wiring discipline

The `harness lint` aggregator (root linter) MUST NOT register the three
PR-evidence linters. Wiring them into `harness lint` would force every
local lint run to require `--base`/`--head`/`--pr-body`, which is hostile
to the local pre-PR convenience use case (per CS35 decision C35-17). The
PR-evidence linters are dispatched ONLY via `harness pr-evidence`.

---

## Init

`harness init` bootstraps a consumer repo with the harness file-class
manifest, scaffolds `harness.config.json` and `.harness-lock.json`, and
optionally opts the project into the PR-evidence gate set.

### `--enable-review-gates` (CS38a)

Passing `--enable-review-gates` to `harness init` performs three idempotent
operations:

1. **Patches `harness.config.json`** with a `review_gates` block — by default
   `{ enabled: true, copilot_required: true, gate_set: ['B1','A3','A4','A5','A16','A6'] }`.
   The default gate set is the CS37 spike PASS branch — full A5+A16
   enforcement (per the project's ADR-0004, ADR4-1).
   Custom gate sets are accepted via direct config edit; the schema enum
   bounds the vocabulary.
2. **Migrates `.github/pull_request_template.md`** from `managed.files`
   to `composed.files` via `lib/file-class-migration.mjs`. The composed
   override gets `_inherited_class: 'managed'` (records the prior class
   for future audit) and `local_blocks: ['pull-request.review-evidence']`
   (the marker block carrying the `## Model audit` + `## Review log`
   tables that CS37's A5+A16 + CS36's A3+A4 read). Consumers that
   already have local prose in their PR template need to re-add it
   (the marker block is appended; outside-marker prose from the prior
   managed template is preserved as the composed skeleton).
3. **Lands the workflow file** `template/managed/.github/workflows/pr-evidence-lint.yml`
   in the consumer repo on the next `harness sync`.

After completion, the command prints a branch-protection instruction
block. The instruction is intentionally manual — the harness CLI does
NOT silently apply branch rulesets because branch-protection mutations
require maintainer authority that the harness deliberately does not
assume (per C38a-8).

The flag is opt-in (`review_gates.enabled` defaults to `false` in
v0.4.0). The default flips to `true` in v0.5.0 (CS41) once the
`harness copilot-engage` wrapper closes the manual-step gap.

Idempotency: re-invoking `harness init --enable-review-gates` on an
already-migrated repo is a no-op (re-emits the instruction block,
makes no config or filesystem changes).

---

## Sync

`harness sync` updates managed and composed files in a consumer repo from the
pinned harness version recorded in `.harness-lock.json`.

### Previewing an upgrade — `harness upgrade`

`harness upgrade <ref>` is a **read-only preview** of bumping the pinned harness
to `<ref>` (a semver tag, branch, or 40-char SHA). It fetches that ref's
templates and runs a **dry-run** `sync` against the consumer repo, printing the
list of files that would change (per-file action + class) + a change-count
summary. **It never writes** — it is additive over `lib/sync.mjs` (no apply-path
rewrite), so it cannot cause data loss. To apply after reviewing: set
`harness.config.json` `version` to `<ref>` and run `harness sync --mode=apply`
(add `--accept-major` for a major bump per § SemVer
policy). This replaces the previous hand-edit-`version`-then-sync-blind workflow
with a previewable upgrade.

### Modes

| Invocation | Behaviour |
|---|---|
| `harness sync` (or `harness check`) | Check mode (**default**): reports drift and exits non-zero if any file is out of sync; writes nothing. Suitable for CI. |
| `harness sync --mode=apply` | Apply mode: writes updates to disk. |
| `harness sync --mode=dry-run` | Dry-run mode: prints what would change; writes nothing. |

### Flags

- **`--config <path>`** — alternate config file path (default:
  `harness.config.json` in `--cwd`). The aggregator must resolve this path
  once and thread it to every subcommand
  ([LRN-038](LEARNINGS.md#lrn-038)).
- **`--cwd <path>`** — treat `<path>` as the consumer repo root.
  Default: `process.cwd()`.
- **`--accept-major`** — required when the resolved template version is a
  major bump from the pinned version (see § SemVer policy).
- **`--resolved-sha <40hex>`** (apply-mode only) — override **only** the
  recorded `resolved_sha` field in `.harness-lock.json` with a specific
  40-character lowercase hex commit SHA, instead of the SHA derived by the
  provenance chain (§ Lock provenance). It does **not** set `harness_ref`, so
  it is **not** a standalone rescue for an install whose provenance is
  otherwise unresolvable: a real `harness_ref` must still be derivable (from
  the npx/npm cache or a git checkout) or apply fails closed
  (`ESYNC_UNRESOLVED_PROVENANCE`). Removes the post-commit-regenerate ordering
  trap (LRN-070) for CSs that touch templates AND root
  files in the same commit: commit content first, then `harness sync
  --mode=apply --resolved-sha <commit-sha>` records a lock that points at
  the actual content commit. The override is rejected (exit 2) in
  `--mode=check` / `--mode=dry-run` (only apply writes the lock) and
  rejected if the value is not 40-char lowercase hex.
- **`--apply-new`** (CS64b C64b-3) — in apply mode, adopt every harness
  `template/managed/` file absent from the consumer's `managed.files`
  (membership, not disk presence; sentinels such as `.gitkeep` are excluded):
  add the `managed.files` entry and materialize the rendered file. In
  `--mode=check` / `--mode=dry-run` it is detection-only (never mutates, never
  changes the exit code).
- **`--quiet`** (CS64b C64b-3) — suppress the new-managed-file advisory (below);
  errors still go to stderr. (Net-new on `sync` in CS64b — before then
  `harness sync --quiet` errored.)

### Lock provenance (CS82)

Apply mode records the running harness install's identity in
`.harness-lock.json` as `harness_ref` (the symbolic ref — tag / branch / spec)
and `resolved_sha` (the exact 40-char commit SHA). These are derived by an
ordered chain against the **harness install** (never the consumer repo):

1. **npx / npm cache.** The install project's
   `node_modules/.package-lock.json` entry for the harness package
   (`packages["node_modules/<pkg>"]`) carries the authoritative `resolved`
   `git+https://…#<sha>` URL and the requested ref (`from` / spec / `version`).
   This is the source of truth under `npx` / `npm`, which strip the installed
   package's own `.git`.
2. **git self-host.** When the harness runs from its own git checkout (e.g.
   local development), the ref + SHA come from `git describe` / `rev-parse`
   against the install directory.
3. **Fail-closed.** If neither yields a real ref + SHA, apply mode **throws**
   `ESYNC_UNRESOLVED_PROVENANCE` and writes **no** lock — it never persists a
   placeholder (`harness_ref: "unknown"`, an all-zero `resolved_sha`, or
   `version: "unknown"` scaffolds). Scaffold `version`s derive from the
   resolved `harness_ref`, so they are guaranteed non-placeholder too.

**npx vs. checkout guidance.** Run apply from a context where provenance is
resolvable: either a git checkout parked at the intended ref, or an
`npx` / `npm` install whose `node_modules/.package-lock.json` is present. A bare
source tarball with neither will fail closed by design. `--resolved-sha` fixes
only `resolved_sha` once a real `harness_ref` is derivable — it is not a
substitute for a resolvable install (see § Flags).

The fail-closed guard runs in **apply mode only**: `--mode=check` and
`--mode=dry-run` validate file drift, not provenance, so they never start
red-flagging a pre-existing lock that already contains placeholder values.

### New-managed-file reconciliation (CS64b)

`harness sync` (check and default paths) surfaces, alongside drift detection,
every consumer-deliverable `template/managed/` file absent from the consumer's
`managed.files` — closing the [LRN-155](LEARNINGS.md#lrn-155) asymmetry where
sync noticed *changed* managed files but never *new* ones. The advisory is
report-only: it does not change `driftDetected` or the exit code.
`sync --mode=apply --apply-new` adopts the surfaced files (adds each
`managed.files` entry + materializes the rendered file); `--quiet` suppresses the
advisory.

### File-class behaviour

| Class | Sync behaviour |
|---|---|
| **managed** | Overwrite unconditionally with the rendered template. Consumer edits are lost. |
| **composed** | Re-render template sections; splice in preserved local-block contents. Consumer prose outside markers is replaced; block contents are kept verbatim. |
| **seeded** | Create if missing (seed once); skip completely if the file already exists. |
| **excluded** | Never touched (e.g. `README.md` per ADR 0002). Listed in `harness.config.json` `excluded[]`. |

### `review_gates` block currency (CS38a / CS41)

`harness sync` checks the `review_gates` block in `harness.config.json`
against the version pinned in `.harness-lock.json`:

- **v0.4.0 (CS38a):** if `review_gates` is absent, sync emits a WARN
  to stderr advising the consumer to run `harness init
  --enable-review-gates` to opt in. Sync still succeeds (exit 0). The
  warning is suppressed by `--quiet`.
- **v0.5.0 (CS41):** the warn is escalated to an ERROR — sync exits 1
  unless `review_gates` is present (any value, including `enabled: false`).
  Consumers that want to remain opted-out must EXPLICITLY record
  `review_gates: { enabled: false }` to acknowledge the choice. Silent
  absence is no longer a valid state because by v0.5.0 the gates are
  the default expectation, not the exception.

Document this escalation path in CS41's release notes; the v0.5.0
upgrade guide must list the manual edit required for any consumer
that wants opt-out without invoking `harness init --enable-review-gates`.

### Composed file sync invariant

For each composed file, the sync engine:

1. Parses the consumer file and extracts all local-block contents by ID.
2. Renders the template (substituting `{{templating}}` variables from config).
3. Splices preserved block contents back into their marker positions.
4. Writes the result atomically.

If the consumer file contains **non-template, non-block content** not covered
by a `legacy_composed_mapping.json` entry, sync exits non-zero and writes
nothing (fail-closed per ADR 0001 § Legacy-content fail-closed invariant).
Use `harness composed-audit --from-existing-harness` to generate the initial
mapping when migrating an existing file onto the harness.

### Consumer-template genericity invariant

The core onboarding docs shipped to consumers — `INSTRUCTIONS.md`,
`.github/copilot-instructions.md`, `TRACKING.md`, `RETROSPECTIVES.md`,
`READMEGUIDE.md` — must be **repo-agnostic**. Their generic locations
(`template/composed/<doc>` bases and `template/managed/<doc>`) must NOT
contain a harness-internal reference: a bare `LRN-<digits>` or `CS<digits>`
token, a `LEARNINGS.md#lrn-` anchor link, or the (case-insensitive)
`henrik-me/agent-harness` slug. A repo that adopts the harness receives basic,
generic instructions — not references that dangle back into the harness's own
institutional memory. The composed bases are scanned **in full**, including the
default `harness:local-*` block bodies (those ship to consumers verbatim on
first init). The harness self-host keeps its own institutional cross-anchors in
the **rendered repo-root** docs (`INSTRUCTIONS.md`,
`.github/copilot-instructions.md`), which the linter does not target — it scans
only the `template/**` generic sources and is package-name self-host gated. The
`check-consumer-template-genericity` linter (registered in `harness lint`)
enforces this invariant so the genericity cannot silently regress, as it did
when those docs first reached consumers carrying dead harness anchors.

### Consumer-doc clickstop-link durability invariant

A **durable** repository doc — an `ARCHITECTURE.md`, a design note, an
onboarding guide, anything meant to outlive a single clickstop — must never
embed **transient** or **institutional** clickstop artefacts. Both failure
modes below leave a durable doc broken the moment the referenced clickstop
closes out:

1. **No links into a transient `project/clickstops/active/` path.** A clickstop
   file lives under `project/clickstops/active/` only while it is in flight;
   close-out `git mv`s it to `project/clickstops/done/`. A durable doc that
   hard-links an `active/` path — especially a **branch-pinned** absolute
   permalink such as
   `https://github.com/<owner>/<repo>/blob/<branch>/project/clickstops/active/…`
   — therefore 404s as soon as that clickstop is done. Prefer, in order: **no
   link** (name the decision inline); a **commit-SHA permalink**
   (`…/blob/<40-char-sha>/…`, which pins the historical tree and keeps
   resolving after the file moves); or a **stable `project/clickstops/done/`
   pointer** once the clickstop has closed. The `clickstop-link-durability`
   linter (run by `harness lint`) fails on a branch-pinned `active/` permalink
   in a durable doc and passes a SHA-pinned one.

2. **No duplicated clickstop decision tables or provenance tags.** Do not copy
   a clickstop's decision table or its inline `(C<NN>-<n>)` provenance tags
   into a durable doc — those are meaningful only inside the clickstop
   workflow, and duplicated into an architecture doc they rot into unexplained
   noise. Restate the decision in the doc's own voice, or reference a single
   stable pointer, instead.

This invariant holds wherever durable docs are **authored or seeded**,
including the one-time bootstrap that scaffolds a new repository: seed durable
docs from generic skeletons and never fold a live clickstop's transient links
or decision provenance into them.

### Integration testing for templated outputs (LRN-057)

Any change to seeded skeletons or composed templates must be validated with the
init → sync-check integration path: run `harness init` into a fresh consumer
repo, then run `harness --cwd <consumer> sync --mode=check`. The sync check
must exit 0 with `No drift detected` and must not mutate files.

This catches bug classes that lint alone can miss: inline harness markers in
prose, unresolved or malformed template placeholders, and composed-merge edge
cases that only appear when the seeded `harness.config.json` selects the
rendered template set. LRN-057 is the canonical example: individual linters
passed, but sync-check rejected the init-produced OPERATIONS.md because the
composed parser saw marker-like prose end-to-end.

### Composed marker syntax

Local blocks are delimited by HTML comment markers. The `id` attribute must
match `[a-z][a-z0-9.-]*`. Markers must occupy the full line (no inline use).
Nesting is an error. Duplicate IDs are an error. Every `local-start` must
have a matching `local-end`. See ADR 0001 § Composed marker syntax and parser
rules for the full normative parser specification.

To document marker syntax inside a code fence (e.g. in tests or this ADR),
insert a zero-width space (U+200B) immediately after the leading `<` to
prevent the parser from treating the example as a live marker.

### Composed-block edits — consumer vs harness-repo paths

When a CS plan or sub-agent briefing tells you to "edit a composed block",
**do the edit at the consumer-repo path**, not the harness-repo template path.
The two are different files:

| Where you are | What to edit | Path |
|---|---|---|
| **Consumer repo** (e.g. `henrik-me/sub-invaders`) | The materialised composed file at the repo root, between its `<​!-- harness:local-start id=… -->` / `<​!-- harness:local-end id=… -->` markers | `<repo-root>/CONVENTIONS.md`, `<repo-root>/OPERATIONS.md`, `<repo-root>/REVIEWS.md` |
| **Harness repo itself** (`henrik-me/agent-harness`) | The template that generates every consumer's composed file. Edits here propagate to all consumers on next `harness sync`. | `template/composed/CONVENTIONS.md`, `template/composed/OPERATIONS.md`, `template/composed/REVIEWS.md` |

The CS plan template historically used harness-repo-relative paths (e.g.
"edit `template/composed/CONVENTIONS.md`") because those plans were authored
in the harness repo. **In a consumer repo, those paths do not exist.** The
orchestrator briefing template now reminds dispatchers to translate to
consumer-relative paths before sending a sub-agent into a consumer repo.

A sub-agent that finds itself looking for `template/composed/...` inside a
consumer repo should escalate ("the dispatch path appears to reference the
harness repo, not this consumer repo — please clarify") rather than silently
guess. ([SI Finding #6](LEARNINGS.md), CS30.)

### Mid-CS sync policy

Do **not** run `harness sync` mid-CS unless fixing a harness blocker. Running
mid-CS when the harness version has changed may unexpectedly update managed
and composed files. The CLI warns when sync is invoked while a CS branch is
in flight (detected from the active branch name). Major-version syncs require
`--accept-major` to proceed.

### Reusable CI workflow

`harness-checks.yml` is a reusable GitHub Actions workflow (`on: workflow_call`)
that runs `harness lint` in any consumer repo with roughly ten lines of caller
YAML. Callers reference it via:

```yaml
jobs:
  harness-checks:
    uses: henrik-me/agent-harness/.github/workflows/harness-checks.yml@<ref>
    with:
      cli-ref: ''   # optional — leave blank to auto-read harness.config.json
```

**Version-locking model:** the workflow accepts an optional `cli-ref` input.
When blank (the default), an inline shell step reads the `version` field from
the caller repo's `harness.config.json` and uses that as the install ref for
the harness CLI (`npx -y github:henrik-me/agent-harness#<resolved-ref>`).
When `cli-ref` is set explicitly, that value is used instead. This ensures
local `harness lint` and CI always invoke the exact same harness version —
no version skew between developer machines and the CI runner.

The workflow's steps are: checkout (pinned SHA), setup-node 20 (pinned SHA),
derive-ref shell step, `npx -y github:henrik-me/agent-harness#<ref> lint --quiet`.
All third-party `uses:` refs are pinned to 40-character commit SHAs.

#### Resolving the SHA for an `actions/<owner>/<repo>@<tag>` pin

The standard recipe is:

```bash
gh api repos/<owner>/<repo>/git/ref/tags/<tag> --jq .object.sha
```

**SAML-protected orgs (Azure, several enterprises) — fallback:** when an org
enforces SAML SSO on its GitHub App and your CLI token isn't SSO-authorised,
`gh api repos/<org>/...` returns `403`. The standard recipe then breaks for
common pins like `Azure/static-web-apps-deploy@v1`.

Use `git ls-remote` instead — it works against the org's public HTTP endpoint
without authentication and returns the same SHA:

```bash
git ls-remote https://github.com/<owner>/<repo>.git refs/tags/<tag>
# Output:
# <40-char-sha>    refs/tags/<tag>
```

Pipe through `awk '{print $1}'` to get the bare SHA. ([SI Finding #7](LEARNINGS.md), CS30.)

### Drift-detection workflow

`template/managed/.github/workflows/harness-drift.yml` is a managed workflow
template that consumers receive via `harness sync`. It runs weekly (Monday
06:00 UTC, cron `0 6 * * 1`) and on `workflow_dispatch`, detecting when the
consumer repo has drifted from the harness version pinned in
`harness.config.json`.

**Behaviour:**

1. An inline shell step reads `harness.config.json` `.version` to derive the
   install ref.
2. `npx -y github:henrik-me/agent-harness#<ref> sync --mode=check --cwd .` is
   run and its exit code captured explicitly:
   - **exit 0** — no drift; the workflow sets `drift_detected=false` and all
     subsequent apply/PR steps skip cleanly via `if:` conditions.
   - **exit 1** — drift detected; `drift_detected=true` is set.
   - **any other exit code** — the workflow fails loudly (broken install,
     network error, or harness crash — never silently produces a PR in this
     state).
3. On drift: `sync --mode=apply` is run to generate the update, then
   `peter-evans/create-pull-request` (pinned to a 40-char SHA) opens a PR
   whose body explains the drift, links to the harness ref, and lists changed
   files.

The template uses `ae` and `henrik-me` placeholders
for PR reviewer/assignee fields; all YAML scalar values containing
`{{...}}` placeholders are quoted so the unrendered template parses as valid
YAML.

**Critical:** never use bare `npx harness ...` in these workflows — the
harness package is not published to npm. Always use
`npx -y github:henrik-me/agent-harness#<ref>`.

---

## Harvest

### Cadence

- **Weekly:** Monday morning, run `harness harvest` (CS04+) and review
  `LEARNINGS.md`. Disposition any `open` entries.
- **Before-claim (CS04+):** run `harness harvest` before claiming
  (`harness claim CS<NN>` runs it automatically per CS64). It
  surfaces stale `open` learnings tagged `process` or `architectural`, or
  tagged with `claim_area` metadata matching the current CS. Resolve before
  the workboard-claim PR lands.

### Bounded-before-claim invariant

All `open` learnings had to be dispositioned (status `applied`, `obsolete`, or
`deferred` with an explicit `deferred_until` date) before the CS15a public flip.
That invariant is now satisfied in this repository; keep it true before future
public-facing release gates. See `LEARNINGS.md` header for the current status.

### LRN entry format

Each learning entry in `LEARNINGS.md` begins with a YAML frontmatter fence
followed by markdown body sections:

```yaml
id: LRN-<NNN>
date: YYYY-MM-DD
category: tooling | process | architectural | operational | anti-pattern
source_cs: CS<NN>
status: open | applied | obsolete | deferred
tags: [<tag>, ...]
claim_area: <area>          # optional — surfaces entry at claim of matching CS
deferred_until: YYYY-MM-DD  # required when status = deferred
```

Body sections (in order): **Problem**, **Finding**, **Evidence**,
**Disposition**. The schema is `schemas/learning.schema.json`;
`check-learnings.mjs` validates all entries as regression fixtures.

### Learning candidate lifecycle

Learning candidates are surfaced in sub-agent reports under
`LEARNINGS CANDIDATES`. The orchestrator decides whether to elevate each
candidate to a full LRN entry in `LEARNINGS.md`. Every candidate must be
surfaced — no silent decisions. The category `<problem>: <finding>:
<evidence>` format in the report directly maps to the LRN body sections.

### Open-LRN audit

To enumerate `LEARNINGS.md` entries by status (e.g. before a release gate or
during a harvest cadence):

```bash
# All entries by status
grep -E '^status: ' LEARNINGS.md | sort | uniq -c

# Just the open ones (with their IDs)
grep -B 4 '^status: open' LEARNINGS.md | grep '^id: '
```

Each `open` entry needs a status flip to `applied` / `obsolete` / `deferred`
(with `deferred_until: <date>`) before any future public-facing release gate
per the bounded-before-claim invariant above.

### CHANGELOG-on-every-CS-close-out

A CS whose deliverables touch the **distributed harness surface** — the files
that ship to consumers on `harness sync`: `template/`, `lib/`, `scripts/*.mjs`,
`bin/`, `scaffolds/`, `schemas/`, `package.json`, `package-lock.json` — adds
its `[Unreleased]` `CHANGELOG.md` bullet as part of its own close-out, rather
than deferring reconciliation to a retroactive sweep at release-cut time
(LRN-101). CSs that touch only repo-internal artefacts (e.g. `LEARNINGS.md`,
`CONTEXT.md`, `WORKBOARD.md`, clickstop files) are exempt.

`check-clickstop` enforces this mechanically: for an `active/` or (post-cutoff)
`done/` clickstop whose `## Deliverables` section names a distributed-surface
path, the `## Tasks` table must include an explicit CHANGELOG-touch row (a row
mentioning `changelog` together with a verb such as
`touch`/`update`/`entry`/`bullet`/`append`/`add`). Distributed-surface
detection consults the `excluded[]` list in `harness.config.json`: a path that
matches a surface glob but is also an explicit sync exclusion is **not** treated
as distributed surface (it does not ship), so a consumer that excludes, say,
`lib/` will not get CHANGELOG enforcement on `lib/`-touching CSs (intended).
The check is date-grandfathered — `done/` CSs closed before the enforcement
cutoff are never flagged — so it locks in the convention going forward without
retroactively tripping the closed backlog.

---

## SemVer policy

The harness follows [Semantic Versioning 2.0.0](https://semver.org).

### Version bump triggers

| Change type | Bump |
|---|---|
| Breaking config schema change (field removed, renamed, or type changed) | **Major** |
| Removed or renamed CLI flag | **Major** |
| New required config field with no default | **Major** |
| New linter script added | **Minor** |
| New optional config field (backward-compatible addition) | **Minor** |
| New template file added to any class (managed, composed, or seeded) | **Minor** |
| New CLI subcommand added | **Minor** |
| Bug fix with no interface change | **Patch** |
| Documentation or comment clarification, no behaviour change | **Patch** |
| Test-only change | **Patch** |

### Harness update guidance

- **Harness-internal updates** go through their own PR/CS on the harness
  repo. Never fold harness version bumps into a consumer CS.
- **Version mismatch warning:** `harness sync` warns when the installed
  harness version differs from the version pinned in `.harness-lock.json`.
  The warning is informational for Minor/Patch diffs.
- **Major-version sync:** `harness sync` exits non-zero with a descriptive
  message if the resolved template is a major version bump from the pinned
  version. Pass `--accept-major` to override after reviewing the migration
  notes. This prevents silent breakage from schema changes or removed flags.
- **Mid-CS sync:** the CLI warns when sync is invoked while a CS branch is
  in flight. Proceed only when fixing a harness blocker.

### Stub subcommands ([LRN-028](LEARNINGS.md#lrn-028))

Planned-but-unimplemented subcommands must exit **3**, not 0. Exit 0 from a
stub creates a false-positive CI signal — callers cannot distinguish "this
worked" from "this was never implemented". Exit codes:

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Runtime error |
| `2` | Bad invocation (unknown flag or missing required argument) |
| `3` | Planned but not yet implemented |

---

## Dependency-bump adoption

A Dependabot (or any bot / maintenance) dependency PR cannot clear the
review-evidence gates on its own: it carries no `## Model audit` / `## Review
log`, so `copilot-review-attached`, `independence-invariant`, and
`review-log-evidence` all fail against it. This section is the repeatable
procedure for adopting such a bump through those gates on a `deps/<pkg>-<ver>`
branch (the sanctioned shape for dependency/maintenance PRs — see the
branch-naming convention in [INSTRUCTIONS.md](INSTRUCTIONS.md) *(if your consumer syncs it)*). It builds on
existing doctrine rather than restating it: the solo-orchestrator merge path
lives in
[§ Content/release-PR admin-merge (solo-orchestrator reality)](#contentrelease-pr-admin-merge-solo-orchestrator-reality)
(CS59 C59-3), and the related Dependabot-alert readback plus stale-bot-PR /
source-template-propagation audit discipline is captured in
[LRN-081](LEARNINGS.md#lrn-081).

Adopt a dependency bump with these ordered steps:

1. **Re-create the bump on a `deps/<pkg>-<ver>` branch** cut from current
   `origin/main`. If a step allocates a throwaway clone to regenerate the
   lockfile, it MUST go through the shared `lib/disposers.mjs` clone /
   `assertSafeRef` re-creation primitives that CS64b (C64b-2) mandates for any
   verb that allocates a clone — never hand-roll a temp dir or pass an
   unguarded `git` ref.
2. **Tighten the semver range to the patched version and regenerate the
   lockfile** (`npm install`), so the adopted bump is pinned to the fix rather
   than a floating range.
3. **Run the tests and the linter** — `node --test tests/*.test.mjs` and
   `harness lint` must both be green.
4. **Generate the review evidence with `harness review`, not by hand.** The PR
   body needs `## Model audit` + `## Review log` blocks per
   [REVIEWS.md § 2.8](REVIEWS.md#28-pr-body-requirements) (implementer model ≠
   reviewer model). Because a `deps/<pkg>-<ver>` PR has no `cs<NN>/` id and thus
   no clickstop file to parse, `harness review <pr> --implementer-models <csv>`
   sources the implementer set from the flag and/or the PR body's existing
   `## Model audit` on a non-CS branch (C68-3) — so the evidence is produced by
   `harness review`, not hand-authored.
5. **Obtain the independent reviews** — a GPT-5.5 rubber-duck `Go` plus a
   Copilot review — then confirm the review-evidence gates
   (`copilot-review-attached`, `independence-invariant`, `review-log-evidence`)
   are green.
6. **Merge via owner override** — `gh pr merge --squash --admin <pr>`, under the
   narrow solo-orchestrator conditions documented in
   [§ Content/release-PR admin-merge (solo-orchestrator reality)](#contentrelease-pr-admin-merge-solo-orchestrator-reality).
   Do not widen that scope here.
7. **Close / supersede the original Dependabot PR** — it is replaced by the
   `deps/` re-creation.
8. **Verify post-merge `main` is green.**

---

## Release process

The mechanical procedure for cutting a harness release (tag + GitHub Release +
consumer notification). Read [`§ SemVer policy`](#semver-policy) first to pick
the bump size; this section assumes that decision is made. A release is its
own CS — file a `planned_cs<NN>_release-v<x.y.z>` plan and follow the standard
3-PR shape (claim → content → close-out).

> **Mechanized by `harness release` (CS67).** The verb turns the Cut +
> Post-merge steps below into a previewable, two-phase, dry-run-first command.
> **Phase A** — `harness release --version <x.y.z>` (or `--bump <level>`)
> previews the version bump (`package.json` + `package-lock.json`), the CHANGELOG
> `[Unreleased] → [x.y.z]` promotion, and the README pin sweep; `--apply` writes
> the files but never commits/tags/pushes. It refuses a SemVer-inconsistent bump.
> **Phase B** — `harness release --publish --version <x.y.z> --sha <squash-sha>`
> verifies `<squash-sha>`: by default it must be the current `origin/main` HEAD
> (a stale/arbitrary SHA fails); passing `--pr <n>` **switches** the check so
> `<squash-sha>` must instead equal that release PR's squash `mergeCommit.oid`
> (authoritative even if `origin/main` has since advanced) and must not be the PR
> branch head. Then `--apply` creates an **annotated** tag
> (`git tag -a v<x.y.z> <sha> -m "Release v<x.y.z>"` then `git push origin v<x.y.z>`,
> matching § Release process step 9) and the GitHub Release on it (a **draft**
> by default; `--no-draft` to publish immediately) via
> `gh release create <tag> --verify-tag` (release-only, no `--target`),
> idempotently, and files issue-only consumer
> notifications (`--consumer`). Run `harness release --help` for the full flag
> list. The steps below remain the canonical spec and the manual fallback;
> commits, the content PR, and the merge stay explicit orchestrator actions.
> The verb is the **single** creator of the GitHub Release; no workflow drafts a
> duplicate. After a manual tag push, re-running `harness release --publish` still
> creates the Release (Phase B is resumable — it creates only the Release when the
> tag already exists); a fully manual (no-verb) cut creates it by hand
> (§ Post-merge step 10).

### Inputs

- Current pinned version (`package.json` `version` field).
- Target version chosen per `§ SemVer policy` (e.g. `0.8.0`).
- A clean `main` (bootstrap sanity-check passes per `INSTRUCTIONS.md` *(if your consumer syncs it)*).

### Pre-release audit ([LRN-101](LEARNINGS.md#lrn-101))

Before touching version files, audit that `CHANGELOG.md` `[Unreleased]`
matches what actually shipped since the previous tag. The cheap form (per
LRN-101's recommended fix) is a diff-check, not a rebuild:

```bash
git log v<prev>..main --oneline                  # commits since last tag
gh pr list --state merged --base main --limit 30 # PR-level granularity
```

For every distributed-surface CS since `v<prev>` (anything that touched
`lib/`, `bin/`, `schemas/`, `template/managed/`, or `template/composed/`),
confirm a corresponding `[Unreleased]` bullet exists. If `[Unreleased]` is
empty or stale, populate it from the close-out CS files before continuing.
Per CS24, the convention is to add `[Unreleased]` bullets at each CS's
close-out PR, not retroactively at release-cut time — anchor drift between
audit-time HEAD and tag-time HEAD is the failure mode LRN-101 catches.

### State-of-the-world probes ([REVIEWS.md § 2.6c F6](REVIEWS.md))

Before the plan-review verdict on the release CS plan, **probe and record**
the current release state — every plan claim about released/draft tag state
is an F6 fact-claim. The canonical probes:

```bash
# Published AND draft releases (covers stale duplicate drafts — LRN-159):
gh api repos/<owner>/<repo>/releases --jq 'map(select(.tag_name=="v<x.y.z>"))'
gh release list --repo <owner>/<repo> --limit 5
git ls-remote origin refs/tags/v<x.y.z>
```

Stale duplicate drafts (e.g. a draft left behind by a prior partial cut) MUST be
deleted **before** the cut starts:

```bash
gh api -X DELETE repos/<owner>/<repo>/releases/<draft-release-id>
```

Record the probes + their output verbatim in the release CS plan's
`## Background` (or `## Constraints`) so the plan-review attestation has the
F6 evidence subsequent reviewers can audit.

### Cut (content PR)

All file edits land on the `cs<NN>/content` branch:

1. **Bump version files.** Use `npm version` (do **not** edit `package.json`
   by hand — `package-lock.json` must stay in sync):

   ```bash
   npm version <x.y.z> --no-git-tag-version
   ```

   `--no-git-tag-version` is required — the tag is created post-merge on the
   squash SHA (step 8 below), not on the pre-merge branch.

2. **Promote the CHANGELOG.** In `CHANGELOG.md`:
   - Rename `## [Unreleased]` → `## [<x.y.z>] — YYYY-MM-DD` (em-dash, not
     hyphen — repo convention).
   - Prepend a fresh `## [Unreleased]` block with the canonical
     `### Added` / `### Changed` / `### Documentation` / `### Fixed`
     skeleton (sections may be empty).
   - Add the new link reference at the bottom:
     `[<x.y.z>]: https://github.com/<owner>/<repo>/compare/v<prev>...v<x.y.z>`.
   - Update the `[Unreleased]` link reference to compare from the new tag:
     `[Unreleased]: https://github.com/<owner>/<repo>/compare/v<x.y.z>...HEAD`.

3. **Sweep README pins.** In `README.md`, update every `v<prev>` install /
   quickstart example pin to `v<x.y.z>` (the Status paragraph at the top,
   install Option B examples, Quickstart block, and any `LRN-121`-style
   notes that reference the current version). Historical narrative paragraphs
   that document *prior* releases retrospectively are intentionally left at
   their original versions.

4. **Validate.** From the repo root:

   ```bash
   npx -y github:henrik-me/agent-harness#v0.17.0 lint --quiet     # expect: 0 failed
   node --test tests/*.test.mjs        # expect: 0 failed
   ```

5. **Local review.** GPT-5.5 rubber-duck mandatory per
   [§ Plan-vs-implementation review (close-out gate)](#plan-vs-implementation-review-close-out-gate)
   and `INSTRUCTIONS.md § Every CS` *(if your consumer syncs it)*. Record model + timestamp + verdict in
   the PR body's `## Model audit` + `## Review log` sections.

6. **Open the content PR.** Use the standard `pull_request_template.md`.

7. **Engage Copilot + pass CI.** Run `harness copilot-engage <pr>` per
   [§ Copilot engagement procedure](#copilot-engagement-procedure-cs35-c35-10-updated-cs37--cs41).
   Wait for Copilot's review, address every Blocking finding, and re-engage
   on any new HEAD per the A5 ordering doctrine. All required status checks
   must be green before merge.

8. **Squash-merge.** Solo-orchestrator content PRs typically need the
   admin-merge path (next subsection) because the author cannot self-approve
   and Copilot only ever submits `COMMENTED`, never `APPROVED`.

### Post-merge

After the content PR squash-merges to `main`:

9. **Tag the squash SHA.** Capture the squash commit SHA from the merged
   PR (`gh pr view <pr> --json mergeCommit -q .mergeCommit.oid`) and tag it:

   ```bash
   git fetch origin main
   git tag -a v<x.y.z> <squash-sha> -m "Release v<x.y.z>"
   git push origin v<x.y.z>
   ```

   Tag the **squash SHA**, not pre-merge branch HEAD — LRN-101's anchor-drift
   case.

10. **Create + publish the Release.** The `harness release` verb (Phase B)
    creates the **draft** GitHub Release with notes from `CHANGELOG.md`
    `[<x.y.z>]`. For a **manual** (no-verb) cut, extract that section to a file and
    create it by hand: `gh release create v<x.y.z> --verify-tag --draft --notes-file <file>`.
    The draft is intentional ([LRN-121](LEARNINGS.md#lrn-121)) — review it, then
    publish, then confirm exactly one release for the tag
    ([LRN-159](LEARNINGS.md#lrn-159)):

    ```bash
    gh release view v<x.y.z>                 # confirm notes match CHANGELOG
    gh release edit v<x.y.z> --draft=false   # publish
    gh release list --limit 5                # verify Latest = v<x.y.z>
    gh api repos/<owner>/<repo>/releases \
        --jq 'map(select(.tag_name=="v<x.y.z>"))'   # confirm exactly one release for this tag
    ```

    If the API returns more than one release for `v<x.y.z>` (typically a
    stale auto-draft from an earlier partial cut), delete the duplicate per
    `§ State-of-the-world probes` above.

11. **Notify consumers.** Use the issue-only handoff per
    [§ Cross-repo procedures](#cross-repo-procedures) and
    [§ Cross-repo pin-bump PR body checklist (CS54)](#cross-repo-pin-bump-pr-body-checklist-cs54).
    For each known consumer repo, file a tracking issue:

    ```bash
    harness cross-repo open-issue \
        --repo <owner>/<consumer-repo> \
        --title "[harness:cs<NN>] bump pinned harness to v<x.y.z>" \
        --body-file <pin-bump-issue-body.md>
    ```

    The body MUST include the verbatim consumer-side PR body checklist from
    `§ Cross-repo pin-bump PR body checklist`. The CLI is idempotent (matches
    an existing open issue by exact title) and always applies the
    `harness-orchestrator` label.

### Content/release-PR admin-merge (solo-orchestrator reality)

The `main` ruleset (CS15a, [LRN-080](LEARNINGS.md#lrn-080)) requires one
approving review on every content PR. For a solo-orchestrator release the
review-of-record paths are:

- The PR author cannot self-approve.
- The Copilot PR reviewer is engaged per the documented mechanics in
  the project's ADR-0004 (accepted review states
  `{APPROVED, COMMENTED, CHANGES_REQUESTED}` per the CS37 spike). In observed
  harness-repo history, Copilot reviews on content/release PRs have
  consistently landed as `COMMENTED` — not `APPROVED` — so the Copilot
  review attached at HEAD does not satisfy the `required_approving_review_count`
  on its own.

The only merge path is therefore `gh pr merge --admin --squash <pr>`,
exercising the admin-bypass actor configured in the ruleset. This is the
content-PR analogue of the workboard-only admin-bypass fallback documented
above — both rely on the same admin bypass but apply to different surfaces.

**Scope (narrow, by design).** The admin merge on a content/release PR is
permitted **only** when **all** of the following hold:

1. The orchestrator is operating solo (no human co-maintainer is available
   to submit an approving review).
2. The mandatory GPT-5.5 rubber-duck review returned `Go` (or
   `Conditional Go` with all conditions met) at the current HEAD, recorded
   verbatim in the PR body's `## Review log`.
3. The Copilot review is attached at the current HEAD per the A5 ordering
   doctrine — every Blocking finding has been addressed, and the PR's
   `copilot-review-attached` status check is green — but the Copilot review
   did **not** itself produce an `APPROVED` verdict that would clear the
   `required_approving_review_count` on its own.
4. All other required status checks (`review-log-evidence`,
   `independence-invariant`, `review-threads-resolved`, CI build/test) are
   green.

This is **not a general bypass license.** When a human reviewer is
available, the approving review path is mandatory; the admin merge is the
documented escape valve for the structural reality that the Copilot review
attached at HEAD does not satisfy the `required_approving_review_count`
ruleset requirement on its own.

Contrast with the workboard-only admin-bypass fallback
([§ Workboard-only PR admin-bypass fallback](#workboard-only-pr-admin-bypass-fallback)):
that path is bot-automated against an exact path allowlist (CS63 C63-7);
this path is manual and scoped to a single PR after both substantive
reviews have passed.

### Quick-reference cheat sheet

```bash
# 0. Audit (LRN-101 + REVIEWS.md § 2.6c F6)
git log v<prev>..main --oneline
gh api repos/<owner>/<repo>/releases --jq 'map(select(.tag_name=="v<x.y.z>"))'
gh release list --repo <owner>/<repo> --limit 5

# 1-3. Bump (on cs<NN>/content branch)
npm version <x.y.z> --no-git-tag-version
#   then: edit CHANGELOG.md (promote [Unreleased] → [<x.y.z>], new [Unreleased] skeleton, link refs)
#   then: sweep README pins v<prev> → v<x.y.z>

# 4. Validate
npx -y github:henrik-me/agent-harness#v0.17.0 lint --quiet
node --test tests/*.test.mjs

# 5-7. Review + engage Copilot + merge
gh pr create --base main --head cs<NN>/content --title ... --body-file ...
harness copilot-engage <pr>
gh pr merge --admin --squash <pr>          # solo-orchestrator path; see scope above

# 8-10. Tag + publish
SQUASH_SHA=$(gh pr view <pr> --json mergeCommit -q .mergeCommit.oid)
git fetch origin main
git tag -a v<x.y.z> "$SQUASH_SHA" -m "Release v<x.y.z>"
git push origin v<x.y.z>
gh release edit v<x.y.z> --draft=false

# 11. Notify consumers
harness cross-repo open-issue \
    --repo <owner>/<consumer-repo> \
    --title "[harness:cs<NN>] bump pinned harness to v<x.y.z>" \
    --body-file <pin-bump-issue-body.md>
```

---

## Conventions

These conventions apply to all harness scripts and CLI code. Quote the
directly-relevant items verbatim in sub-agent briefings.

### ESM only

All harness scripts use ESM (`import`/`export`) and the `.mjs` extension.
No CommonJS `require()`. No `.cjs` files. Node.js 20+. Use `node --test` for
the test runner (no external test framework).

### Line endings and BOM ([LRN-006](LEARNINGS.md#lrn-006), [LRN-018](LEARNINGS.md#lrn-018))

The `create` tool on Windows writes CRLF regardless of `.editorconfig` LF
settings. Files may also carry a UTF-8 BOM. Required normalization after
creating any text file on Windows:

```js
let content = fs.readFileSync(filePath, 'utf8');
// Strip UTF-8 BOM
if (content.charCodeAt(0) === 0xFEFF) content = content.slice(1);
// Normalize CRLF → LF
content = content.replace(/\r\n/g, '\n');
fs.writeFileSync(filePath, content, 'utf8');
```

All parsers that compare content (composed merge, lock file, doc-schema)
must normalize CRLF and strip BOM in their read step. Using `\r?\n` in
regexes is an acceptable alternative to full normalization in parser contexts.

### Windows `spawnSync` ([LRN-029](LEARNINGS.md#lrn-029))

On Windows, `npm`, `npx`, and other Node-ecosystem wrappers are `.cmd` batch
files, not executables. `spawnSync` or `execFileSync` without `{ shell: true }`
attempts to spawn the wrapper as a binary and returns EINVAL regardless of
whether `'npm'` or `'npm.cmd'` is used as the command name.

Canonical pattern:

```js
import { spawnSync } from 'node:child_process';

const result = spawnSync('npm', ['pack', '--dry-run'], { shell: true });
if (result.status !== 0) { /* handle error */ }
```

Use `{ shell: true }` for **all** npm script invocations. This is the only
safe cross-platform pattern.

### `--help` re-forwarding ([LRN-030](LEARNINGS.md#lrn-030))

A global CLI parser that intercepts `--help` must check whether a subcommand
is also present in the argv slice before printing global help:

```js
// In the global arg parser, before printing global help:
if (argv.includes('--help') && knownSubcommands.has(argv[0])) {
  // --help belongs to the subcommand, not the global invocation
  return dispatchSubcommand(argv[0], ['--help']);
}
```

`harness sync --help` must show sync-specific flag documentation, not global
help. Any global flag added later must apply the same subcommand-context check.

### Explicit `--file` for linters ([LRN-032](LEARNINGS.md#lrn-032))

A `harness <subcommand>` wrapper that invokes a linter script must construct
the consumer-cwd-relative file path explicitly and pass it as `--file`:

```js
// In cmdLint (bin/harness.mjs):
const targetFile = path.join(cwd, 'LEARNINGS.md');
spawnSync(
  'node',
  ['scripts/check-learnings.mjs', '--file', targetFile],
  { shell: true, stdio: 'inherit' }
);
```

Never infer the path from `import.meta.url` inside the linter script. When
the script runs as an installed package dependency, `import.meta.url` resolves
inside the harness package directory, not the consumer repo. The `--cwd` flag
passed to the `harness` CLI defines the consumer boundary; the linter must
receive the consumer-rooted path explicitly.

### `requireValue` arg guard ([LRN-040](LEARNINGS.md#lrn-040))

All linters and CLI commands that take flag values must guard the next token
before consuming it:

```js
function requireValue(args, i, flag) {
  if (!args[i + 1] || args[i + 1].startsWith('-')) {
    process.stderr.write(`${flag}: missing or invalid value\n`);
    process.exit(2);
  }
  return args[++i];
}

// Usage — instead of bare args[i+1]:
case '--file':
  filePath = requireValue(args, i++, '--file');
  break;
```

Bare `if (args[i+1])` is prohibited. It silently consumes the next flag as a
value, producing confusing errors like "file not found: --quiet" with no
indication that argument parsing failed.

### Aggregator config single-source ([LRN-038](LEARNINGS.md#lrn-038))

Aggregator commands that both read config and thread it to child subcommands
must resolve the config path exactly once:

```js
// Resolve once:
const effectiveConfigPath = resolveConfigPath(flags.config, cwd);
const cfg = readConfig(effectiveConfigPath);

// Thread the same variable everywhere — never re-resolve independently:
runChildLinter(['--config', effectiveConfigPath, '--cwd', cwd]);
```

Two separate resolution paths that agree for the default case silently diverge
when a non-default `--config` or `--cwd` is passed by automation.

### Schema is source of truth ([LRN-039](LEARNINGS.md#lrn-039))

Before writing any code that reads `harness.config.json`,
`.harness-lock.json`, or any structured config/lock file:

1. Open the corresponding `schemas/*.schema.json`.
2. Locate the exact field path (e.g. `composed.overrides[file].local_blocks`,
   not `composed_files`).
3. Cross-reference every field access against the schema before writing a
   single line of access code.

Guessing field names from intuition passes unit tests (because the test
fixtures are typically authored against the same guessed name) but fails
integration silently. Two CS06 sub-agents independently hit this: one used
`harness_pin` instead of `version`; the other used `composed_files` instead
of `composed.files`. Both failed only at integration time.

### Stdout/stderr discipline ([LRN-044](LEARNINGS.md#lrn-044))

Scripts that emit a primary artifact to stdout (renderers, exporters) must
maintain a strict channel separation:

- **stdout** — artifact only (clean data channel; suitable for pipe capture).
- **stderr** — progress, status, and warnings (non-quiet mode only).
- **suppressed** — all output except the artifact when `--quiet` is passed.

Mixing progress text on stdout corrupts the artifact for piped callers, even
in `--quiet` mode.

### Fail-closed parsers ([LRN-033](LEARNINGS.md#lrn-033))

Any parser that encounters a malformed structured entry must emit an ERROR
and exit non-zero. Silent `continue` or silent skip violates the fail-closed
invariant and gives false confidence that the document is clean. A block that
contains an `id:` field matching the document's entry-id pattern but fails
YAML parse is not silently dropped — it surfaces as a parse-error result and
the linter emits an ERROR.

### Safety-flag depth ([LRN-045](LEARNINGS.md#lrn-045))

Safety-required flags (e.g. `--redact-required`, `--strict`) must validate
the **substance** of the requirement, not just its surface presence. A flag
named `--redact-required` must verify that the applicable redaction rule
exists and is non-empty — not merely that some config object was loaded. Check
the deepest invariant the flag implies.

### Temp-dir/clone disposer pattern ([LRN-157](LEARNINGS.md#lrn-157), CS64b)

Any new verb (or `lib/` module) that allocates a temp directory or a `git clone`
MUST do so through the shared `lib/disposers.mjs` primitives — `makeTempDir()` /
`withTempDir()` for the provenance-safe paired allocation + idempotent cleanup
(remove only the path you allocated; never path-prefix-guess), and
`assertSafeRef(ref)` for any `--ref` / branch / tag argument before it reaches
`git` (rejects empty, leading-dash, and out-of-allowlist refs — an
argv-injection guard). Never hand-roll an inline `fs.mkdtempSync` + best-effort
`rmSync`. The `tests/cs64b-disposer-pattern.test.mjs` guard fails the build if a
`lib/` module allocates a raw temp dir outside `lib/disposers.mjs`. Reviewers:
flag any new temp-dir/clone allocation or unguarded `git` ref argument that
bypasses these helpers.

### Commit-trailer hook (`install-hooks`)

`npx -y github:henrik-me/agent-harness#v0.17.0 install-hooks` installs an **opt-in** git `prepare-commit-msg`
hook (CS100, [#421](https://github.com/henrik-me/agent-harness/issues/421)) into
the repository's active hooks directory. The hook appends the canonical
`Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer to
a commit message when that exact line is absent — **including on merge commits** —
so the commit-trailers (B1) gate passes by construction and you no longer need to
`git commit --amend` a `git merge` that integrates `main` into a long-running CS
branch (the recurring LRN-018 friction).

- **Opt-in only.** `npx -y github:henrik-me/agent-harness#v0.17.0 init` never installs the hook; it is written
  solely when you run `install-hooks` explicitly.
- **Merge-safe placement.** The trailer is inserted **above** git's comment /
  scissors template (not at end-of-file), so it survives git's message cleanup on
  every source — normal, template, squash, amend, and merge. The skip condition and
  the appended line are byte-for-byte identical to what B1 matches (case-sensitive,
  whole line), so re-runs and amends never duplicate the trailer.
- **Idempotent + safe.** The hook carries a sentinel, so re-running is a no-op
  (`skipped`); a pre-existing **non-harness** `prepare-commit-msg` hook is **refused**
  (exit 1) unless `--force` is passed, which overwrites either.
- **Exit codes.** `0` created / replaced / already-installed; `1` refused (foreign
  hook present — re-run with `--force`) or error (not a git repository); `2` usage
  error (unknown flag).

The harness self-host keeps linear history by rebasing rather than merging, so it
rarely creates the merge commits this hook targets; the hook is primarily a
convenience for consumers whose workflow merges `main` into feature branches.

---

## Local block

The section below is managed by the project team. Edit only the content
**between** the markers. The markers and all content above are managed by
the harness and will be overwritten on the next `harness sync`. The block ID
`operations.project-deploy` must be listed in `harness.config.json` under
`composed.overrides["OPERATIONS.md"].local_blocks`.

<!-- harness:local-start id=operations.project-deploy -->
_(Add project-specific deployment workflow, environment list, secrets handling, etc.)_
<!-- harness:local-end id=operations.project-deploy -->
