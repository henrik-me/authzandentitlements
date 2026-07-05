# Review & PR merge-gate policy (CS40)

How `main` is protected in `henrik-me/authzandentitlements`, why, and the operational conventions that keep merges **bypass-free**. Owner: the "push to main" repository ruleset (id `18513457`). This doc is the human-readable companion to that ruleset.

## TL;DR

- Every change lands via a **squash** PR; direct pushes to `main` are blocked.
- A PR merges **normally (no admin bypass)** once its **required checks are green** and **all review threads are resolved**.
- Admin bypass is retained only as a deliberate **break-glass**, not a routine path.

## Required status checks

The ruleset requires these five checks (all produced by GitHub Actions, `integration_id` 15368):

| Check | Workflow | Enforces |
|---|---|---|
| `build-test` | `dotnet-ci.yml` | Solution builds (warnings-as-errors) + tests pass |
| `structural-gate` | `harness-pr-check.yml` | `harness lint` + managed/composed template drift |
| `read-only-gates` | `pr-evidence-lint.yml` | PR-body evidence (B1/A3/A4/A6) |
| `copilot-review-attached` | `review-gates.yml` | A Copilot review is attached to the PR |
| `independence-invariant` | `review-gates.yml` | Reviewer model ≠ every implementer model |

Plus the `pull_request` rule: squash-only, **all review threads must be resolved**, `code_scanning` (CodeQL) must be clean, and `creation`/`deletion`/`non_fast_forward` protect the branch.

## Why merges were previously stuck on admin bypass (fixed)

The ruleset originally carried the **`update` ("Restrict updates") rule**, which lets *only bypass actors* update `main`. That made **every** non-admin merge impossible — so 100% of merges went through admin bypass, defeating the "not bypassed" goal. **CS40 removed the `update` rule** (keeping `pull_request` + required checks, which already block direct pushes). Verified: rule-suite results flipped from `bypass` → `pass`; PRs now merge normally once green.

## Copilot review

`copilot_code_review` is intentionally **not** a ruleset rule: Copilot only ever submits *COMMENTED* reviews (never *APPROVED*), so requiring it would force admin bypass on every PR. Instead, **`copilot-review-attached` is a required status check** — it verifies Copilot reviewed, without the never-approves deadlock.

> **Caveat:** `review-gates.yml` triggers only on `pull_request`, so `copilot-review-attached` does **not** auto-rerun when Copilot submits its review (unlike `read-only-gates`, which has a `pull_request_review` trigger). After Copilot lands its review on a failing check, re-run that job or push a trivial PR event. (See harness-upstream fix (c) below.)

## Bypass posture

Bypass actors = **Admin (RepositoryRole 5)** only — a deliberate break-glass for the solo maintainer and the workboard auto-merge bot. The design intent is that a compliant PR **never needs** it; bypass is for exceptional, logged overrides.

## Dependabot policy

Dependabot bot PRs cannot satisfy the required review-gates (`copilot-review-attached` + `independence-invariant` fail — no Copilot on bot PRs), and Dependabot **actions** bumps additionally fail `structural-gate` because they edit harness-**managed** workflow files, tripping the managed/composed **template-drift** check (the workflow-pins sub-check itself passes). Until the harness-upstream fixes below land:

- **Interim:** admin-merge Dependabot PRs after `build-test` (and, for non-actions bumps, `structural-gate`) pass.

## Conventions

- **Apply the `workboard-only` label at PR creation** (`gh pr create --label workboard-only`) so the review-evidence jobs evaluate the label on their first run and `skip` cleanly (a skipped required check counts as passing) instead of leaving stale `failure` runs.
- **Wait for `Analyze (csharp)` (CodeQL) to finish** before merging — the `code_scanning` rule needs it complete; a PR shows `BLOCKED` until then, then `CLEAN`/`UNSTABLE`.

## Harness-upstream fixes needed (file in `henrik-me/agent-harness`)

These require changes to **harness-managed** workflows this consumer must not edit. Per OPERATIONS.md **C35-13** the agent does not open issues in the harness repo, so they are documented here for the maintainer to file (or to relay upstream):

- **(a) `review-gates` should skip Dependabot/bot-authored PRs.** Bot PRs have no Copilot review, so `copilot-review-attached` + `independence-invariant` fail and — now that they're required — block bot PRs entirely. Add a bot-author skip (as `pr-evidence-lint.yml` already has a `bot-author` skip-reason). Evidence: PRs #104, #105.
- **(b) The managed/composed template-drift check (`structural-gate`) should tolerate Dependabot GitHub-Actions bumps to harness-managed workflows.** Dependabot rewrites `uses: owner/action@<new-sha> # vX.Y.Z` inside harness-managed workflow files (e.g. `review-gates.yml`), which the drift check flags as unauthorized edits to managed files — failing a *required* check. The workflow-pins sub-check passes; the failure is drift. Upstream should own action-version bumps for managed workflows (ship via `harness sync`) and/or the drift check should tolerate action-SHA-only edits in managed workflows. Evidence: PR #104 (actions/checkout 6.0.2→7.0.0) — `structural-gate` log shows `✓ workflow-pins: pass` with a managed-drift failure.
- **(c) `review-gates` jobs should evaluate the `workboard-only` label robustly / add a `pull_request_review` trigger.** Two symptoms: stale pre-label `failure` runs when a label is added after open (mitigated consumer-side by labeling at creation), and `copilot-review-attached` not auto-rerunning on Copilot's review submission.

## References

- CS28 (dotnet-ci gate), CS34 (log-forging fix), CS40 (this hardening).
- Ruleset: `gh api repos/henrik-me/authzandentitlements/rulesets/18513457`.
