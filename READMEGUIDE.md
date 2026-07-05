# Harness READMEGUIDE — How to write your project's README

This guide describes the structural requirements your project's `README.md` must
satisfy. The harness linter (`scripts/check-readme.mjs`) mechanically enforces
every ERROR-level rule listed here; WARNING-level rules are advisory.

> **File class: managed.** This guide is overwritten on every `harness sync`.
> Your `README.md` is _not_ — it is consumer-owned and never touched by sync.

---

## Why this matters

A well-structured README is the first thing contributors, maintainers, and
automated tooling see when they open your project. It answers three questions in
under sixty seconds:

1. **What does this project do?** (H1 + one-liner)
2. **How do I run it?** (Quickstart)
3. **Where do I go next?** (Architecture, Status, License)

Getting those three things right reduces the time from "I found this repo" to
"I made my first contribution". It also lets the harness CI gate catch structural
rot before it accumulates — a README that's missing its Quickstart is a README
that's already failing new contributors silently.

---

## Required structure

The sections below map directly to what `check-readme.mjs` enforces. Every
ERROR-level item causes the linter to exit with code `1` and blocks CI. Every
WARNING-level item prints a diagnostic but does not fail the build.

### 1 — H1 project name (ERROR)

The **first non-empty line** of the file must be an H1 heading that names your
project.

```markdown
# My Project Name
```

The linter pattern is `/^#\s+\S/` — a `#`, a space, and at least one
non-whitespace character. A blank `#` heading fails; a comment above the heading
fails; a YAML front-matter block above the heading fails.

**Why:** Tooling (GitHub, npm, documentation generators) uses the first H1 as
the canonical project title. Putting anything else first breaks that contract.

---

### 2 — One-liner pitch (ERROR)

Between the H1 and the first H2 there must be **at least one paragraph** that
describes what the project does.

```markdown
# My Project Name

Fast, zero-dependency CSV parser for Node.js.
```

A blockquote, a code block, or any non-heading, non-blank line satisfies this
check. A status badge row by itself does _not_ satisfy it — add a sentence.

**Why:** Search engines, GitHub's repository card, and humans skimming dozens of
repos all rely on the first paragraph for context. An H1 followed immediately by
an H2 gives zero information.

---

### 3a — Quickstart or Getting started (ERROR)

The file must contain an H2 that matches `quickstart` or `getting started`
(case-insensitive). Either form is accepted:

```markdown
## Quickstart
```

```markdown
## Getting started
```

The section should contain, at minimum:

- How to install or acquire the project (e.g. `npm install`, `go get`, clone
  instructions).
- The shortest command sequence that proves it works (e.g. `npm test`, a
  `curl` to a running server, a smoke-test invocation).

Keep it short — three to ten lines of code blocks plus a sentence of prose.
Link to a longer `CONTRIBUTING.md` or `docs/` for complex setup.

**Why:** Without a Quickstart, contributors cannot verify their local environment
is working. This is the single highest-value section for reducing time-to-first-
contribution.

---

### 3b — License (ERROR)

The file must either contain an `## License` heading _or_ include the word `MIT`
somewhere in the file. Both of these satisfy the check:

```markdown
## License

MIT — see [LICENSE](LICENSE).
```

```markdown
This project is released under the MIT License.
```

If your project uses a non-MIT license, use the `## License` heading and name
the license there. The linter only falls back to the `MIT` string as a
convenience — it does not validate that the licence text matches.

**Why:** GitHub's Explore and dependency security tools infer licence from the
README when no `LICENSE` file is present (or when the file is machine-unreadable).
Missing licence information makes adoption harder and may trigger legal review in
enterprise consumers.

---

### 3c — Architecture (ERROR)

The file must contain an `## Architecture` heading _or_ a direct link to
`ARCHITECTURE.md`. Either of the following satisfies the check:

```markdown
## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design.
```

```markdown
For the design, see [ARCHITECTURE.md](ARCHITECTURE.md).
```

The Architecture section (or pointer) should answer: how is the codebase
structured, what are the major subsystems, and where does execution flow? A
two-sentence summary plus a link is enough. Do not copy-paste the entire
ARCHITECTURE into the README.

**Why:** New contributors spend disproportionate time reconstructing mental models
that experienced contributors carry in their heads. A single pointer eliminates
that cost without requiring the README to stay in sync with every design change.

---

### 3d — Status (ERROR)

The file must contain a `## Status` heading _or_ a direct link to `CONTEXT.md`.
Either of the following satisfies the check:

```markdown
## Status

See [CONTEXT.md](CONTEXT.md) for current project state and active work.
```

```markdown
Current state is tracked in [CONTEXT.md](CONTEXT.md).
```

The Status section should convey: is this project active, pre-release, stable,
or deprecated? What is the current version or milestone? Link to `CONTEXT.md`
for detail rather than duplicating it.

