# RETROSPECTIVES

> **Managed file** — Do not edit by hand. Canonical source is
> `template/managed/RETROSPECTIVES.md`; the repo root copy is rendered from
> this template during `harness sync`. Changes belong in the template.

This file defines what a *learning* is, how to author one, how the harvest
cadences work, and the rules that govern bounded before-claim prompts. It is
the companion reference for [LEARNINGS.md](LEARNINGS.md).

---

## What is a learning?

A **learning** (identified by an `LRN-NNN` id) is a durable,
project-applicable insight surfaced by completing a CS, task, or plan, that
meets **all three** of the following tests:

1. **Not already captured.** The insight is not a restatement of an existing
   rule in `INSTRUCTIONS.md`, `CONVENTIONS.md`, `OPERATIONS.md`,
   `RETROSPECTIVES.md`, `ARCHITECTURE.md`, `TRACKING.md`, or `REVIEWS.md`.
   If it duplicates existing guidance, it is not a learning — it is either
   noise or a signal that the existing guidance needs updating.

2. **Would change future behaviour.** A future agent or operator who reads
   this learning would approach similar work differently — more carefully,
   with a different tool, or with an explicit precondition check. If the
   finding has no actionable implication, it is not a learning.

3. **Concrete and verifiable.** The learning is grounded in an observable
   event: a failed command, an unexpected API response, a PR that got stuck,
   a two-day delay caused by a missing approval path. Opinion and preference
   are not learnings.

If a finding fails any of these tests, reconsider its form: it might be a
documentation fix filed directly against the relevant file, a new CS to
improve tooling, or simply noise that can be discarded.

When in doubt, file it as `open` and let the next harvest cadence
disposition it. The disposition act itself is calibration data.

---

## Category taxonomy

Every learning carries exactly one `category` value drawn from the following
closed enum (enforced by `schemas/learning.schema.json`):

| Category | When to use |
|---|---|
| `architectural` | A constraint, invariant, or trade-off that was discovered about the system design. Captures _why_ something is the way it is, so it is not accidentally undone. |
| `operational` | A procedural pitfall or recipe: something that must be done in a specific order, something that silently fails if a precondition is skipped, or a repair procedure that had to be figured out the hard way. |
| `tooling` | A tool or library quirk that caused unexpected behaviour — version pinning gotcha, a CLI flag that is silently ignored, a rate limit that only triggers at scale, etc. |
| `process` | A workflow gap that caused waste: a step that was missing from a checklist, an approval path that wasn't documented, a handoff that failed because ownership wasn't clear. |
| `anti-pattern` | An approach that was tried and caused harm or waste. Documents the blast radius so it is not repeated. |

Choosing between categories: prefer the category that best describes the
**root cause**. A tool quirk that caused a process delay is `tooling`; a
missing checklist step that happened to involve a tool is `process`.

---

## Disposition states

Every learning entry carries a `status` value drawn from the following closed
enum (enforced by `schemas/learning.schema.json`):

### `open`

The learning has been recorded but not yet acted on. An `open` entry is a
debt item: it represents something that should change how we work, but hasn't
yet been propagated upstream.

- `open` entries older than 14 days trigger a **warning** from
  `check-learnings.mjs` (the age-out threshold).
- At the weekly harvest cadence, every `open` entry is reviewed.
- At the bounded before-claim cadence, only `open` entries relevant to the
  upcoming CS scope are surfaced.

### `applied`

The learning has been incorporated. The `**Disposition:**` field in the entry
body **must** record where the change landed: the file(s) edited, the CS
filed, the commit SHA, and/or the PR number. `check-learnings.mjs` enforces
the presence of a `**Disposition:**` paragraph for this status.

Typical dispositions:
- "Applied to `CONVENTIONS.md` § Migrations; see commit `abc1234`."
- "Filed as planned `CS<NN>` (`project/clickstops/planned/planned_cs<NN>_*.md`)."
- "Backfilled into `OPERATIONS.md` § Claim checklist."

### `obsolete`

The learning is no longer relevant. Common reasons: the context that produced
it has been removed (the tool was replaced, the workflow was retired), or a
subsequent learning supersedes it. The `**Disposition:**` field **must** note
the reason for obsolescence. `check-learnings.mjs` enforces the presence of
a `**Disposition:**` paragraph for this status.

### `deferred`

The learning is acknowledged but intentionally postponed. `deferred` entries
**must** carry a `deferred_until: YYYY-MM-DD` field (enforced by the schema).
The `**Disposition:**` field should record the reason for deferral and the
conditions that would cause it to be re-evaluated.

After the `deferred_until` date passes, `check-learnings.mjs` emits a
warning. A second consecutive deferral without substantive change causes the
learning to be escalated to weekly-harvest-only and removed from
before-claim prompts, preventing context bloat from repeatedly deferred
entries.

---

## Entry format

Each entry in `LEARNINGS.md` follows this exact structure. The YAML
frontmatter fence is parsed and validated by `check-learnings.mjs` against
`schemas/learning.schema.json`.

````markdown
### LRN-<NNN>

