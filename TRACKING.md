# TRACKING

> **Managed file** — do not edit by hand. Harness sync overwrites this file on
> every `harness sync` run. Local changes will be lost. See
> [`harness.config.json`](harness.config.json) for project-specific settings.

Clickstop lifecycle, workboard state machine, and agent identification for this
project. Canonical definition lives in the harness template
(`template/managed/TRACKING.md`); the copy in your repository is kept in sync
automatically.

---

## Clickstop lifecycle

A **clickstop** (CS) is the smallest unit of releasable work in the harness
process. Each CS travels through three lifecycle stages, represented by a
mandatory file (or directory) rename inside `project/clickstops/`:

| Stage | Folder | File / dir prefix |
|-------|--------|-------------------|
| Planned — not yet started | `project/clickstops/planned/` | `planned_cs<NN>_<slug>` |
| In-flight — claimed and active | `project/clickstops/active/` | `active_cs<NN>_<slug>` |
| Completed — merged to main | `project/clickstops/done/` | `done_cs<NN>_<slug>` |

Stage transitions are performed with `git mv`. The rename preserves full history
and makes the lifecycle state visible at a glance.

### Simple form

Most CSs consist of a single markdown file:

```
project/clickstops/planned/planned_cs<NN>_<slug>.md
project/clickstops/active/active_cs<NN>_<slug>.md   ← after claim
project/clickstops/done/done_cs<NN>_<slug>.md        ← after close
```

### Directory form (artifact-bearing CSs)

When a CS produces or carries supporting artifacts (planning docs, baseline
reports, design notes, generated schemas, …), use the **directory form** so
artifacts travel with the CS file across lifecycle renames:

```
project/clickstops/planned/planned_cs<NN>_<slug>/
    planned_cs<NN>_<slug>.md      ← main CS file (always present)
    <artifact-1>
    <artifact-2>
    …
```

After claim (`git mv` the entire directory):

```
project/clickstops/active/active_cs<NN>_<slug>/
    active_cs<NN>_<slug>.md
    <artifact-1>
    …
```

After close:

```
project/clickstops/done/done_cs<NN>_<slug>/
    done_cs<NN>_<slug>.md
    <artifact-1>
    …
```

Both forms are valid. `check-clickstop.mjs` accepts either. Prefer the directory
form whenever the CS ships non-trivial supporting material that reviewers may need
to consult after the CS is done.

### Naming conventions

- CS numbers occupy the `<NN>` field, **zero-padded to two digits**.
- CS numbers are **uppercase in prose/tables** (`CS<NN>`) and **lowercase in
  branches and filenames** (`cs<NN>`).
- Slugs are **kebab-case** (`bootstrap-repo`, `cli-dispatcher`).
- Branch name: `cs<NN>/<slug>`.
- WORKBOARD-only PR branches: `workboard/cs<NN>-claim`,
  `workboard/cs<NN>-close`, etc.

### CS file front-matter

Every clickstop file begins with the following header block:

```markdown
# CS<NN> — <Title>

**Status:** planned | active | done
**Owner:** <agent-id>
**Branch:** cs<NN>/<slug>
**Started:** <ISO-8601 timestamp>
**Closed:** <ISO-8601 timestamp>   (present when done)

## Goal
## Deliverables
## Exit criteria
## Tasks
| Task | State | Owner | Notes |

## Notes / Learnings
```

`check-clickstop.mjs` validates this structure on every lint run.

> **Close-out gate reminder:** every `done/` clickstop file MUST also include a
> `## Plan-vs-implementation review` H2 section with verbatim
> `**Reviewer:**`, `**Date:**`, `**Outcome:**` field labels (see
> [OPERATIONS.md § Plan-vs-implementation review (close-out gate)](OPERATIONS.md#plan-vs-implementation-review-close-out-gate)
> for the canonical skeleton). The skeleton above intentionally omits this
> section because it's filled at close-out, not at file/claim time.

---

## Workboard state machine

`WORKBOARD.md` is the live coordination file. Orchestrating agents update it to
claim, progress, and close CS work items. The state machine for a single CS entry
in the **Active Work** table is:

```
planned  ──(claim)──►  claimed  ──(start work)──►  active  ──(merge PR)──►  done
                                                       │
                                                  (blocker hits)
                                                       │
                                                       ▼
                                                   blocked  ──(blocker resolved)──►  active
```

### State definitions

| State | Meaning |
|-------|---------|
| `claimed` | An agent has reserved the CS; work has not started yet. The WORKBOARD entry exists; the clickstop file is still in `planned/`. |
| `active` | Work is actively in progress. The clickstop file has been renamed to `active/` and a content branch is open. |
| `blocked` | Work cannot proceed (dependency, environment issue, decision needed). The `Blocked Reason` column in WORKBOARD is populated. |
| `done` | The content PR has been merged to main; the clickstop file has been renamed to `done/`. The Active Work row in WORKBOARD is removed (the `done/` directory IS the historical record — WORKBOARD never duplicates it). |

### Claiming a CS

1. Rename the clickstop file from `planned/` to `active/` on a claim branch.
2. Open a small PR tagged `workboard-only` that updates `WORKBOARD.md` only
   (Active Work row: set `State = claimed`, fill in `Owner` and `Branch`).
3. In the public protected phase, eligible workboard-only PRs are auto-merged
   via the workboard bot workflow after its validation gate passes. Before the
   Ruleset/bot path existed, these were user-reviewed small PRs.

### Closing a CS

1. Open the content PR on branch `cs<NN>/<slug>`.
2. After merge, open a close-out PR on branch `workboard/cs<NN>-close` that:
   - Renames the clickstop file from `active/` to `done/`.
   - Removes the WORKBOARD Active Work row for this CS (the `done/` directory is the historical record; WORKBOARD never carries a "recently completed" log).
3. Tag main with the next version if this CS bumps the harness version.

### Task locking

When a task in the **Active Work** table is assigned to an agent ID, no other
orchestrator may claim it. The assignment is a soft lock enforced by convention
(and eventually by `check-workboard.mjs`). There is no automated reclamation in
the proto phase; manual edits only.

---

## Agent identification

Every orchestrating agent has a **unique agent ID** that appears in WORKBOARD.md,
clickstop Owner fields, and commit trailers. The format is:

```
<machine-short>-<repo-short>[-c<N>]
```

This project's `repo-short` is **`ae`** (defined in
[`harness.config.json`](harness.config.json) under `project.agent_suffix`).

