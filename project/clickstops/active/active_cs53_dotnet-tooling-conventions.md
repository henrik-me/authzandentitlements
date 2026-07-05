# CS53 — Codify .NET / Windows build-tooling conventions

**Status:** active
**Owner:** yoga-ae
**Branch:** cs53/content
**Started:** 2026-07-05
**Closed:** —
**Filed by:** yoga-ae-c2 on 2026-07-05 — harvest of open learnings LRN-079/080 (both `source_cs: CS50`, `category: tooling`), whose own dispositions already flagged them as CONVENTIONS.md candidates "at the next harvest". State-of-world probe (2026-07-05): the current on-disk max CS id is 50; `docs/file-planned-cs51-*` (sibling remote branch) and CS54 (the PDP-adapter CS filed in this same harvest, renumbered from CS52) are ahead, so CS53 was chosen with margin above the live filing race.
**Depends on:** none

## Goal

Capture the two recurring **.NET / Windows build-tooling gotchas** surfaced by LRN-079/080 as canonical conventions in the `CONVENTIONS.md` project-local block, so contributors stop rediscovering them the hard way — a failing harness `text-encoding` gate after `dotnet sln add`, and a `CS0246` compile error on a new xUnit test file.

## Background

- CS50 (AppHost application-model smoke test) surfaced two small but recurring .NET/Windows conventions that today live **only** in `LEARNINGS.md`:
  - **LRN-079 (tooling):** `dotnet sln add` on Windows was observed to rewrite `AuthzEntitlements.sln` with **CRLF line endings + a UTF-8 BOM**, which violates the repo's `.gitattributes` LF mandate and fails the harness `text-encoding` gate (part of `harness lint`). The fix is to **re-normalize the `.sln` to LF / no-BOM** before committing (strip a leading `EF BB BF`, replace `\r\n`→`\n`, `[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))`); post-normalization the `git diff` shows only the intended project-registration lines. Apply the same normalization after `dotnet sln remove` as a conservative convention (same tool family).
  - **LRN-080 (tooling):** new xUnit test `.cs` files need an **explicit `using Xunit;`** — the test projects enable `ImplicitUsings`, but that does **not** bring in the `Xunit` namespace, so omitting the using yields `CS0246` (type or namespace `Fact`/`FactAttribute` not found).
- These mirror guidance already present in the repo: `CONVENTIONS.md` already notes that the create/edit tooling writes CRLF and that new text must be normalized to LF/no-BOM (the same `text-encoding` gate), and every existing test suite (e.g. `tests/AuthzEntitlements.Edge.Gateway.Tests/*.cs`) already carries `using Xunit;`. So both learnings belong consolidated as explicit conventions rather than tribal knowledge.
- Both are **documentation-only** captures; the existing solution and test suites already comply, so no code or `.csproj` change is required.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Where the conventions land | The `CONVENTIONS.md` `conventions.project` local block | Both are project-specific build/tooling conventions; the local block is the sanctioned home for repo-specific conventions, edited only between the `harness:local-start id=conventions.project` / `harness:local-end` markers. |
| 2 | `.sln` normalization (LRN-079) | Document: after any `dotnet sln add` / `dotnet sln remove` on Windows, **re-normalize the `.sln` to LF / no-BOM** before committing, and verify the `git diff` contains only the intended registration lines | `dotnet sln add` was observed to re-introduce CRLF + BOM (apply the same rule after `remove` conservatively); the `text-encoding` gate rejects it; normalization keeps the diff clean and the gate green. Generalize the note to any Windows tool that rewrites tracked text. |
| 3 | `using Xunit;` (LRN-080) | Document: every test `.cs` needs an explicit `using Xunit;` even though the test projects enable `ImplicitUsings`; mirror the existing suites | `ImplicitUsings` does not include the `Xunit` namespace; omitting the using is a `CS0246` build break. A one-line convention prevents the repeat error. |
| 4 | Scope boundary | Documentation-only; **no code / `.csproj` / `.sln` changes** in this CS | Pure convention capture; the shipped solution and test suites already comply. |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (cs53-plan-review) | e897a4a4eacf | 2026-07-05T17:23:00Z | Go-with-amendments | Sound; amend unverified `dotnet sln remove` rewrite claim and the imprecise "CS ids ≤50" probe wording. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (cs53-plan-review-r2) | 11a2e1290a18 | 2026-07-05T17:34:00Z | Go | R1 findings resolved; `remove` framed as conservative convention; probe now "current on-disk max CS id is 50"; no new issues. |

