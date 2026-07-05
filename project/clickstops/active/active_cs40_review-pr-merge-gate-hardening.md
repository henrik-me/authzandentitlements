# CS40 — Review & PR merge-gate hardening (bypass-free normal merges)

**Status:** active
**Owner:** yoga-ae-c5
**Branch:** cs40/content
**Started:** 2026-07-05
**Closed:** —
**Filed by:** yoga-ae-c5 — 2026-07-04; follow-up to the branch-protection hardening + CS34: the ruleset only requires build-test + structural-gate, review-evidence gates are advisory, and Dependabot/workboard PRs can't merge without admin bypass. Directions for each decision were chosen by the user (see Decisions). Renumbered to CS40 (above the in-flight arc, with margin) to resolve CS-number collisions with concurrently-filed sibling CSs.
**Depends on:** none

## Goal

Make the "push to main" ruleset actually enforce the harness review discipline so that a compliant content PR merges **without** admin bypass, and give Dependabot/workboard PRs a working (non-bypass) path — closing the gap where every merge currently routes through admin bypass. Direction for each item was decided with the user.

## Background

Branch-protection hardening + CS34 established: required checks = `build-test` + `structural-gate`; `code_coverage`/`code_quality`/`copilot_code_review` removed (unsatisfiable — they forced universal admin bypass); bypass = Admin-only; `required_review_thread_resolution` on; the 4 `cs/log-forging` CodeQL alerts are fixed (CS34) so `code_scanning` no longer blocks on them.

Investigation of the open PRs showed the remaining bypass drivers: (1) the harness review-evidence gates (`read-only-gates`, `copilot-review-attached`, `independence-invariant`) run per-PR but are **not required**, so nothing enforces review evidence at merge; (2) Dependabot bot PRs fail `copilot-review-attached` + `independence-invariant` (no Copilot on bot PRs; `read-only-gates` passes via its bot-author skip) and Dependabot GitHub-Actions bumps fail `structural-gate` (the harness workflow-pins gate) — both harness-managed; (3) workboard-only PRs hit a `blocked`-despite-green state because the review-evidence workflows run on the initial push **before** the `workboard-only` label lands, leaving stale `failure` runs.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Required review-evidence checks | Add **`read-only-gates` + `copilot-review-attached` + `independence-invariant`** to the ruleset `required_status_checks` (alongside `build-test` + `structural-gate`; all `integration_id` 15368). | User-chosen (strongest / matches "not bypassed"): content PRs cannot merge without PR-body evidence, an attached Copilot review, and reviewer-model independence. These checks already run per PR. |
| 2 | Copilot-review enforcement | Keep the `copilot_code_review` ruleset rule **OFF**; enforce Copilot review via the now-required **`copilot-review-attached`** check. | User-chosen. `copilot_code_review` requires an approving review, which Copilot never gives (it only *COMMENTs*) → it forced admin bypass on every PR. `copilot-review-attached` verifies engagement without the deadlock. |
| 3 | Review-thread resolution | Keep **`required_review_thread_resolution` ON**. | User-chosen: unresolved Copilot/reviewer threads block merge, forcing triage of every review comment. |
| 4 | Dependabot merge policy | **Interim:** admin-merge Dependabot PRs after `build-test` passes. **Strategic:** land the harness-upstream fixes in Decision 6 so bot PRs pass/skip the required review-gates and the workflow-pins gate, enabling normal bot-PR merges. | User-chosen (invest in normal-merge). Bot PRs currently fail `copilot-review-attached` + `independence-invariant` (`read-only-gates` passes via bot-author skip); Dependabot actions bumps also fail `structural-gate`. The durable fix is harness-side, with admin-merge as the documented interim. |
| 5 | Bypass posture | Keep **Admin-only** bypass as deliberate break-glass (unchanged); do not remove it or change to `pull_request` mode. | User-chosen: the goal is that normal PRs never *need* bypass once the gates are satisfiable; Admin break-glass preserves solo admin-merge + the workboard auto-merge bot fallback. |
| 6 | Harness-upstream fixes + workboard convention | File **three** tracking issues in `henrik-me/agent-harness`: (a) review-gates jobs **skip Dependabot/bot authors**; (b) the workflow-pins gate **tolerates Dependabot GitHub-Actions SHA+version-comment bumps**; (c) review-gates jobs **key off the `workboard-only` label from their first run** (avoid stale pre-label failures / the blocked-despite-green state). Also adopt, in this repo, the convention of applying `workboard-only` **at PR-creation**. | User-chosen (file all three). These live in harness-managed files this consumer must not edit; cross-repo rule → surface as issues, not edits. The label-at-creation convention is the consumer-side interim for (c). |