```yaml
id: LRN-<NNN>
date: YYYY-MM-DD
category: architectural | operational | tooling | process | anti-pattern
source_cs: CS<NN>
status: open | applied | obsolete | deferred
tags: [tag1, tag2]
claim_area: <optional — drives before-claim prompts>
deferred_until: YYYY-MM-DD   # required if and only if status=deferred
```

**Problem:** One paragraph describing the situation that produced the
learning. What assumption was being made? What was expected?

**Finding:** One paragraph (two at most) stating the concrete discovery.
What is actually true? What constraint or behaviour was observed?

**Evidence:** Where to find proof: PR #N, commit `<sha>`, log link, or a
reference to another LRN entry.

**Disposition:** (mandatory if status is `applied` or `obsolete`)
What was done with this learning: file(s) edited, CS filed, commit SHA,
or reason for obsolescence.

**Implications carried forward:** (optional)
- Any follow-on notes for future agents or operators.
````

---

## Harvest procedure

Adding a learning entry is a six-step operation:

### Step 1 — Pick the next ID

Open `LEARNINGS.md` and find the highest existing `LRN-NNN` number. The new
entry gets the next integer, zero-padded to three digits minimum. Do not
recycle IDs. `check-learnings.mjs` will warn on sequence gaps and error on
duplicates.

### Step 2 — Write the YAML frontmatter

Author the frontmatter block with all required fields:

```yaml
id: LRN-<NNN>
date: 2026-06-10          # today's date, ISO 8601
category: process         # one of the five canonical values
source_cs: CS<NN>         # the CS or task that surfaced it
status: open              # always start as open
tags: [harvest, checklist]
claim_area: orchestrator-loop   # optional; omit if not relevant to a CS area
```

Required fields: `id`, `date`, `category`, `source_cs`, `status`, `tags`.
Optional fields: `claim_area`, `deferred_until` (only if `status: deferred`).
No additional properties are allowed (the schema uses `additionalProperties: false`).

### Step 3 — Write the entry body

Write the four body sections in order: `**Problem:**`, `**Finding:**`,
`**Evidence:**`, `**Disposition:**`. For new `open` entries, `**Disposition:**`
may be omitted or left as a placeholder — it becomes mandatory when the
status changes to `applied` or `obsolete`.

### Step 4 — Place the entry under the correct section heading

`LEARNINGS.md` uses four top-level `## ` section headings corresponding to
the four status values: `## Open`, `## Applied`, `## Obsolete`, `## Deferred`.
New `open` entries go under `## Open`. The linter warns if entries exist for a
status but the matching heading is absent, and errors if an unrecognised heading
is found.

### Step 5 — Run the linter

```sh
npx -y github:henrik-me/agent-harness#v0.12.0 lint
```