**Why:** A README with no status information looks abandoned. Maintainers stop
updating the README once the project stabilises; the Status section is a
deliberate hook that forces that update. It also gives contributors a clear
signal about whether to expect breaking changes.

---

### 4 — Status badges in first 30 lines (WARNING)

The linter looks for at least one Markdown image in the form `![…](…)` within
the first 30 lines of the file. Missing badges produce a `WARNING` — the build
still passes, but the diagnostic is surfaced.

Recommended badges (pick what's relevant):

```markdown
![CI](https://github.com/org/repo/actions/workflows/ci.yml/badge.svg)
![npm version](https://img.shields.io/npm/v/my-package)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)
```

Place badges immediately after the H1 or at the end of the one-liner paragraph,
before the first H2.

**Why:** Badges give at-a-glance health signals — CI green/red, version number,
licence — without requiring a reader to navigate away from the README. They are
especially valuable on GitHub where the rendered README is the first thing a
visitor sees.

---

## Optional sections

The following sections are not enforced by the linter but are strongly
recommended for mature projects:

| Section | Purpose |
|---|---|
| `## Screenshots` / `## Demo` | Visual proof the thing works; reduces setup friction |
| `## Contributing` | Points to `CONTRIBUTING.md`; sets expectations |
| `## Roadmap` | Links to `CONTEXT.md` or an issue board |
| `## Changelog` | Links to `CHANGELOG.md` or release notes |

---

## Good example

A minimal README that passes all ERROR checks and the badge WARNING:

```markdown
# csv-lite

![CI](https://github.com/org/csv-lite/actions/workflows/ci.yml/badge.svg)

Fast, zero-dependency CSV parser for Node.js — handles quoted fields, custom
delimiters, and streaming inputs.

## Quickstart

\`\`\`bash
npm install csv-lite
\`\`\`

\`\`\`js
import { parse } from 'csv-lite';
const rows = parse('a,b\n1,2');
console.log(rows); // [{ a: '1', b: '2' }]
\`\`\`

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for the streaming pipeline design and
the tokeniser state machine.

## Status

Stable — v1.2.0. See [CONTEXT.md](CONTEXT.md) for active work.

## License

MIT — see [LICENSE](LICENSE).
```

What this does right:

- H1 on the first line ✓
- Badge in the first 30 lines ✓
- One-liner pitch paragraph before the first H2 ✓
- `## Quickstart` with install + usage ✓
- `## Architecture` with link to `ARCHITECTURE.md` ✓
- `## Status` with link to `CONTEXT.md` ✓
- `## License` with MIT mention ✓

---

## Bad example

A README that fails multiple checks:

```markdown
<!-- csv-lite — a CSV parser -->

## Installation

npm install csv-lite

## Usage

See the source.
```

What this gets wrong:

- ✗ First non-empty line is a comment, not an H1 → **ERROR**
- ✗ No one-liner paragraph before the first H2 → **ERROR**
- ✗ No `## Quickstart` or `## Getting started` heading → **ERROR**
- ✗ No `## License` or MIT mention → **ERROR**
- ✗ No `## Architecture` or `ARCHITECTURE.md` link → **ERROR**
- ✗ No `## Status` or `CONTEXT.md` link → **ERROR**
- ✗ No badge image in the first 30 lines → **WARNING**

Five errors and a warning. The CI gate rejects this README before a single
human reviewer has to say a word.

---

## Running the linter

```bash
npx -y github:henrik-me/agent-harness#v0.17.0 lint
```

With suppressed per-finding output (summary only):

```bash
npx -y github:henrik-me/agent-harness#v0.17.0 lint --quiet
```

Exit codes:

| Code | Meaning |
|---|---|
| `0` | No errors (warnings are allowed) |
| `1` | At least one validation error |
| `2` | Usage error (e.g. an unknown flag) |

CI runs this check automatically as part of the harness lint aggregate. It is
safe to run locally at any time; it is read-only and makes no changes.

> **Note:** Do not run `check-readme.mjs` against this guide file
> (`READMEGUIDE.md`). The linter is for actual `README.md` files, and this
> guide deliberately shows counter-examples that would fail the checks.

---

## Relationship to `template/seeded/README.md`

When a consumer runs `harness init` in an empty repository, the sync engine
checks for a `README.md`. If none is found, it copies `template/seeded/README.md`
into the consumer as a starting skeleton. That file is pre-structured to pass all
linter checks out of the box.

On every subsequent `harness sync` the consumer's `README.md` is **never
overwritten** — it is consumer-owned (seeded file class). Changes the consumer makes are
preserved indefinitely. Changes the harness makes to `template/seeded/README.md`
do not propagate to existing consumers.

This guide, by contrast, **is** overwritten on every `harness sync` (managed file
class). Think of this file as the specification the linter is built against, not
as the README skeleton consumers edit.

---

_Managed by agent-harness — do not edit directly. Changes belong in
`template/managed/READMEGUIDE.md` in the harness repository._