## Deliverables

- **Ruleset "push to main" updated** (via `gh api` PUT; GET-verified): `required_status_checks` = [`build-test`, `structural-gate`, `read-only-gates`, `copilot-review-attached`, `independence-invariant`] (all `integration_id` 15368); `copilot_code_review` absent; `required_review_thread_resolution` true; `bypass_actors` = Admin (RepositoryRole 5) only.
- **`docs/ci/review-pr-hardening.md`** (new): the merge-gate policy — required-check list + rationale, the Copilot-review-via-`copilot-review-attached` approach, the Dependabot interim admin-merge policy + links to the upstream issues, the bypass posture, the `workboard-only`-at-PR-creation convention, and a note that `copilot-review-attached` does **not** auto-rerun on Copilot's review submission (`review-gates.yml` triggers only on `pull_request`, unlike `read-only-gates` which has a `pull_request_review` trigger) so the job must be re-run (or a later PR event triggered) after Copilot lands.
- **WORKBOARD heads-up** posted before/at the ruleset update, noting the new required-check set + expected impact on the in-flight peer content PRs #114/#119/#120.
- **Three tracking issues filed in `henrik-me/agent-harness`** (labeled `harness-orchestrator` where possible), URLs recorded in the doc + this CS.
- **Verification note**: confirm (or document blockers to) a compliant content PR merging **without** admin bypass once its required checks are green + threads resolved.

## User-approval gates

- Making the review-evidence gates required **will block the in-flight peer content PRs `#114`/`#119`/`#120`** until their reviews complete — user approved (Decision 1). Post a WORKBOARD heads-up so other orchestrators know the required-check set changed.

## Exit criteria

- Ruleset GET shows the 5 required checks + `copilot_code_review` absent + thread-resolution on + Admin-only bypass.
- `docs/ci/review-pr-hardening.md` merged; `harness lint` green.
- Three `henrik-me/agent-harness` issues filed (URLs captured).
- WORKBOARD heads-up posted announcing the new required-check set + peer-PR impact.
- A content PR is shown to merge without admin bypass once green + threads resolved, **or** the residual blocker is documented against the upstream issue.

## Risks + open questions

- **Blocks in-flight peer PRs** (#114/#119/#120) until reviewed — accepted; mitigate with a WORKBOARD heads-up.
- **Dependabot + workboard PRs stay admin-only** until the upstream fixes (Decision 6) land — documented interim.
- **Applying required checks is coordination-sensitive** (other orchestrators are active); apply deliberately and announce.
- `copilot-review-attached` reliability: `review-gates.yml` triggers only on `pull_request`, so it does **not** auto-rerun when Copilot submits its review (unlike `read-only-gates`, which has a `pull_request_review` trigger) — a job rerun or a later PR event is needed after Copilot lands. Monitor; the harness-upstream fix (Decision 6) can add the trigger.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c5) | 7da321845998 | 2026-07-04T22:39:12Z | Go-with-amendments | Applied all 3 amendments: corrected Dependabot fact (read-only-gates passes via bot-skip); noted copilot-review-attached lacks a review-submit retrigger; WORKBOARD heads-up now a tracked deliverable. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Ruleset: require read-only-gates + copilot-review-attached + independence-invariant | pending | — | gh api PUT; all integration_id 15368; keep build-test + structural-gate; verify via GET |
| WORKBOARD heads-up on the new required-check set | pending | — | Announce impact on in-flight peer PRs #114/#119/#120 (blocked until reviewed) |
| Write docs/ci/review-pr-hardening.md | pending | — | Required checks + rationale, copilot-review-attached approach (+ no review-submit retrigger caveat), Dependabot interim admin-merge, bypass posture, workboard-only-at-creation |
| File 3 harness-upstream issues in henrik-me/agent-harness | pending | — | (a) review-gates skip bot authors; (b) workflow-pins tolerate Dependabot actions bumps; (c) review-gates key off workboard-only label from first run |
| Verify a compliant content PR merges bypass-free | pending | — | Required checks green + threads resolved → normal merge (or document residual blocker) |
| Close-out: docs + restart state | pending | — | Update WORKBOARD + CONTEXT.md so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings; open follow-up CSs if any |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c5 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
