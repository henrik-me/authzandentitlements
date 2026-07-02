# CONVENTIONS

> **File class: composed.** The sections below are managed by the harness and overwritten on
> every `harness sync`. Do not edit them directly — changes will be lost. Project-specific
> conventions belong exclusively in the local block at the bottom of this file.

---

## File naming

- Use **lowercase kebab-case** for all source files and directories: `my-module.mjs`,
  `user-auth/`, `check-composed-blocks.mjs`.
- Test files mirror the module they test, suffixed with `.test`: `composed.test.mjs` tests
  `lib/composed.mjs`.
- Configuration and schema files use kebab-case: `harness.config.json`,
  `harness.config.schema.json`.
- Markdown documents use `SCREAMING_SNAKE_CASE.md` for process docs (`CONVENTIONS.md`,
  `INSTRUCTIONS.md`) and kebab-case for reference docs under `docs/` (`0001-file-classes.md`).
- Place fixtures and test data next to the code or test file that uses them, not in a
  top-level `fixtures/` folder, unless multiple test files share the same fixture.
- Avoid generic names like `utils.mjs` or `helpers.mjs`; prefer names that describe what
  the module does (`format-date.mjs`, `resolve-path.mjs`).

---

## Branch naming

- **Feature / CS work:** `cs<NN>/<slug>` — e.g. `cs08/content`.
- **Workboard-only changes:** `workboard/cs<NN>-<action>` — e.g. `workboard/cs08-claim`,
  `workboard/cs08-close`.
- **Hotfixes:** `fix/<short-slug>` — squash-merged to `main` with a standard commit message.
- **Experiments:** `exp/<short-slug>` — never merged without explicit review and rename.
- Slugs are lowercase kebab-case, ≤40 characters. No personal identifiers, ticket numbers,
  or dates in the slug unless they are genuinely disambiguating.
- Branch names are stable: do not rename a branch after opening a PR against it.

---

## Commit conventions

- **Subject line:** short imperative sentence, ≤72 characters, no trailing period.
  Example: `Add composed-file merge engine`.
- **Body:** one blank line after the subject, then a paragraph explaining *why* the change
  was made. Include context that is not obvious from the diff. Wrap at 72 characters.
- **Trailers:** always include the Co-authored-by trailer on every commit made with agent
  assistance. Per CS35 Decision C35-5 the harness's PR-evidence B1 gate (lands in CS36)
  enforces this on **every commit** in `git log <base>..<head>`, NOT only on the squash
  commit — squashing hides intermediate dirty state and that is exactly what B1 catches.
  ```
  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
  ```
- **Squash-merge only** on `main`. Feature branch history is preserved locally but only
  the squash commit appears in `main`'s log.
- **No force pushes** to `main`. Force-pushing a feature branch before merge is acceptable
  but must be communicated to any co-authors.
- **Atomic commits:** each commit should leave the repo in a buildable, testable state.
  Avoid "WIP" or "fixup" commits on long-running feature branches that will be squashed.

---

## Pull request conventions

- Every PR must reference the clickstop it belongs to (e.g. `CS08`) in the title or body.
- PR title follows the same imperative-subject format as commit messages.
- The PR description includes: **What** changed, **Why** it was needed, **Testing** done,
  and any **Known limitations** or follow-up work.
- All PRs undergo a GPT-5.5 rubber-duck pre-merge review (or a documented fallback) before
  merge — see `REVIEWS.md` for the process. This requirement holds in the private phase
  except via explicit waiver.
- Keep PRs small and focused. A PR should ideally change one logical area; split large
  changes into sequenced PRs with explicit dependencies noted.
- Draft PRs are welcome for early feedback but must be converted to Ready before the
  rubber-duck review step.
- Delete the source branch after merge unless it is a long-lived integration branch.

**PR-evidence skip predicates (CS35 C35-7/8/9):**
- PRs labeled `workboard-only` skip ALL PR-evidence gates (per Decision C35-7) —
  workflow-level `if: !contains(labels.*.name, 'workboard-only')`. Used for the claim
  and close-out PRs that touch only `WORKBOARD.md` + the CS rename.
