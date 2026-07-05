# Copilot Agent Instructions

> **File class:** composed — managed core + one project-local block.
> Do **not** edit the managed-core sections directly. Edit only the content
> inside the `copilot-instructions.harness` local block (see § Local block at
> the end of this file). All managed-core sections are overwritten on every
> `harness sync`.

---

## What this repo uses

This repository is wired to the **agent-harness** — a process framework for
AI-assisted, clickstop-driven development. Two documents govern day-to-day
operation; read both before acting:

- **`OPERATIONS.md`** — claim/dispatch/handoff/sync/harvest procedures.
- **`INSTRUCTIONS.md`** — quick-reference checklist; re-read after every
  `git pull`.

---

## Hard rules (non-negotiable)

### 1 — No-commit preflight when running as a sub-agent

Before writing a single file, record the current HEAD SHA in your report
preamble:

```
PREFLIGHT: HEAD = <sha>  (no commit / push / rebase / reset / gh pr)
```

Do **not** commit, push, rebase, reset, or open a PR unless you are the
designated orchestrator and the CS plan explicitly assigns that action to you.

### 2 — Explicit file ownership

Every sub-agent owns a declared list of files/directories. The briefing
prompt states them under **"File ownership — OWN only"**. You must not
read-then-modify, rename, or delete anything outside that list. Curiosity
reads (grep/view) are fine; writes are not.

### 3 — Never touch `lib/` unless it is your deliverable

`lib/` contains harness runtime code. Sub-agents assigned to template,
doc, or config work must not modify anything under `lib/`. If you discover
a bug in `lib/` while doing your work, surface it as a learning candidate
and escalate — do not fix it in-band.

### 4 — Schema is source of truth

JSON schemas in `schemas/` define the canonical shape of every harness
artefact. If your deliverable conflicts with a schema, fix the deliverable,
not the schema, unless the CS plan explicitly assigns you a schema change.
Run `npx -y github:henrik-me/agent-harness#v0.17.0 sync --mode=check` (validates your config against the
schema) and `npx -y github:henrik-me/agent-harness#v0.17.0 lint` to confirm conformance before reporting
back.

### 5 — Mandatory briefing preamble (orchestrator-side)

The orchestrator MUST paste the canonical sub-agent briefing preamble (emitted
by `npx -y github:henrik-me/agent-harness#v0.17.0 dispatch` from the harness-owned `DISPATCH-PREAMBLE.md`) verbatim into every sub-agent
prompt, including small or "obvious" ones. Reference is not enough — verbatim
paste is the discipline that prevents process steps from being forgotten.
Sub-agents should reject (or surface as a learning candidate) any dispatch
whose prompt does NOT include the canonical preamble.

### 6 — Cross-repo handoff: file issues, never commit

The orchestrator operates directly **only in its own home repository**. For
**any other repository** (including consumer repos that adopt this harness),
the orchestrator MUST NOT commit, push, open branches, or create pull
requests — even via delegated harness-side sub-agents, helper scripts, or
background tasks (no proxy bypass). The consumer-repo agent owns the PR,
validation, and merge path.

