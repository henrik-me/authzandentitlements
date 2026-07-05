# CS51 — Harness version-pin mismatch guard: doc note + LRN-79

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c3 on 2026-07-05 — surfaced when this session's `startup --pull-ff-only` fast-forwarded onto the already-merged v0.17.0 pin bump (PR #157) mid-command while still invoking the v0.16.0 CLI, producing a cryptic `Template file not found: .../template/managed/DISPATCH-PREAMBLE.md`. Upstream harness gap filed as henrik-me/agent-harness#502.
**Depends on:** none

## Goal

Prevent recurrence of the "running an older harness CLI than the repo's pin" mistake by (a) codifying — in the project-local INSTRUCTIONS.md block — that `harness.config.json` `version` is the authoritative pin, with a recognisable symptom-to-remedy for the post-pull `Template file not found` failure, and (b) recording the finding as a durable learning (LRN-79) linked to the upstream fix (agent-harness#502).

## Background

- This session began at `harness.config.json` = **v0.16.0** (HEAD `f01475a`; verified `.github/copilot-instructions.md` also v0.16.0). The v0.17.0 pin bump (commit `024a6b9`, PR #157, "chore: upgrade harness to v0.17.0") had merged ~10h earlier but was not yet in the local checkout.
- The first `npx ...#v0.16.0 startup --pull-ff-only` fast-forwarded the working tree onto the v0.17.0 config **within the same command**, then ran its bootstrap `sync --mode=check` under the **v0.16.0** CLI. v0.17.0's config declares managed target `DISPATCH-PREAMBLE.md` (first added at `024a6b9`); the v0.16.0 package does not ship that template, so sync failed: `Sync error: Template file not found: ...7390b14.../template/managed/DISPATCH-PREAMBLE.md (required for managed target "DISPATCH-PREAMBLE.md")`. Re-running `startup` with `#v0.17.0` passed (23 passed / 0 failed, no drift).
- **Root cause:** no preflight compares the running harness package version to `harness.config.json` `version`. Verified against the v0.17.0 source: `config.version` is consumed for doc-ref rendering (`sync.mjs`), major-bump detection (`sync.mjs` `semverMajor`), workflow-pin checks (`check-workflow-pins.mjs`), and review-gate identity checks (`bin/harness.mjs`) — but is **never** compared against the running harness package version. The doc `#<ver>` literals are all sync-rendered from `harness.config.json` `version` (`harness.config.json:4`), which is the single source of truth.
- **Upstream:** filed as **henrik-me/agent-harness#502** (suggests a running-CLI-vs-`config.version` preflight, re-exec under the pulled pin, and a clearer error). The upstream fix is tracked there; this CS is the consumer-side mitigation + durable learning only.
- **State-of-the-world probes (2026-07-05):** `gh issue view 502 --repo henrik-me/agent-harness` -> OPEN; `harness.config.json:4` -> `"version": "v0.17.0"`; `gh pr view 157` -> merged, branch `deps/harness-0.17.0` (so v0.17.0 was adopted via a `deps/` maintenance PR, not a consumer clickstop).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Consumer-side mitigation surface | Add a note to the **INSTRUCTIONS.md `instructions.harness` local block** (the sanctioned project-local, sync-preserved block) | The managed core is overwritten on sync; the local block is the only consumer-editable, durable place. The note names `harness.config.json` `version` as authoritative and maps the `Template file not found` symptom to "your CLI is older than the pin — re-run with the pinned version." |
| 2 | Durable learning | File **LRN-79** (`category: tooling`, `status: open`, `source_cs: CS51`) capturing the pin-mismatch failure mode + remedy, linked to agent-harness#502 | Knowledge lives in the repo, not agent memory (INSTRUCTIONS.md doctrine). `category: tooling` per the taxonomy (a version-pinning gotcha). `source_cs` must be a CS id (learning-schema-required); this CS is the legitimate source. |
| 3 | Upstream fix ownership | Do **not** attempt an in-repo harness code fix; track the real fix via agent-harness#502 | This is a consumer repo; `lib/` / harness runtime is not ours. The cross-repo fix is the harness maintainer's; the consumer mitigation is docs + learning. |
| 4 | Scope | **Docs only** — the INSTRUCTIONS.md local-block note + the LRN-79 entry. No code, schema, or workflow changes | The deliverable is a recurrence guard + durable record, not an implementation. |

## Deliverables

- **INSTRUCTIONS.md** — inside the existing `harness:local-start id=instructions.harness` / `harness:local-end` markers, add a short note (verbatim intent):
  > **The authoritative harness pin is `harness.config.json` -> `version`.** Every `npx -y github:henrik-me/agent-harness#<ver> ...` literal in these docs is sync-rendered from it; a version named in a stale session snapshot or cached doc can lag after an in-session pull. Before running any harness command, confirm `<ver>` matches `harness.config.json` `version`. If `startup`/`sync` fails right after a pull with `Template file not found: .../template/managed/<X>.md (required for managed target "<X>")`, that almost always means your CLI is **older** than the repo's pin — re-run with the pinned version. (Upstream fix tracked: agent-harness#502.)
- **LEARNINGS.md** — a new `## Open` entry **LRN-79**:
  - Frontmatter: `id: LRN-79`, `date: 2026-07-05`, `category: tooling`, `source_cs: CS51`, `status: open`, `tags: [harness, version-pin, startup, sync, bootstrap]`, `claim_area: orchestrator-loop`.
  - **Problem:** `startup --pull-ff-only` can fast-forward a harness-pin bump mid-command, then run bootstrap `sync` under the invoking (older) CLI, yielding a cryptic `Template file not found` for a managed target added only in the newer version.
  - **Finding:** `harness.config.json` `version` is the authoritative pin; no running-CLI-vs-config preflight exists (verified v0.17.0 source). Remedy: re-run with the pinned version.
  - **Evidence:** session-start config v0.16.0 (`f01475a`); FF -> v0.17.0 (`024a6b9` / PR #157, adds `DISPATCH-PREAMBLE.md`); v0.16.0 sync failed template-not-found; v0.17.0 sync 23/0. Upstream agent-harness#502.
  - **Disposition:** `open` — the INSTRUCTIONS.md note (this CS) is the consumer mitigation; the upstream preflight fix is tracked via #502. Flip to `applied` at this CS's close-out (record the commit/PR).

## User-approval gates

- None. Docs-only recurrence guard + learning. (An in-repo harness runtime change, if ever wanted, would be a separate CS and is out of scope — the fix belongs upstream per Decision #3.)

## Exit criteria

- INSTRUCTIONS.md `instructions.harness` local block carries the authoritative-pin note; LRN-79 is present in LEARNINGS.md (`source_cs: CS51`, cross-linking agent-harness#502) — filed `open` during implementation and flipped to `applied` at this CS's close-out (per the harvest convention that a learning tracked by its own CS flips to `applied` at close-out, recording where the note landed); `harness lint` green (clickstop, learnings-schema, text-encoding LF/BOM, xref durability). No code, schema, or workflow changes.

## Risks + open questions

- **Low risk** — docs + one learning; no runtime surface.
- **Upstream dependency.** The real fix (a preflight) lives in agent-harness#502; if it ships, LRN-79 can be revisited (the operator note stays useful regardless).
- **Local-block boundary.** Edit strictly between the `instructions.harness` markers — the managed core is overwritten on sync (per the file-class model).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs51-plan-review (rubber-duck) | 73163656e50f | 2026-07-05T16:41:29Z | Go-with-amendments | Verified #502 open, PR157 merged, v0.17.0 pin, DISPATCH-PREAMBLE add, no CLI-vs-config preflight, LRN-79 free/valid; amended Background config.version usage + clarified LRN-79 open->applied. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
