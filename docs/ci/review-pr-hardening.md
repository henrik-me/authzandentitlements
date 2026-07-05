# Review & PR merge-gate policy (CS40)

How `main` is protected in `henrik-me/authzandentitlements`, why, and the operational conventions that keep merges **bypass-free**. Owner: the "push to main" repository ruleset (id `18513457`). This doc is a **non-authoritative human summary**; the **live ruleset is the operational source of truth** — query it with `gh api repos/henrik-me/authzandentitlements/rulesets/18513457`. See [Source of truth & the committed spec](#source-of-truth--the-committed-spec) for how this relates to the committed `infra/main-protection-ruleset.json`.

## TL;DR

- Every change lands via a **squash** PR; direct pushes to `main` are blocked.
- A PR merges **normally (no admin bypass)** once its **required checks are green** and **all review threads are resolved**.
- Admin bypass is retained only as a deliberate **break-glass**, not a routine path.

## Required status checks

The live ruleset (id `18513457`) currently requires these five checks (all produced by GitHub Actions, `integration_id` 15368). This table summarizes the live state — it is **not** the source of truth; verify against the ruleset itself:

| Check | Workflow | Enforces |
|---|---|---|
| `build-test` | `dotnet-ci.yml` | Solution builds (warnings-as-errors) + tests pass |
| `structural-gate` | `harness-pr-check.yml` | `harness lint` + managed/composed template drift |
| `read-only-gates` | `pr-evidence-lint.yml` | PR-body evidence (B1/A3/A4/A6) |
| `copilot-review-attached` | `review-gates.yml` | A Copilot review is attached to the PR |
| `independence-invariant` | `review-gates.yml` | Reviewer model ≠ every implementer model |

Plus the `pull_request` rule: squash-only, **all review threads must be resolved**, `code_scanning` (CodeQL) must be clean, and `creation`/`deletion`/`non_fast_forward` protect the branch.

## Source of truth & the committed spec

The **live ruleset** (id `18513457`, `gh api repos/henrik-me/authzandentitlements/rulesets/18513457`) is the operational source of truth. The repo also contains a committed spec, `infra/main-protection-ruleset.json`, which is the **harness-intended** ruleset: `harness sync --mode=apply` injects the four review-gate contexts — `review-log-evidence`, `copilot-review-attached`, `independence-invariant`, `review-threads-resolved` — into its `required_checks` when `reviews.enforce_gates=true`, and `sync --mode=check` fails when those contexts are missing (OPERATIONS.md, REVIEWS.md).

That spec was authored while the repo was **private**, when repository rulesets could not be applied (private repos need GitHub Pro; the apply returned HTTP 403), so it was **never applied** — CI ran advisory-only. The repo is now **public** and the live ruleset was applied and hardened directly via the API during CS40. The committed spec therefore **drifts** from the live ruleset:

- **In both:** `copilot-review-attached`, `independence-invariant`.
- **Live-only:** `build-test`, `structural-gate`, `read-only-gates` (repo-specific build/lint/evidence checks the harness spec never listed).
- **Spec-only:** `review-log-evidence` and `review-threads-resolved` — both are standalone jobs defined in `review-gates.yml`, but neither is in the live ruleset's **required** set. (Thread resolution is still enforced live, independently, by the `pull_request` rule's `required_review_thread_resolution`.)

Reconciling the drift is split by ownership: the now-stale "ruleset not applied / CI advisory-only" note in `CONTEXT.md` (consumer-owned) is corrected at CS40 close-out, while updating the committed `infra/main-protection-ruleset.json` spec itself — a harness-sync-managed artifact whose review contexts are injected by `harness sync` — is tracked as a CS40 follow-up (see the CS file) rather than hand-edited here.


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

## Harness-upstream fixes (tracked in `henrik-me/agent-harness`)

These require changes to **harness-managed** workflows this consumer must not edit. They are tracked upstream in `henrik-me/agent-harness` (filed at the maintainer's direction; OPERATIONS.md C35-13 otherwise makes the harness repo an inbound-only channel for the agent):

- **(a) `review-gates` should skip Dependabot/bot-authored PRs.** Bot PRs have no Copilot review, so `copilot-review-attached` + `independence-invariant` fail and — now that they're required — block bot PRs entirely. Add a bot-author skip (as `pr-evidence-lint.yml` already has a `bot-author` skip-reason). Evidence: PRs #104, #105. **Tracked upstream: henrik-me/agent-harness#393** (corroborated — the bot-author skip is item 2 there).
- **(b) The managed/composed template-drift check (`structural-gate`) should tolerate Dependabot GitHub-Actions bumps to harness-managed workflows.** Dependabot rewrites `uses: owner/action@<new-sha> # vX.Y.Z` inside harness-managed workflow files (e.g. `review-gates.yml`), which the drift check flags as unauthorized edits to managed files — failing a *required* check. The workflow-pins sub-check passes; the failure is drift. Upstream should own action-version bumps for managed workflows (ship via `harness sync`) and/or the drift check should tolerate action-SHA-only edits in managed workflows. Evidence: PR #104 (actions/checkout 6.0.2→7.0.0) — `structural-gate` log shows `✓ workflow-pins: pass` with a managed-drift failure. **Tracked upstream: henrik-me/agent-harness#496.**
- **(c) `review-gates` jobs should evaluate the `workboard-only` label robustly / add a `pull_request_review` trigger.** Two symptoms: stale pre-label `failure` runs when a label is added after open (mitigated consumer-side by labeling at creation), and `copilot-review-attached` not auto-rerunning on Copilot's review submission. **Tracked upstream: henrik-me/agent-harness#497.**

## References

- CS28 (dotnet-ci gate), CS34 (log-forging fix), CS40 (this hardening).
- Ruleset: `gh api repos/henrik-me/authzandentitlements/rulesets/18513457`.