**Check-only mode for status questions** (e.g. "is the consumer on the latest
pin?"): use read-only `gh pr list`, `gh issue list`, or `gh api` to inspect
state. If no tracking issue exists for the work in question, idempotently
create exactly one issue labeled `harness-orchestrator` and report its URL.

**No escape hatch.** Even urgent cross-repo work routes through an issue
(see `OPERATIONS.md § Cross-repo procedures`). This is an orchestrator
constraint; the human user can always act directly outside the orchestrator.

**Cross-repo PR body checklist:** when the consumer-repo agent opens a PR
in response to a harness-filed issue (typically a pin bump), the PR body
MUST include the sections your review process requires — at minimum
`## Summary`, `## Changes`, `## Testing`, `## Model audit`, and `## Review
log` — at PR-open time. The consumer's `.github/pull_request_template.md`
cannot be assumed to populate them. See `OPERATIONS.md § Cross-repo
procedures` for the full doctrine and pre-open self-check.

---

## Per-CS loop (summary)

Each clickstop (CS) follows this lifecycle. See `OPERATIONS.md` and
`INSTRUCTIONS.md` for full detail.

1. **Pre-claim** — review `LEARNINGS.md` for stale `open` items tagged
   `process` or `architectural`; disposition before claiming.
2. **Claim** — rename `planned_csNN_*.md` → `active_csNN_*.md`; update
   `WORKBOARD.md`; commit on a `workboard/csNN-claim` branch; open a
   `workboard-only` PR; squash-merge after review.
3. **Branch** — `git checkout -b csNN/<slug>` from `main`.
4. **Dispatch** — identify parallelisable sub-tasks; dispatch sub-agents
   with structured briefings (see `OPERATIONS.md § Sub-agent dispatch`).
5. **Implement** — work only on owned files; validate early and often.
6. **Local review** — rubber-duck with the configured review model before
   opening a PR; record model + timestamp in the PR body.
7. **Open PR** — use `pull_request_template.md`; CI must pass.
8. **Review + merge** — threads resolved; squash-merge.
9. **Plan-vs-implementation review gate** — run before the close-out PR.
   See `OPERATIONS.md § Plan-vs-implementation review (close-out gate)`.
   Record the review in the active CS file's `## Plan-vs-implementation review`
   section. NEEDS-FIX outcome blocks close-out.
10. **Close-out** — rename `active_csNN_*.md` → `done_csNN_*.md`; update
   `WORKBOARD.md` + `CONTEXT.md`; file learnings in `LEARNINGS.md`.
11. **Harvest** — if weekly cadence or pre-claim cadence triggers.

---

## Sub-agent dispatch and reporting

### Briefing (what the orchestrator must provide)

Every sub-agent prompt must include, in order:

1. Identity + scope (role, CS, owned files, what NOT to touch).
2. Mode (`background` for fan-out, `sync` for tiny pre-requisite probes).
3. Required reading (explicit file list — not "read what you need").
4. Conventions to follow (ESM Node 20+, zero runtime deps, `node --test`,
   file-class model, composed-parser strict rules, agent-ID naming).
5. Deliverables (explicit file list + exit criteria).
6. Self-checks (tests, linters, schema validation, cross-link integrity).
7. Decision authority + escalation boundaries.
8. Findings to surface (learning candidates go in the report).
9. Report shape (see below).

For a newly-created git worktree or fresh checkout, the briefing must require
`npm install` in that checkout before dependency-backed harness linters run;
`node_modules` is gitignored and per-checkout, not shared from the parent tree.

### Reporting independence

**Self-review carries zero review weight.** Any implementer self-review of
the diff is a debugging aid, not a review-of-record. The orchestrator MUST
dispatch a separate reviewer sub-agent (per REVIEWS.md § Phase 2) whose model
differs from every implementer model used in the CS. The `harness review <pr>` CLI obtains the rubber-duck review; do not
pre-empt that step or present implementer self-review as review evidence.

Required final report field: `IMPLEMENTER MODEL USED` (the model-id(s)
materially used for the sub-agent's work), so the orchestrator can update the
CS sub-agent ledger and the PR-body `## Model audit` table.

### Required report shape

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

A report missing this structure is rejected; the orchestrator re-dispatches.

---

## Validation entry point

```
harness lint
```

This is the single command that must exit 0 before you report back. It runs
PR-body linting and any other harness-registered checks. If `harness` is not
on the PATH, invoke it via:

```
npx -y github:henrik-me/agent-harness#v0.17.0 lint
```

---

## Branch and commit conventions

- Branch names: `csNN/<slug>` for CS work; `workboard/csNN-<action>` for
  WORKBOARD-only PRs.
- Every commit must include the trailer:
  `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- Squash-merge only; linear history required on protected `main`.

---

## File-class model

| Class | Meaning | Consumer may edit? |
|---|---|---|
| `managed` | Harness-owned; regenerated on `harness sync` | No |
| `composed` | Generated from templates + project config | No (edit config instead) |
| `seeded` | One-time scaffold; harness never touches again | Yes |

This file is `composed`. Treat all files under `template/composed/` the same
way in consumer repos.

---

## Escalation vs. silent decision

If you encounter any uncertainty, deviation, or surprise:

- **Escalate** (put in `ESCALATIONS` section of your report): stack/dep
  choices, schema field additions, anything crossing CS boundaries.
- **Surface as learning candidate** (put in `LEARNINGS CANDIDATES`): process
  friction, gotchas, anything the next agent should know.
- **Never decide silently.** A silent decision that goes wrong costs far
  more to unwind than an escalation that turns out to be trivial.

---

## Local block

The section below is project-local and is preserved across `harness sync`.
Edit only the content **between** the markers. The markers and all content
above are managed by the harness and will be overwritten on the next
`harness sync`. The block ID `copilot-instructions.harness` must be listed in
`harness.config.json` under
`composed.overrides[".github/copilot-instructions.md"].local_blocks`.

<!-- harness:local-start id=copilot-instructions.harness -->
_(Project-local agent instructions — repository-specific cross-repo
procedures, model choices, and institutional citations. Empty by default.)_
<!-- harness:local-end id=copilot-instructions.harness -->
