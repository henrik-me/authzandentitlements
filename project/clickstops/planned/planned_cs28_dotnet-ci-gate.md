# CS28 — .NET build/test CI gate (close the cross-CS integration gap)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c3, 2026-07-04 — surfaced by LRN-035 and the CS13↔CS16 `PdpDecisionAuditEvent` merge break (each PR green vs its own base; `main` failed to compile after both merged; fixed reactively in PR #60). Maintainer requested closing the gap.
**Depends on:** none

## Goal

Add .NET build + test to CI so a broken solution is caught automatically instead of silently landing on `main`. Concretely: `pull_request` CI catches breaks visible in a PR against its current (synthetic) merge base, and `push`→`main` CI **detects** cross-CS *logical* merge conflicts — two PRs each green against their own base that don't compile once both land, e.g. CS13↔CS16 — **reactively, after merge**, turning `main` red immediately instead of silently. Fully *preventing* the merge-order class requires require-up-to-date or a merge queue, which need branch protection (out of scope here — see Risks).

## Background

Today the GitHub Actions workflows run **process gates only** (`harness lint`, managed/template drift, review-evidence); `harness startup` runs `node --test` + lint + sync. **Neither builds or tests the .NET solution** — the only .NET correctness check is `dotnet build`/`test` run locally by each agent. There is no `merge_group` (merge queue), so CI never builds the *merged* result. Branch protection is unavailable on this private free-tier repo (`GET .../branches/main/protection` → HTTP 403 "Upgrade to GitHub Pro or make this repository public"), so no check is required-to-merge and merges happen by admin/direct push.

That convention held for single-CS work but has a hole for concurrent cross-CS merges: CS16 expanded `PdpDecisionAuditEvent` (added `DeterminingRule`/`PolicyReferences`/`Narrative`) while CS13 added a test constructing the old shape; both were green in isolation, `main` failed to compile once both merged, and nothing automated caught it (`error CS7036`, fixed in PR #60). See LRN-035.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Where to gate .NET | New `.github/workflows/dotnet-ci.yml` | The one place that can build/test the solution automatically; a red ✗ on PRs is high-signal even when advisory. |
| 2 | Triggers + concurrency | `pull_request` → `main` and `push` → `main`; concurrency grouped per `workflow`+`ref` with `cancel-in-progress` **only** for `pull_request` | PR trigger catches per-PR breaks; push trigger detects merge-order breaks reactively post-merge; PR-only cancellation avoids cancelling `main` diagnostics (preserving the red-`main` signal) and superseded PR runs. |
| 3 | SDK provisioning | `actions/setup-dotnet` reading `global.json`, action pinned to a commit SHA | Installs the exact pinned .NET 10 SDK deterministically; SHA pin satisfies the harness `workflow-pins` gate. |
| 4 | Test scope | `dotnet build` (warnings-as-errors already on via Directory.Build.props) + `dotnet test AuthzEntitlements.sln` | Full-solution build+test; the Docker-dependent OPA/OpenFGA live tests self-skip, so no Docker/services are needed on the runner. |
| 5 | Enforcement level | **Advisory** now; document that required-status + merge-queue / require-up-to-date need branch protection (public repo or GitHub Pro/Team) | Full block-merge enforcement is out of scope until the repo goes public/Pro; captured as a follow-up, not silently dropped. |
| 6 | Posture reversal | Reverse the documented "no .NET in CI" posture; update `CONTEXT.md` and point `LRN-035` at this CS | The maintainer explicitly decided to adopt CI .NET gating (resolves the LRN-035 escalation). |

## Deliverables

- `.github/workflows/dotnet-ci.yml`: `runs-on: ubuntu-latest`; on `pull_request` (→ `main`) and `push` (→ `main`); SHA-pinned `actions/checkout` + `actions/setup-dotnet` (SDK from `global.json`), then `dotnet build AuthzEntitlements.sln` (the restoring build — no separate restore step) followed by `dotnet test AuthzEntitlements.sln --no-build`; least-privilege `permissions: contents: read`; `concurrency` grouped by `${{ github.workflow }}-${{ github.ref }}` with `cancel-in-progress: ${{ github.event_name == 'pull_request' }}` (so `main` push diagnostics are never cancelled).
- `LRN-035` updated to reference CS28 (the gap is being addressed here) with a status/implications note.
- `CONTEXT.md` posture note updated: the "no .NET in CI" state is resolved by CS28 (advisory check); required-to-merge enforcement still pending branch protection.
- Residual limitations documented (advisory-only until branch protection; the merge-order class is only fully closed by a merge queue / require-up-to-date) in the CS notes and CONTEXT.

## User-approval gates

- **Posture reversal (adopting .NET in CI)** — approved by the maintainer on 2026-07-03 ("file a cs and claim it, we need this").

## Exit criteria

- A PR that breaks `dotnet build`/`test` (against its merge base) produces a **failing `dotnet-ci` check**; a merge-order break not visible in any single PR is caught **reactively** by the `push`→`main` run turning `main` red; a green solution produces a passing check.
- The workflow uses **SHA-pinned** actions (harness `workflow-pins` lint green) and runs **without Docker** (live-engine tests self-skip).
- `harness lint` is green; the workflow is observed running (and passing on green `main`) on the content PR that introduces it.

## Risks + open questions

- **.NET 10 is a preview SDK.** `setup-dotnet` must install the exact `global.json` version on the runner — mitigated by pinning via `global-json-file`; if the preview feed is unavailable the job fails loudly (acceptable — it is the gate).
- **Advisory-only until branch protection.** On private free-tier a red check does not block an admin/direct merge; documented, and requires public repo or GitHub Pro/Team to enforce (out of scope here).
- **`push`-on-`main` build is reactive**, not preventive — it flags a broken `main` after merge. Full prevention of the merge-order class needs a merge queue / require-up-to-date (follow-up when the repo is public/Pro).
- **Runtime/cost** — full-solution build+test is ~1–3 min; acceptable. Path-filtering could be added later if noisy.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | yoga-ae-c3 (rubber-duck) | 577ddaa230a2 | 2026-07-04T04:35:33Z | Go-with-amendments | Sound advisory .NET CI; amended: merge-order limitation wording, per-ref concurrency (PR-only cancel), runner OS + restoring-build then --no-build test. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
