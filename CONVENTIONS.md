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
  To satisfy this by construction — including on **merge** commits, which is the easy case
  to forget — run `npx -y github:henrik-me/agent-harness#v0.16.0 install-hooks` once to install the opt-in
  `prepare-commit-msg` hook (CS100); it appends the trailer above when the exact line is
  absent. The hook is opt-in (never auto-installed) and leaves a pre-existing hook untouched
  unless `--force`. See [OPERATIONS.md § Commit-trailer hook](OPERATIONS.md#commit-trailer-hook-install-hooks).
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
### Language + build

- **.NET 10 / C#**, ASP.NET Core minimal APIs + .NET Aspire; EF Core 10 + Npgsql on
  Postgres. **Central Package Management**: every `<PackageVersion>` lives in
  `Directory.Packages.props`; `.csproj` `<PackageReference>` entries omit `Version`.
- `Directory.Build.props` sets **`TreatWarningsAsErrors=true`** — build **0 warnings**.
  LF line endings, no BOM (the harness text-encoding gate rejects CRLF/BOM).

### Fail-closed authorization + entitlements (security)

Authorization and entitlement code is security-critical — apply these at **authoring**
time; they have repeatedly been review blockers (LRN-011, LRN-017):

- **Never trust caller-supplied security attributes.** Derive tenant / branch / owner /
  maker / checker from the **trusted** source — the loaded resource row or the validated
  token (`sub`, `tenant` claim) — never from the request body. A caller may not act as
  another subject; bind maker/checker/tenant to the token.
- **Fail closed on every gate.** A missing/unknown claim, an unreachable dependency, an
  unknown key, a malformed payload, or a decision-service error must **deny**, never
  allow: missing tenant claim → 403; entitlements/PDP unreachable → deny (503); unknown
  feature/module/policy key → disabled/deny **without** consulting a downstream provider
  (the local catalog is the source of truth); malformed input → deny + clear error, never
  a silent default.
- **Distinguish transient failures from business denials.** A decision endpoint returns
  **2xx allow/deny** for business outcomes and a **5xx (503)** for transient/infrastructure
  failures, so a fail-closed caller maps the 5xx to "unavailable → deny" rather than
  mislabeling it as a business decision (e.g. a quota-store retry-exhaustion is a 503, not
  a 429 "quota exceeded").
- **Defense in depth.** Token/scope/role checks are an **outer** gate over domain
  invariants (maker-checker, SoD, tenant scoping), which still enforce independently —
  never the only line of defense.
- **Client sentinels are non-deserializable.** Fields that signal a *local* fail-closed
  state on a typed-client result (e.g. `IsUnavailable`, a sentinel `Reason`) must be
  `[JsonIgnore]` so a wire payload can never inject them; only the local `Unavailable(…)`
  factory sets them.
- **Emit audit-ready decision events.** Every authz/entitlement decision emits a structured
  event with stable, matchable fields — the decision-type/outcome **values** are lower-cased
  for stable matching; ingestion may be deferred (Audit.Service, CS13) but emission is not.

### Concurrency (Postgres + EF Core)

- **Hard capacity caps use a pessimistic per-subject lock, not `Serializable` + retry.** For a
  `count → check → insert` capacity cap (seats today; and new grant/capacity work such as JIT
  grants) enforce it atomically with a Postgres **advisory transaction lock** —
  `SELECT pg_advisory_xact_lock(hashtextextended(<id>, 0))` issued inside the EF transaction
  via `db.Database.ExecuteSqlInterpolatedAsync(…)`. It serializes (blocks) rather than
  conflict-retrying, so it does not thrash/500 under contention; a `Serializable`-isolation +
  retry loop exhausts and 500s (LRN-015, verified to 30-way concurrency on seat assignment).
- **Decide-once / last-writer races use `xmin` optimistic concurrency.** Map the Postgres
  system column with `entity.Property<uint>("xmin").IsRowVersion()`
  (`UseXminAsConcurrencyToken()` was removed in Npgsql 10 rc1); verify the generated SQL adds
  no physical column (LRN-004). **Approve/reject** (maker-checker decide-once) surfaces a
  **409** on the losing writer instead of last-writer-wins. **Quota-consume** instead uses an
  `xmin` **retry** loop (re-read + re-evaluate) and, on sustained-contention retry exhaustion,
  fails closed with a transient **503** (never a 200 business deny or a 429), per the
  fail-closed convention above (LRN-017).
<!-- harness:local-end id=conventions.project -->