## Deliverables

- `CONVENTIONS.md` `conventions.project` local block gains two convention entries: **(a)** after `dotnet sln add` / `dotnet sln remove` on Windows, re-normalize the `.sln` to LF / no-BOM before committing (and verify the diff is only the registration lines); **(b)** every xUnit test `.cs` must include an explicit `using Xunit;` despite `ImplicitUsings`. Edited only between the `harness:local-start id=conventions.project` / `harness:local-end` markers.
- `LEARNINGS.md`: LRN-079 and LRN-080 flip to `status: applied` at this CS's close-out, each `**Disposition:**` citing this CS and the merge commit. (They stay `open`, linked to CS53, until then.)
- No code changes; the solution stays green and `harness lint` passes (including the `text-encoding` gate).

## User-approval gates

- None — documentation-only.

## Exit criteria

- `CONVENTIONS.md` `conventions.project` documents both conventions (`.sln` LF/no-BOM re-normalization after `dotnet sln` edits; explicit `using Xunit;` in test files).
- `harness lint` passes (including `text-encoding`); the full solution stays green.
- LRN-079/080 are dispositioned `applied` at close-out with a commit citation.

## Risks + open questions

- **Composed-file discipline.** `CONVENTIONS.md` is a composed file; the edits must stay strictly within the `conventions.project` local-block markers, or `harness sync` will overwrite them. This is the only real risk.
- **Redundancy.** The `.sln` note overlaps the existing CONVENTIONS text-encoding guidance; frame it as a specific `dotnet sln` corollary rather than a duplicate.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add both conventions to the `CONVENTIONS.md` `conventions.project` local block: (a) after `dotnet sln add` / `dotnet sln remove` on Windows, re-normalize the `.sln` to LF / no-BOM before committing and verify the diff is only registration lines (LRN-079); (b) every xUnit test `.cs` needs an explicit `using Xunit;` despite `ImplicitUsings` (LRN-080). Edit only between the `harness:local-start id=conventions.project` / `harness:local-end` markers. | done | yoga-ae | Content PR #182 (`ec0bd5c`); PVI scope fix PR #183 (`d6549f3`). Documentation-only; no code / `.csproj` / `.sln` change (Decision #4). |
| Close-out: docs + restart state | done | yoga-ae | `CONTEXT.md` CS53 paragraph added; WORKBOARD row removed at close-out. |
| Close-out: learnings + follow-ups | done | yoga-ae | LRN-079/080 flipped to `status: applied` in `LEARNINGS.md`, each `**Disposition:**` citing content PR #182 (`ec0bd5c`) + PVI fix PR #183 (`d6549f3`). |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-05T20:34:00Z
**Outcome:** GO

| Deliverable | Outcome | Evidence |
|---|---|---|
| D1 — both conventions in `CONVENTIONS.md` `conventions.project` block | match | Both entries present in "### Language + build" inside the local-block markers. The `.sln` bullet attributes the observed CRLF+BOM rewrite to `dotnet sln add` and frames `remove` as conservative (within LRN-079 scope, after the PR #183 fix); the `using Xunit;` bullet matches LRN-080. |
| D2 — LRN-079/080 flip to `applied` at close-out | match | Both dispositioned to CS53 and flipped to `applied` in this close-out (status + `**Disposition:**` citing content PR #182 `ec0bd5c` + PVI fix PR #183 `d6549f3`). |
| D3 — no code changes; solution green; `harness lint` passes | match | Content diff touched only `CONVENTIONS.md` (doc-only); `harness lint` 23 passed / 0 failed / 10 skipped. |

**Test-coverage assessment:** sufficient — documentation-only convention capture; no code change, and the `text-encoding` + composed-block gates cover the risk.

**Round history:** R1 (GPT-5.5) NEEDS-FIX on D1 — the `.sln` bullet over-claimed that `dotnet sln remove` was *observed* to rewrite CRLF+BOM, but LRN-079 only observed `add`. Fixed in PR #183 (`d6549f3`) → R2 (GPT-5.5) GO.