- Bot-authored PRs (`dependabot[bot]`, `github-actions[bot]`) skip the per-commit
  trailer (B1), per-file enumeration (A2), and stale-diff (A4) gates per Decision
  C35-8 (bot PRs lack the doctrine-required content by construction). The plan-review
  attestation gate (A6, CS35b) and Copilot engagement gate (A16) still apply if a
  Copilot review is explicitly requested.
- Fork PRs run all read-only gates normally; the Copilot mutation gate (CS41) cannot
  run from a fork because `GITHUB_TOKEN` is read-only on fork PRs (per Decision C35-9).
  The mutation gate fails loudly with a maintainer-rerun instruction; do not paper over.

---

## Code style fundamentals

These rules apply across all languages and tool stacks. Language-specific rules belong in
the local block below.

- **Consistency over cleverness.** Prefer readable, conventional code over terse tricks.
  A future reader — human or agent — should understand the intent without tracing context.
- **Small modules.** Target ~100 LOC per module where reasonable. Large modules are a signal
  to split by responsibility, not a rule violation, but they attract extra review scrutiny.
- **Pure functions first.** Prefer pure, side-effect-free functions. Isolate I/O and state
  mutation at the boundary (entry points, command handlers). This makes testing easier and
  logic clearer.
- **No global mutable state.** Module-level constants (frozen objects, regex literals,
  enum-like maps) are acceptable. Module-level variables that mutate at runtime are not.
- **Explicit over implicit.** Prefer explicit function parameters over hidden dependencies.
  Avoid relying on ambient globals, process environment leaks, or module-load side effects.
- **Error handling:** use structured error objects with a `code` string property for
  programmatic errors. Plain `Error` with only a message is acceptable for programmer errors
  (precondition violations). Never swallow errors silently.
- **No commented-out code** in committed files. Remove dead code; if it may be needed, note
  the intent in a comment that explains the trade-off, not the code itself.
- **Comments explain why, not what.** If a comment re-states what the code does, it is
  redundant and should be deleted. Reserve comments for non-obvious decisions, external
  constraints, or known limitations.

---

## Documentation conventions

- All process docs live at the repo root or under `template/` in the harness.
- Use H2 (`##`) for top-level sections and H3 (`###`) for subsections. Do not skip heading
  levels. Do not use H1 inside a document body (the document title is the only H1).
- Cross-links use repo-relative paths: `[ADR 0001](docs/adr/0001-file-classes.md)`. Do not
  use absolute URLs for in-repo links.
- Code, file names, command names, and configuration keys are wrapped in backticks.
- Avoid passive voice in normative statements. "Do X" is clearer than "X should be done."
- Tables are used for comparative or tabular data only. Do not use tables as a layout trick
  for two-column prose.
- Keep line length ≤100 characters in Markdown source where practical. This is not enforced
  by CI but aids diff readability.
- ADRs (`docs/adr/`) follow a fixed structure: title, date, status, context, decision,
  consequences. Do not add sections outside that structure without updating this file.

---

## What goes where

| Path | Contents |
|---|---|
| Repo root | Managed and seeded process docs (`INSTRUCTIONS.md`, `CONVENTIONS.md`, etc.) |
| `template/managed/` | Harness-owned templates overwritten on every sync |
| `template/composed/` | Templates with managed core + local extension blocks |
| `template/seeded/` | Templates seeded once on init; consumer owns thereafter |
| `docs/adr/` | Architectural Decision Records — immutable once accepted |
| `lib/` | Harness library modules (pure ESM, `.mjs`) |
| `bin/` | Harness CLI entry points |
| `scripts/` | Harness development and CI scripts |
| `schemas/` | JSON Schema files for config and lock formats |
| `scaffolds/` | Skeleton files copied during `harness init` |
| `project/clickstops/` | CS lifecycle (`planned/`, `active/`, `done/`) |

Files not listed in `harness.config.json` (under `managed`, `composed`, `seeded`, or
`excluded`) cause `harness sync` to exit non-zero. Every file the harness ships must be
accounted for.

---

## Project-specific conventions

<!-- harness:local-start id=conventions.project -->
_(Add project-specific conventions here. Example: language version, formatter config,
framework conventions.)_
<!-- harness:local-end id=conventions.project -->