### Component breakdown

#### `machine-short`

Derived from `os.hostname()` by taking the **last meaningful lowercase segment**
when the hostname contains separator characters (`-` or `_`):

| Hostname | `machine-short` |
|----------|-----------------|
| `HENRIKM-YOGA` | `yoga` |
| `HENRIKM-OMNI` | `omni` |
| `devbox` | `devbox` |
| `MY_WORKSTATION` | `workstation` |

The canonical implementation lives in `machineShortFromHostname()` in
`bin/harness.mjs`. Split on `/[-_]+/`, filter empty parts, take the last segment.

#### `repo-short`

A short project-defined token configured in `harness.config.json`:

```jsonc
{
  "project": {
    "agent_suffix": "ae"
  }
}
```

Example values: `ah` (agent-harness repo), `si` (sub-invaders repo), `myapp`.

#### `-c<N>` (clone index)

Derived from the **basename of the consumer repo working directory**. Two
patterns are recognised (checked in order):

| Directory basename | Derived suffix | Example agent ID |
|--------------------|---------------|------------------|
| `<repo>_copilot<N>` | `-c<N>` | `yoga-ae-c2` |
| `<repo><N>` (trailing digits) | `-c<N>` | `yoga-ae-c3` |
| anything else | _(omitted)_ | `yoga-ae` |

This allows multiple clones on the same machine (e.g. one clone per Copilot
agent window) to produce distinct IDs without manual configuration.

Canonical implementation: `cloneSuffixFromDir()` in `bin/harness.mjs`.

```js
// Simplified:
const base = path.basename(dirPath);
let m = base.match(/_copilot(\d+)$/i);
if (m) return `c${m[1]}`;
m = base.match(/(\d+)$/);
if (m) return `c${m[1]}`;
return null;
```

### Override env var

To pin `machine-short` without changing the hostname (useful in CI or when
working across machines with different names), set the environment variable:

```
HARNESS_AGENT_AE_MACHINE=<override>
```

The env var name pattern is **`HARNESS_AGENT_<SUFFIX_UPPER>_MACHINE`** where
`SUFFIX_UPPER` is `project.agent_suffix` uppercased. For example, if
`agent_suffix` is `ah`, the override var is `HARNESS_AGENT_AH_MACHINE`.

The `project.agent_env_var` key in `harness.config.json` can override the
default env var name entirely if needed.

`harness whoami --explain` prints the full derivation chain:

```
hostname:       HENRIKM-YOGA
machine-short:  yoga (derived from hostname)
env-var-name:   HARNESS_AGENT_AE_MACHINE
env-var-value:  (not set)
effective-machine-short: yoga
config-suffix:  ae
consumer-cwd:   C:\src\<repo>
clone-suffix:   (none)
agent-id:       yoga-ae
```

### Worked examples

| Clone path | Hostname | Env var | Agent ID |
|------------|----------|---------|----------|
| `C:\src\<repo>` | `HENRIKM-YOGA` | _(not set)_ | `yoga-ae` |
| `C:\src\<repo>_copilot2` | `HENRIKM-OMNI` | _(not set)_ | `omni-ae-c2` |
| `C:\src\<repo>3` | `HENRIKM-YOGA` | _(not set)_ | `yoga-ae-c3` |
| `C:\src\<repo>` | `HENRIKM-YOGA` | `HARNESS_AGENT_AE_MACHINE=ci` | `ci-ae` |

### WORKBOARD Orchestrators table

The **Orchestrators** table in `WORKBOARD.md` registers each active agent. The
required columns are:

| Column | Description |
|--------|-------------|
| `Agent ID` | Derived agent ID (e.g. `yoga-ae`) |
| `Machine` | Raw hostname (e.g. `HENRIKM-YOGA`) |
| `Repo Folder` | Absolute path to the working directory |
| `Status` | `🟢 Active` (seen within 24 h), `🟡 Idle` (24 h–7 d), `⚪ Offline` (>7 d) |
| `Last Seen` | ISO-8601 UTC timestamp of the agent's last WORKBOARD update |

The agent ID derivation is documented here so it can be reproduced manually
without running `harness whoami` — for example when editing WORKBOARD.md by hand
or reviewing a PR from a new contributor.

---

## Further reading

- [`WORKBOARD.md`](WORKBOARD.md) — live coordination board
- [`harness.config.json`](harness.config.json) — project-level harness config
- `harness whoami [--explain]` — CLI derivation tool
- `scripts/check-workboard.mjs` — WORKBOARD structural linter
- `check-clickstop.mjs` — clickstop file structure linter
