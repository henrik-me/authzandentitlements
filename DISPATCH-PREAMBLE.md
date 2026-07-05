# DISPATCH-PREAMBLE

> **Managed file** — do not edit by hand. Harness sync overwrites this
> file on every `harness sync` run. Local changes will be lost. See
> [`harness.config.json`](harness.config.json) for project-specific settings.

Canonical **source of truth** for the sub-agent briefing preamble that
`npx -y github:henrik-me/agent-harness#v0.17.0 dispatch` machine-extracts and emits verbatim (CS86 C86-2).
The orchestrator never hand-copies the block below — `dispatch` parses the
fenced sections at run time — so an emitted briefing stays byte-identical
to this documented source. Do not hand-edit this file; the `--language-profile`
seam is the per-consumer customization point, not prose edits here.

---

### Mandatory briefing preamble (copy verbatim into every dispatch)

The orchestrator MUST paste the block below verbatim into every sub-agent
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

```text
## CRITICAL PREFLIGHT (LRN-021)

1. Run `git log --oneline -1` NOW and record the SHA. Include it in your
   report as `PREFLIGHT SHA: <sha>`.
2. You MUST NOT commit, push, rebase, reset, `git add`, or `gh pr ...` at
   any point. The orchestrator commits at CS end.
3. At the end of your work, re-run `git log --oneline -1`. It MUST equal
   the preflight SHA. Include it as `FINAL SHA: <sha>`.
4. Run `git status --short` and include the output in your report. Only
   your owned files should appear; nothing must be staged.
5. State literally in your report: "No commit was created."

## File ownership (LRN-016)

OWN EXCLUSIVELY — you may read AND write only the files listed in the
Identity + scope section of this dispatch. You MUST NOT modify, rename,
or delete any file outside that list. Curiosity reads (grep/view) are
fine; writes are not.

Rationale: parallel sub-agents share a working tree. If two agents write
the same file, the later writer silently overwrites the earlier one's work
with no error or warning. Non-overlapping ownership is the only safe
parallel model (validated across CS03 where stubs silently replaced rich
APIs — see LRN-016).

## Required reading

Read every path listed in the Required reading section of this dispatch.
Do not infer what to read — only the explicit list counts. "Read what you
need" produces silent gaps that surface as integration failures later.

## Conventions to follow

- LF line endings, no BOM. After every file write on Windows, normalize:
  strip BOM if present (first 3 bytes must NOT be 0xEF 0xBB 0xBF), replace
  \r\n with \n. All content comparisons must normalize in the read step.
  (LRN-006, LRN-018, LRN-065)

- No dot-notation placeholders (LRN-049). Use flat keys only:
  `ae` not `{{project.agent_suffix}}`. Dot-notation is not
  supported by the template engine and will be emitted literally.

- Cross-repo path discipline (LRN-105). When a sub-agent operates in a repo
  OTHER than the orchestrator's, every path in the briefing must be rooted
  in the executing repo. For composed-block edits in a consumer repo:
  edit `<consumer-root>/<file>` between `<​!-- harness:local-start id=X -->`
  markers, NOT `template/composed/<file>` (that path only exists in the
  harness repo). Disambiguate any `template/`, `scripts/`, or other
  directory name that exists in both repos with different semantics.

- Fail-closed parsers (LRN-033). Malformed JSON/YAML/etc → clear error
  message to stderr + process.exit(1). NEVER silent default. NEVER let a
  stack trace be the only error signal.

<!-- harness:dispatch-language-conventions -->

## Self-checks before reporting

Run all of the following and include each result in SELF-CHECKS RUN:

1. `git status --short` — only owned files appear; nothing staged.
2. `git log --oneline -1` — must match preflight SHA.
3. Text-encoding + line-ending validation (BOM + line endings; LRN-065,
   LRN-074): `npx -y github:henrik-me/agent-harness#v0.17.0 lint` must exit 0. The encoding check
   runs as part of the lint aggregate over the whole cwd (not just
   modified files); it catches CRLF/bare-\r line endings introduced by
   Windows core.autocrlf or stale editor settings.

<!-- harness:dispatch-language-self-checks -->

## Reporting independence (CS48 / issue #142)

**Self-review carries zero review weight.** Any implementer self-review of
the diff is a debugging aid, not a review-of-record. The orchestrator MUST
dispatch a separate reviewer sub-agent (per REVIEWS.md § Phase 2) whose model
differs from every implementer model used in the CS. The `harness review <pr>` CLI obtains the rubber-duck review; do not
pre-empt that step or present implementer self-review as review evidence.