This runs the `LEARNINGS.md` check (among the harness's other linters). Fix any
errors before proceeding. Warnings are advisory; document any intentional
deviation in the entry body.

### Step 6 — Commit and open a PR

Commit `LEARNINGS.md` with a descriptive message. Include the LRN ID in the
commit subject (e.g., `learnings: add LRN-<NNN> — deferred-until enforcement`).
Open a PR against `main` following the standard CS PR procedure in
`OPERATIONS.md`.

---

## Cadences

The harvest runs on two distinct cadences. Both are driven by `harness harvest`
but differ in scope and trigger.

### Weekly — orchestrator-triggered retro

**Trigger:** The orchestrator's weekly retro loop, or a manual `harness harvest --weekly`.

**Scope:** Every entry in `LEARNINGS.md` with `status: open` or
`status: deferred` (including entries whose `deferred_until` date has passed).

**Process:**

1. For each entry, the operator (or orchestrator) chooses one of:
   - **Apply upstream** — edit the relevant file(s) in `INSTRUCTIONS.md` /
     `CONVENTIONS.md` / `OPERATIONS.md` / `ARCHITECTURE.md` / `REVIEWS.md`;
     update `status` to `applied`; fill in `**Disposition:**` with commit SHA.
   - **File a CS** — for tooling or automation gaps that require a dedicated
     change set. Link the CS in `**Disposition:**`; leave `status: open` until
     the CS closes, then flip to `applied`.
   - **Obsolete** — mark `status: obsolete`; fill in `**Disposition:**` with
     the reason.
   - **Defer** — set `status: deferred` and provide an explicit
     `deferred_until` date. Document the reason. A second consecutive deferral
     without substantive progress escalates the entry to weekly-only and
     removes it from before-claim prompts.

2. Run `npx -y github:henrik-me/agent-harness#v0.12.0 lint` to confirm no errors remain.

3. Commit the batch of dispositioned entries as a single `learnings: weekly harvest` commit.

**Age-out rule:** Entries with `status: open` older than 14 days trigger a
`check-learnings.mjs` warning. Entries with `status: deferred` whose
`deferred_until` date has passed trigger a warning as well. Neither is a
hard error, but both indicate harvest debt that should be addressed in the
next weekly cycle.

### Bounded before-claim — pre-CS scope gate

**Trigger:** The `claim` flow for a new CS, before the CS is formally opened.
Runs automatically as part of `harness harvest --before-claim <area>`.

**Scope:** Strictly bounded — see [Bounded prompt rules](#bounded-prompt-rules).

**Process:**

1. The harness identifies all `open` entries whose `claim_area` matches the
   area being claimed, plus all `open` entries categorised as `process` or
   `architectural` (which are cross-cutting and always relevant).
2. If no entries match, the prompt is silent. No action required.
3. If entries match, they are presented in a single batched prompt:
   > "3 learnings are relevant to this claim. Disposition each before
   > proceeding: apply / defer / obsolete / skip-for-this-CS."
4. "Skip-for-this-CS" is a soft bypass — the entry remains `open` but is
   excluded from the current CS's before-claim prompt. It will still appear
   in the next weekly harvest.
5. After disposition, run `npx -y github:henrik-me/agent-harness#v0.12.0 lint` before opening
   the claim PR.

**Invariant:** No CS claim PR should be opened while there are undispositioned
`open` entries matching its scope. This invariant is enforced by the CS
precondition checklist in `OPERATIONS.md`.

---

## Bounded prompt rules

When the before-claim harvest runs, the set of LRN entries surfaced to the
agent or operator **must** be bounded by both of the following filters.
Violating either filter inflates context unnecessarily and can cause the agent
to confuse learnings from unrelated areas with the current CS scope.

### Filter 1 — tag/area match

Only surface entries where **at least one** of the following is true:

- The entry's `claim_area` field exactly matches the area slug being claimed
  (e.g., `claim_area: orchestrator-loop` matches a claim for `orchestrator-loop`).
- The entry's `category` is `process` or `architectural` (these are
  cross-cutting and always relevant to any claim).
- One or more of the entry's `tags` overlap with the tag set associated with
  the upcoming CS in the cs-plan.

Entries that pass none of these tests must be excluded from before-claim
prompts regardless of age.

### Filter 2 — status is `open`

Only surface entries with `status: open`. Entries that are `applied`,
`obsolete`, or `deferred` (even if `deferred_until` has passed) must be
excluded from before-claim prompts.

Rationale: `applied` and `obsolete` entries are closed loops — they are
historical records, not action items. `deferred` entries have an explicit
revisit date and belong to the weekly harvest, not the claim gate.

### Enforcement

The harness CLI enforces these filters automatically in `--before-claim` mode.
If you are running a manual harvest, apply the filters by hand: query
`LEARNINGS.md` for `status: open` entries and then cross-check `claim_area`
and `category` against the upcoming CS area before including any entry in your
review pass.

### Anti-bloat principle

The before-claim prompt must never include more than ~10 entries. If more than
10 `open` entries match the area filters, the weekly harvest cadence has
fallen behind. In that case, run a full weekly harvest first to reduce the
open set, then proceed to the before-claim gate.

---

## What to capture as learnings

Use these as calibration examples:

- "Assumption X turned out to be wrong because of Y constraint." →
  `architectural` or `operational`
- "Tool Z silently drops field Q under condition W." → `tooling`
- "We waited N days for a second review because the approval path wasn't
  documented." → `process`
- "We tried approach A; it caused blast-radius B — don't do this again." →
  `anti-pattern`
- "The CLI flag `--foo` was deprecated in v2 but still accepted silently;
  results were wrong until we pinned v1." → `tooling`
- "The bootstrap checklist didn't include step N; two CSs were opened to fix
  the gap." → `process`

---

## What NOT to capture

- Style or formatting preferences ("I prefer arrow functions").
- Direct restatements of rules already in `INSTRUCTIONS.md`, `CONVENTIONS.md`,
  or any other canonical file — update the source file instead.
- Opinions without evidence or observable event.
- Implementation details that are meaningful only in the context of one
  specific file and would not generalise to any future CS.
- Hypothetical risks that have not yet been observed in practice.

If a candidate finding fails these tests but feels important, consider whether
it belongs as a direct documentation fix, a planned CS, or an addition to an
existing architectural decision record.

---

## Schema reference

The YAML frontmatter of every entry is validated against
`schemas/learning.schema.json` (AJV, JSON Schema 2020-12). The schema:

- **Requires:** `id`, `date`, `category`, `source_cs`, `status`, `tags`
- **Allows (optional):** `claim_area`, `deferred_until`
- **Forbids** any other property (`additionalProperties: false`)
- **Enforces** that `deferred_until` is present if and only if `status` is
  `deferred`
- **Validates** `id` format against `^LRN-[0-9]{3,}$`
- **Validates** `source_cs` format against `^(SI-)?CS[0-9]+[a-z]?$`
- **Validates** `date` and `deferred_until` as ISO 8601 date strings

Run the linter at any time:

```sh
npx -y github:henrik-me/agent-harness#v0.12.0 lint           # runs the LEARNINGS.md check over the repo
npx -y github:henrik-me/agent-harness#v0.12.0 lint --quiet   # summary only
```

Exit code 0 = no linter errors. Exit code 1 = at least one linter error (for
`LEARNINGS.md`, a schema or consistency violation). Warnings (age-out,
deferred-past-due, ID gaps) do not affect the exit code.

---

*This reference is maintained as a harness-managed template.*