Required final report field: `IMPLEMENTER MODEL USED` (the model-id(s)
materially used for the sub-agent's work), so the orchestrator can update the
CS sub-agent ledger and the PR-body `## Model audit` table.

## Mandatory report shape

Reports missing any field are rejected; orchestrator re-dispatches with
missing fields explicitly listed.

    STATUS: complete | partial | blocked
    PREFLIGHT SHA: <sha>
    FINAL SHA: <sha>
    SUMMARY: <one paragraph>
    IMPLEMENTER MODEL USED: <model-id(s) materially used for this work; used by the CS sub-agent ledger and PR-body ## Model audit>
    FILES CHANGED:
      - <path> (created | edited | deleted) — <one-line why> — <line count>
    SELF-CHECKS RUN:
      - git status / git log / text-encoding / [other checks]: pass | fail
    DECISIONS MADE:
      - <decision> — rationale
    ESCALATIONS: (none) | <issue> — recommended path
    LEARNINGS CANDIDATES: (none) | <category>: <problem>: <finding>: <evidence>
    NEXT STEPS (if partial/blocked):
      - <what's needed to complete>
```

#### Language profiles

The `## Conventions to follow` and `## Self-checks before reporting` sections
inside the core fence above each end with an injection marker
(`<!-- harness:dispatch-language-conventions -->` /
`<!-- harness:dispatch-language-self-checks -->`). `harness dispatch` replaces
each marker with the matching part of the language profile selected by
`dispatch.language_profile` in `harness.config.json` (default `node`) or the
`--language-profile <name>` override, so a non-Node consumer (e.g. a .NET
project) no longer has to negate Node/ESM/npm conventions in every dispatch.
Each profile below lives in its own ```text fence whose first content line is
`## LANGUAGE PROFILE: <name>`, split by `<!-- harness:profile-self-checks -->`
into a conventions part and a self-checks part. The language-agnostic core
(preflight, file ownership, required reading, fail-closed, report shape) is
emitted for every profile.

```text
## LANGUAGE PROFILE: node

### conventions

- ESM `.mjs` only, Node 20+ stdlib. No CommonJS `require()`, no `.cjs`
  files, no npm dependencies unless explicitly authorized in this dispatch.

- Fresh git worktrees/checkouts need their own `npm install` before running
  dependency-backed harness linters; `node_modules` is gitignored and
  per-checkout, not shared from the parent worktree.

- `requireValue(args, i, flagName)` guard for every value-taking CLI flag
  (LRN-040). Must verify args[i+1] exists AND reject tokens starting with
  `-`, exiting code 2 + usage message. Bare `if (args[i+1])` silently
  consumes the next flag as a value.

- Schema is source of truth (LRN-039). Read `schemas/*.schema.json` BEFORE
  writing any field access against harness.config.json, .harness-lock.json,
  or any other structured config. Do not guess field names.

- Stdout for success output; stderr for errors and warnings (LRN-044).
  `--quiet` suppresses success stdout only. Errors always go to stderr.

- Consumer-root-relative paths (LRN-050). Scripts run from the consumer's
  cwd, not the harness source location. Never use `import.meta.url` or
  `process.cwd()` to resolve consumer-repo files.

<!-- harness:profile-self-checks -->

### self-checks

4. If tests were added/modified: `node --test` — report count delta
   (e.g. "23 → 27 tests; all pass").
5. For any .mjs files authored: `node -c <file>` exits 0.
6. If template files were modified (anything under `template/`),
   `npx -y github:henrik-me/agent-harness#v0.17.0 lint` must exit 0 — the lint aggregate includes the
   templates linter (LRN-049/050/051: no dot-notation placeholders, no
   relative-up paths, no self-referencing TODO/FIXME tokens in PR-template
   files).
```

```text
## LANGUAGE PROFILE: dotnet

### conventions

- C#/.NET 8+ on the .NET SDK toolchain. A new service or library owns its
  `.csproj` together with its solution `.sln` entry and, once introduced,
  `Directory.Packages.props` as a single ownership bundle, so the project
  file, solution registration, and central-package pins move together
  (issue #423 / the CS10 Aspire-service ownership finding).

- NuGet central package management: declare each package version once in
  `Directory.Packages.props` (`<PackageVersion Include="..." Version="..." />`)
  and reference it from a `.csproj` with a version-less
  `<PackageReference Include="..." />`. Do not pin versions per project.

- Argument parsing, file layout, and tooling follow .NET/C# idioms rather
  than the Node-profile equivalents; honor the project's established analyzer
  settings (nullable reference types, warnings-as-errors) instead of
  introducing new ones.

- Fail-closed parsing still applies (agnostic doctrine restated for the .NET
  toolchain): malformed JSON/config → a clear stderr error + non-zero exit,
  never a silent default.

<!-- harness:profile-self-checks -->

### self-checks

4. `dotnet build` — the affected projects/solution build with no errors.
5. `dotnet test` — report the pass/fail count (e.g. "42 passed, 0 failed").
6. `dotnet format --verify-no-changes` — formatting + whitespace conform
   (non-zero exit if any file would be reformatted).
```
