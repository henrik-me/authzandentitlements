# REVIEWS

> **File class: composed.**
> The managed core below is owned by the harness and updated on sync. The
> `reviews.project-gates` block at the end is owned by the project and
> preserved across syncs. Edit managed sections via
> `template/composed/REVIEWS.md`; edit local gates directly in this file.

---

## Overview

Every clickstop (CS) passes through a three-phase review lifecycle:

1. **Claim PR review** — a brief local review performed before opening the
   content PR, confirming the work is coherent enough to share.
2. **Content PR review** — iterative GPT-5.5 (or approved fallback) review
   rounds on the open PR until all blocking findings are resolved.
3. **Close-out PR review** — a final pass confirming that non-blocking
   findings have been tracked or addressed and that the retrospective entry is
   filed.

This document defines the policies, models, calibration targets, and findings
taxonomy that govern each phase.

---

## Phase 1 — Claim PR Review

The claim PR is a lightweight WORKBOARD-only PR (labeled `workboard-only`).
It is auto-approved and auto-merged by the dedicated workflow
(`workboard-auto-approve.yml`) when:

- Only `WORKBOARD.md` and/or clickstop rename paths changed.
- The `workboard-only` label is present.
- The author is in the approved-actors list.

No formal review iteration is required for claim PRs. The orchestrator is
responsible for verifying the claim is correct (CS number, slug, branch name)
before the PR is raised.

---

## Phase 2 — Content PR Review

### 2.1 Review model

**Primary reviewer: GPT-5.5.**

GPT-5.5 is the sole approved primary reviewer for all content PRs. Every CS
implementation must receive at least one GPT-5.5 review before the PR is
considered merge-ready.

**Fallback: Claude Sonnet 4.6** — subject to the independence invariant and
risk-class restrictions below.

### 2.2 Fallback policy (Decision #22)

If GPT-5.5 is unavailable for more than 30 minutes, or after two failed
attempts, the orchestrator may fall back to a Claude Sonnet 4.6 rubber-duck
review. The following conditions must all hold before the fallback is allowed:

| Condition | Requirement |
|---|---|
| Independence invariant | Sonnet 4.6 must **not** have materially implemented the CS being reviewed. If Sonnet performed non-trivial implementation sub-tasks within the CS, the fallback is forbidden. |
| Risk class | The CS must **not** be HIGH-RISK (see §2.3). |
| Documentation | The PR body must record: model used, timestamp, fallback reason, and the implementer-model-list for the CS. |

If Sonnet 4.6 cannot be used (independence violation or HIGH-RISK CS), the
only permitted options are: retry GPT-5.5, or obtain an explicit user waiver.
A user waiver must be recorded in the PR body with the waiver rationale.

### 2.2.1 Reviewer model fallback ladder (CS35 C35-2)

The fallback ladder governs which model to use when the primary reviewer
(GPT-5.5) is unavailable:

> GPT-highest-available (5.5 → 5.4 → ...) → Claude Sonnet-highest (4.7 → 4.6 → ...) → orchestrator's own model (last resort with explicit user waiver). The independence invariant (no implementer/reviewer model overlap) applies at every step of the ladder.

See §2.3 for risk-class restrictions: HIGH-RISK CSs forbid the
orchestrator-own-model rung absent an explicit user waiver.

### 2.3 Risk-class restrictions

HIGH-RISK CSs require GPT-5.5 **or explicit user waiver only** — no Sonnet
fallback regardless of independence:

- CS03 (sync engine)
- CS11 (self-host)
- CS15a (public-readiness, visibility flip, and Ruleset activation)
- CS18b (PILOT-A baseline)
- CS19 (migration)

Any CS newly designated HIGH-RISK by the orchestrator inherits these same
restrictions.

### 2.4 Review-round calibration

Review convergence takes multiple rounds. The following targets are calibration
baselines, not hard caps — converge when all blocking findings are resolved
regardless of round count.

| CS risk class | Expected rounds | Calibration source |
|---|---|---|
| User-facing surface (CLI, schema-rendered outputs) | ~3 | [LRN-031](LEARNINGS.md#lrn-031) |
| HIGH-RISK (sync engine, schema authoring, public-flip, migration) | 5–8 | [LRN-024](LEARNINGS.md#lrn-024) |
| Thin plumbing / tooling | ~2 | heuristic |

**Do not close the content PR until GPT-5.5 (or approved fallback) issues an
explicit GO verdict.**

Key observations from [LRN-024](LEARNINGS.md#lrn-024) and
[LRN-031](LEARNINGS.md#lrn-031):

- Each fix round can introduce new findings by disturbing adjacent invariants.
  Multiple iterations are normal, not a sign of poor work.
- High-risk CSs with multiple interacting invariants (fail-closed semantics,
  cross-platform behaviour, prototype-pollution edge cases) routinely require
  6–7 rounds even for careful implementations.
- User-facing CLIs generate 5–10 findings per round because they have rich
  behavioural contracts (exit codes, flag semantics, help text, platform
  portability).
- Budget time and sub-agent slots accordingly when planning high-risk or
  user-facing CSs.

### 2.4.1 Canonical orchestrator command (`harness review`)

For content PR review rounds, the canonical orchestrator entry point is:

```
harness review <pr> [--repo owner/name] [--model gpt-5.5|sonnet-4.6] [--round R<n>] [--no-poll|--dry-run]
```

`harness.config.json` → `reviews.require_copilot_review` defaults to `true`;
set it to `false` only for projects where Copilot review evidence is not
required or unavailable.

The command validates that the target is a content PR, enforces the reviewer
independence invariant from the PR body's `## Model audit` / CS plan review
evidence, emits the manual MVP rubber-duck prompt, optionally triggers and
polls Copilot review, and appends the PR body's `## Review log` + `## Model
audit` evidence when the round completes. Exit codes are: `0` = Go / dispatch
accepted, `1` = No-Go or unresolved Blocking finding, `2` = usage, policy, or
transport failure.

Use `harness copilot-engage` only as a Copilot-only fallback when the combined
review command is unavailable or unsuitable for a narrowly scoped retry.

### 2.4.2 Specialized review-family verbs (CS66)

Layered on the `harness review` orchestration are four domain-specific review
verbs. Each makes the right checklist invokable at the right lifecycle moment
instead of relying on the reviewer to remember it:

| Verb | Review type | Canonical executable path for |
|---|---|---|
| `harness review-doc <pr>` | Doc / prose fact-claim | The § 2.6a F1–F5 fact-claim checklist |
| `harness perf-review <pr>` | Performance | A diff-scoped perf checklist (hot-path allocations, algorithmic complexity, N+1 / repeated IO, sync-in-async, unbounded growth) |
| `harness security-review <pr>` | Security | A diff-scoped security checklist (secrets, command/path injection, unsafe deserialization, workflow `permissions`, ref/`--body-file` containment, supply-chain pin drift) |
| `harness review-cs <NN>` | Clickstop readiness | A local verify-only aggregation of the plan-review attestation + Plan-vs-implementation (PVI) gates |

The three PR-based verbs (`review-doc`, `perf-review`, `security-review`) reuse
the `harness review` reviewer-independence invariant (§ 2.3) and emit the
canonical reviewer-output shape. They are **advisory by default** (exit 0) and
invoke **no model** unless a completed reviewer output is supplied via
`--reviewer-output` (the default run only composes and prints the dispatch
prompt + plan); pass `--strict` to fail on a non-Go verdict. `harness review-cs`
is **local and verify-only** — no model, no `gh`, no PR — the fast "is this CS
review-complete? what's missing?" pre-flight before the close-out gate.

### 2.4.3 Leaner review sequencing (issue #424)

Content-PR review churn is minimised by sequencing the rubber-duck and Copilot
legs deliberately, rather than interleaving them:

1. **Rubber-duck to Go before the first Copilot engage.** Drive the local
   GPT-5.5 (or approved-fallback) review to an explicit `Go` at the current
   HEAD *before* requesting the Copilot reviewer. Engaging Copilot on a diff the
   rubber-duck has not yet cleared produces two independent streams of findings
   on the same unsettled code.
2. **Batch review fixes into minimal commits.** Fold all fixes for one review
   round into a single commit, not one commit per finding — every new HEAD
   invalidates the A4 stale-diff attestation, re-triggers the gates, and (see
   below) prompts a fresh Copilot re-review.
3. **After the final Copilot re-engage, resolve all threads and merge without
   further commits.** Once Copilot has re-reviewed the fixed HEAD, resolve every
   remaining thread and merge — do not open another fix→re-attest→re-engage
   cycle for the cosmetic residue of a converged review.
4. **A genuinely new *blocking* finding is the one exception.** If a review round
   surfaces a real new bug, it takes the normal path: fix it, obtain a fresh
   local `Go` at the new HEAD (A4), and re-engage Copilot. The "no further
   commits" rule in (3) applies only to **re-raises of already-addressed items
   and non-blocking style nits** — never to a new real bug.
5. **Treat Copilot re-raises as "resolve, don't re-fix".** Copilot re-scans the
   whole diff on every engage and re-emits its full comment set, so already-fixed
   items reappear as fresh `COMMENTED` threads. Resolve them with a one-line
   disposition that points at the commit or test already covering them; do not
   re-edit code that is already correct and test-guarded.

The push-triggered `read-only-gates` job (`.github/workflows/pr-evidence-lint.yml`)
re-runs **automatically** when a review is submitted — the `pull_request_review`
trigger added for issue #424 — so the A5+A16 Copilot gate turns green on its own
once Copilot's asynchronous review lands, with no manual `gh run rerun`.

### 2.5 What the reviewer examines

The review scope depends on CS type:

**Implementation CSs:**
- Correctness and edge-case coverage.
- Sync invariants — especially composed-class fail-closed semantics.
- Schema compatibility (no breaking changes to `harness.lock.json` or
  published schemas).
- Test coverage — new behaviour must have tests; regression tests for any
  found bugs.
- Secrets and IP hygiene — no credentials, internal hostnames, or
  third-party-copyright content committed.

**Template CSs (CS08–CS10, canonical doc authoring):**
- Linter pass (`check-composed-blocks.mjs` for composed-class files).
- Cross-link integrity (anchors resolve, LEARNINGS.md IDs exist).
- Schema conformance (YAML front-matter where required).
- No project-specific leakage into managed templates — managed sections must
  be portable across harness consumers.

**Migration CSs (CS19):**
- Parity manifest completeness.
- Freshness-calendar compliance.
- Migration-base SHA recorded.
- Soft-freeze status confirmed.
- Rollback path documented.

### 2.6 Findings taxonomy

Every finding is classified at time of delivery. The reviewer must use
exactly one of these labels:

| Label | Meaning | Gate |
|---|---|---|
| **Blocking** | Defect, missing invariant, security/IP issue, or broken contract that must be resolved before merge. | Hard gate — PR cannot merge with open Blocking findings. |
| **Non-blocking** | Real issue worth tracking, but safe to defer. Examples: debt items, minor inconsistency, opportunistic improvement. | Soft gate — must be recorded in the close-out entry; may not silently vanish. |
| **Suggestion** | Optional improvement at the orchestrator's discretion. No gate. | No gate — record or discard. |

### 2.6a Rubber-duck scope — fact-claim verification (PR #218 doctrine)

The reviewer's job is not only to read the diff. A "Go" verdict is only valid
when the reviewer has affirmatively verified that every factual claim in the
diff matches the cited shipped surface. This applies to all CS types, but is
the **dominant failure mode for documentation and prose PRs** — where the
diff itself looks coherent but the claims it makes about CLI behaviour,
doctrine, file paths, or prior LRN/CS entries can be wrong without the
reviewer noticing.

**Required checks for every Go verdict on a doc/prose-heavy PR:**

| # | Check | Source of truth |
|---|---|---|
| F1 | Every `--flag` mentioned exists in the CLI surface. | `bin/harness.mjs` (`SUBCOMMAND_HELP` blocks and `cmdXxx` argument parsers); `lib/<module>.mjs` for behaviour; `scripts/*.mjs` for pass-through subcommands (e.g. `harness review-output` forwards to `scripts/check-review-output.mjs`). |
| F2 | Every file path mentioned exists in the tree (or is explicitly described as not-yet-existing). | Repo filesystem at the analyzed HEAD. |
| F3 | Every doctrine-strength claim (`required`, `mandatory`, `enforces`, `recommended`, `optional`) matches the cited source's wording verbatim or via a documented synonym. | Cited doc (OPERATIONS.md, REVIEWS.md, INSTRUCTIONS.md, README.md, etc.). |
| F4 | Every summary of a LEARNINGS.md or CS entry stays within the source entry's stated scope. No generalisation beyond what the Problem / Finding / Decision text asserts. | The LRN/CS entry itself. |
| F5 | Cross-doc claims are mutually consistent. If the diff says "OPERATIONS.md says X" or "the CLI does Y", verify that OPERATIONS.md actually says X and the CLI actually does Y at the analyzed HEAD. | The other doc(s) and code referenced. |

**Reviewer prompt obligation.** When dispatching a rubber-duck for a doc PR,
the orchestrator MUST include language equivalent to: *"verify F1–F5 above
against the shipped surfaces — do not rely on the diff being internally
coherent."* The canonical reviewer preamble's `**scope:**` field already
references this expectation (see
[OPERATIONS.md § Reviewer dispatch — canonical preamble](OPERATIONS.md#reviewer-dispatch--canonical-preamble)).

**Executable path (CS66).** `harness review-doc <pr>` is the canonical
executable entry point for this F1–F5 checklist: it composes a reviewer prompt
that enumerates F1–F5, scopes it to the PR diff, enforces reviewer independence
(§ 2.3), and — when a completed reviewer output is supplied — parses the
verdict. Advisory by default; see § 2.4.2.

**Empirical motivation.** PR #218 (CS55+CS56 doc backfill) required 3
substantive Copilot review rounds to surface 7 unique fact-claim issues
(R4 returned 0 findings) — every one of which the rubber-duck pre-review
missed because the dispatch prompt asked the reviewer to verify the diff
was coherent, not to cross-check claims against shipped code/help
text/doctrine. Examples of what was missed: nonexistent CLI flag
(`--idempotent`), nonexistent file path (`template/composed/INSTRUCTIONS.md`),
"enforces" overclaim contradicting CLI help text that says "doctrine; not
enforced", and LRN-138 summary that generalised the entry beyond its
"contents transmitted to a third party" scope.

### 2.6b Rubber-duck scope — schema-conformance verification (LRN-145)

A change that **adds or edits a config or schema reader** — any code that reads
`harness.config.json`, a `schemas/*.schema.json`, or another structured
artefact and decides which fields are required vs defaulted vs validated —
carries a P0 blind spot that diff-coherence review misses: the reader can
enforce a requirement-level the schema does not declare. A "Go" verdict on such
a change is only valid when the reviewer has diffed the reader's enforced
contract against the schema's declared contract.

**Required checks for every Go verdict on a config/schema-reader change:**

| # | Check | Source of truth |
|---|---|---|
| S1 | Every field the reader *requires* (throws/errors when absent) appears in the schema's `required` array. A reader that rejects a field the schema marks optional or supplies a `default` for is a defect — fix the reader, not the schema. | `schemas/*.schema.json` `required` + per-property presence. |
| S2 | Every field the reader defaults-when-absent uses the **same default** the schema declares (`default`), unless a deliberate divergence is documented (code comment + LEARNINGS.md). A silent default mismatch is a defect. | `schemas/*.schema.json` per-property `default`. |
| S3 | The inverse also holds: the reader does not silently accept a *present-but-malformed* value the schema would reject (wrong `type`, bad `pattern`/`enum`, non-`uniqueItems`). Fail closed on present-but-malformed. | The property's `type` / `pattern` / `enum` / `items` constraints. |

**Reviewer prompt obligation.** When dispatching a rubber-duck for a change
that touches a config or schema reader, the orchestrator MUST include language
equivalent to: *"verify S1–S3 above — diff the reader's required / default /
validation contract against the schema, not just the diff's internal logic."*

**Empirical motivation.** In CS60 the LRN-142 de-drift of
`check-independence-invariant.mjs` made the reader *require*
`reviews.rubber_duck_model` and `reviews.high_risk_clickstops`, both of which
the schema marks optional with `default`s — a silent breaking change for a
schema-valid consumer config that omits them. A three-round GPT-5.5 rubber-duck
review-of-record returned Go without catching it (it verified the fail-closed
*logic*, never the reader-vs-schema *contract*); GitHub Copilot caught it
post-Go by reading the schema. CS61 (LRN-145) factored the reader into a single
`loadReviewsPolicy` with default-when-absent / fail-closed-on-malformed
semantics and added this checklist item.

**Author-side self-check (LRN-149).** Apply S1–S3 to your *own* config/schema
reader **before** opening the PR. Enumerate every constraint dimension the
schema declares for the subtree you read — `required`, per-field `default`,
`type`, `enum`, `pattern`, `uniqueItems`, `additionalProperties`,
`minLength`/`maxLength`, numeric bounds — and confirm the reader honours each
(default-when-absent for optional+defaulted; fail-closed on present-but-malformed
for every constraint). This is the cheapest place to catch the gap: in CS61
(PR #250) the post-merge-style review surfaced roughly one unhandled dimension
per round (`additionalProperties:false`, then empty-string `minLength`, then
whitespace-only `pattern`) across several rounds; a single up-front author S1–S3
sweep would have collapsed them into the first.

### 2.6c Plan-review scope — fact-claim verification (LRN-139 / LRN-158)

The PR-side F1–F5 doctrine in § 2.6a applies symmetrically to **plan reviews**
of CS files under `project/clickstops/{planned,active}/` (per
[OPERATIONS.md § Plan review attestation procedure (CS35b)](OPERATIONS.md#plan-review-attestation-procedure-cs35b)).
A plan-review `Go` verdict is only valid when the reviewer has affirmatively
verified that every factual claim the plan makes about the repository state
matches the actual repository at the analyzed HEAD — and that every
state-of-the-world premise (release/tag/PR/issue/label state) has been
verified via a non-mutating CLI probe. Plan reviews were the surface where
LRN-139 and LRN-158 surfaced this gap: a plan can assert a defect that does
not exist (CS54 T1 — false positive that survived 17 rubber-duck rounds) or
assume a state-of-the-world that is wrong (CS70 — premise wrong across
3 plan-review rounds; the audit itself was ~10 seconds).

**Scope — all reviewer-consumed plan sections.** Per OPERATIONS.md
§ Plan review attestation procedure, the reviewer consumes the full
plan file — Background, Decisions, Deliverables, Sub-agent fan-out,
Exit criteria, Risks + open questions — together with any cross-CS
dependencies the plan declares. Fact-claim verification applies to
every factual claim in any of those sections (the list is exhaustive
for the inputs CS35b enumerates), not only Decisions+Deliverables (the
surface covered by the plan-review hash). The hash attests the reviewer
saw a particular Decisions+Deliverables body; F1–F6 attest the reviewer
verified the plan's factual premises across the whole reviewer-consumed
surface.

**Required checks for every plan-review `Go` verdict:**

| # | Check | Source of truth |
|---|---|---|
| F1 | Every `--flag` named in the plan exists in the CLI surface (or is explicitly described as not-yet-existing). | `bin/harness.mjs` (`SUBCOMMAND_HELP` blocks and `cmdXxx` argument parsers); `lib/<module>.mjs`; `scripts/*.mjs` pass-throughs. |
| F2 | Every file path named in the plan exists in the tree (or is explicitly flagged as to-be-created). For citations of the form `path/to/file:N`, the reviewer MUST open the file at the analyzed HEAD and confirm line N contains what the plan asserts. A "stray fence at L680" claim requires confirming the fence has no matching opener at the analyzed HEAD — line numbers drift across snapshots, across syncs, and across edits between plan draft and plan review. | Repo filesystem at the analyzed HEAD. |
| F3 | Every doctrine-strength claim (`required`, `mandatory`, `enforces`, `recommended`, `optional`) attributed to another doc matches that doc's wording verbatim or via a documented synonym. | The cited doc (OPERATIONS.md, REVIEWS.md, INSTRUCTIONS.md, README.md, etc.). |
| F4 | Every summary of a LEARNINGS.md or CS entry stays within the source entry's stated `Problem` / `Finding` / `Decision` scope. No generalisation beyond what the source asserts. | The LRN/CS entry itself. |
| F5 | Cross-doc claims are mutually consistent across the surfaces named in the plan (CHANGELOG vs OPERATIONS vs REVIEWS vs LRN). | The other doc(s) and code referenced. |
| F6 | Every **state-of-the-world claim** the plan relies on (e.g. "tag `v0.7.0` does not exist", "release X is in Draft", "PR Y is open", "label Z is present in the consumer repo", "the branch protection ruleset requires N approvals") is verified at plan-review time via a non-mutating CLI probe and the probe is recorded in the plan's Background or Constraints so subsequent reviewers can audit the same premise. For release/tag state: `gh release list --repo <owner>/<repo> --limit N`, `gh api repos/<owner>/<repo>/releases --jq 'map(select(.tag_name=="<tag>"))'` (both published AND draft), `git ls-remote origin refs/tags/<tag>`. For PR/issue/label state: `gh pr view <num> --repo <owner>/<repo>`, `gh issue view <num> --repo <owner>/<repo>`, `gh label list --repo <owner>/<repo>`. | GitHub API / git remote / live shell at plan-review time. |

**Inherited findings discipline (LRN-139).** When a plan inherits a finding
from another repo, from another snapshot, or from any prior Copilot /
rubber-duck review whose anchor (file:line, SHA, tag, release id) was
captured at a different HEAD, the plan author MUST re-verify the finding
against the current harness repo state before claiming it as a defect or as
a premise. An unverified inherited citation is not an acceptable plan
premise; the reviewer MUST treat it as a `Needs-Fix` blocker even if the
surrounding plan prose is internally coherent.

**Reviewer prompt obligation.** When dispatching a plan-review rubber-duck,
the orchestrator MUST include language equivalent to: *"verify F1–F6 above
against the actual repository at HEAD `<sha>` and the live shell — do not
accept the plan's factual claims on faith. For every state-of-the-world
claim in Background or Constraints, list the CLI probe you ran (or are
about to run) to verify it; if the plan does not include such a probe for
a state-of-the-world premise, return `Needs-Fix`."* The canonical reviewer
preamble in
[OPERATIONS.md § Reviewer dispatch — canonical preamble](OPERATIONS.md#reviewer-dispatch--canonical-preamble)
carries this expectation; the CS35b plan-review attestation procedure
cross-references this section.

**Empirical motivation.**

- **CS54 T1 (LRN-139).** The plan asserted a "stray triple-backtick fence at
  `template/composed/OPERATIONS.md:680` with no matching opener", inherited
  from a Copilot inline finding on a sibling-repo PR. Seventeen GPT-5.5
  rubber-duck rounds attested at hash `5c40242b24c7`, all `Go`. None opened
  the file. Implementation discovered the triple-backtick at L809 (line
  numbers had drifted since the inherited finding) was the legitimate close
  of the canonical preamble fence opened at L684; applying the "fix" broke
  the `composed-blocks:OPERATIONS.md` lint by orphaning the
  `operations.project-deploy` local-block markers downstream. Reverted at
  commit `a770134`. F2 + the inherited-findings clause above directly address
  this failure mode.
- **CS70 (LRN-158).** The plan asserted that `v0.7.0` had been bumped in
  `package.json` + CHANGELOG at CS54's close-out (commit `53e1a09`,
  2026-06-03) but never tagged or shipped. Three GPT-5.5 plan-review rounds
  (R1 Needs-Fix → R2 Needs-Fix → R3 Go, all at hash `7ab92e2eb150`)
  attested without anyone running `gh release list` or
  `gh api repos/<owner>/<repo>/releases --jq '.[]'`. Execution discovered
  the tag plus a published Release had existed at `53e1a09` for six days
  (auto-created by `release.yml` LRN-121); the only Phase 1 action actually
  needed was deleting a stale duplicate Draft sibling. F6 directly addresses
  this failure mode.

Both incidents share the same shape: the plan prose was internally coherent
(diff-equivalent), but the factual premise about the repository state was
wrong. § 2.6c closes that gap on the planning surface symmetrically to how
§ 2.6a closed it for shipped-code PR reviews.

**Executable path (CS66).** `harness review-cs <NN>` is the canonical local
pre-flight for clickstop readiness: it locates the single planned/active/done
file for the CS and aggregates the plan-review attestation gate
(`check-clickstop-plan-review.mjs`) and the PVI close-out gate
(`check-clickstop.mjs`) into one actionable report. It is a **mechanical gate
check, not a substitute** for the F1–F6 fact-claim verification above — the
verb confirms the attestation exists and passes; the reviewer still verifies
the plan's factual premises. Advisory by default, `--strict` to fail; see
§ 2.4.2.

### 2.7 Finding disposition

**Blocking findings:** must be addressed before merge via one of:

1. **Fixed** — the finding is corrected in the PR and the reviewer confirms in
   the next round.
2. **Explicitly waived** — the orchestrator documents a justification for why
   the finding is safe to defer or disagree with. The waiver is recorded in
   the PR body. The orchestrator reserves final judgment on all waivers.

Silently ignoring a Blocking finding is not permitted. If the orchestrator
disagrees with the reviewer's classification, the disagreement must be stated
explicitly in the PR body before proceeding.

**Non-blocking findings:** must appear in the close-out retrospective entry.
The entry records: finding text, source round, and one of: `deferred`,
`addressed`, or `accepted-debt`.

**Suggestions:** the orchestrator logs or discards at its discretion. No
close-out entry required.

### 2.8 PR body requirements

Every content PR body must record the following fields before merge:

```
## Review log

| timestamp | analyzed_head | actor | model | verdict | evidence_link |
|---|---|---|---|---|---|
| 2026-05-14T10:32:00Z | a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2 | yoga-ah | gpt-5.5 | Go | https://github.com/henrik-me/agent-harness/pull/150#issuecomment-123456 |
```

**Review log column rules:**

- `timestamp` — RFC 3339 UTC (`...Z`).
- `analyzed_head` — full 40-character commit SHA the reviewer analysed. The A4 gate compares this to the current PR HEAD.
- `actor` — round/role annotation (e.g. `yoga-ah`, `rubber-duck`, `rubber-duck (narrow R2)`, `omni-ah (PvI R3)`). This is the column where round numbers and dispatch labels live.
- `model` — **MUST be the bare reviewer-model identifier** (e.g. `gpt-5.5`, `claude-sonnet-4.6`, `claude-opus-4.7`). Decorations like `gpt-5.5 (R2)`, `gpt-5.5 (reviewer)`, `gpt-5.5 (PvI)`, `gpt-5.5 (narrow re-attest)` are not permitted — they historically slipped past the PR-side `review-log-evidence` gate because `normalizeModel()` in `scripts/checks/check-review-log-evidence.mjs` collapsed them away from the primary reviewer ID (`gpt-5.5 (R2)` → `gpt-5.5-r2`), failed the primary-reviewer check, and were then approved via the fallback-rationale path (LRN-136). Display-form inputs that contain whitespace (e.g. `Claude Opus 4.7`) are also rejected — the bare-id regex `/^[A-Za-z0-9._-]+$/` only accepts unbroken identifiers (the case-normalization happens downstream in `normalizeModel()`, so `gpt-5.5` and `GPT-5.5` are treated equivalently for the audit-match check; both pass the bare-id check). Put round / role annotations in the `actor` column instead. Mechanically enforced since CS54 by `scripts/checks/check-review-log-evidence.mjs` (bare-id check fires for every row BEFORE `reviewerModelApproved()`). Note: this is a separate concern from the independence invariant (`scripts/checks/check-independence-invariant.mjs`), which reads `## Model audit`'s `Reviewer model` rather than the Review log `model` cell.
- `verdict` — one of `Go`, `Conditional Go`, `Needs-Fix` (historical spelling `Go-with-amendments` is accepted but not preferred).
- `evidence_link` — URL to the rubber-duck report comment, sub-agent transcript, or other artefact backing the verdict.

## Model audit

| Field | Required | Description |
|---|---|---|
| `Implementer models` | yes | Comma-separated list of every model that materially implemented any code/doc/config in the CS (orchestrator + all sub-agents). Case-insensitive on the family + version pair (e.g. `claude-opus-4.7` ≡ `Claude Opus 4.7`). |
| `Reviewer model` | yes | Single model identifier from the C35-2 fallback ladder. |
| `Implementer agent` | overlap is a hard error in v0.5.0 (CS41); missing or empty cells warn in v0.5.0, become a hard error in v0.6.0 (C42-6 strict-flip) | GitHub username of the implementing agent. Per CS35 C35-18 (agent-identity independence). Mechanically enforced: `scripts/check-clickstop-implementer-not-reviewer.mjs` (CS41) on the planned/active/done CS files; `scripts/check-review-evidence.mjs` (CS36, parser extended in CS41) on the PR body's `## Model audit` block. |
| `Reviewer agent` | overlap is a hard error in v0.5.0 (CS41); missing or empty cells warn in v0.5.0, become a hard error in v0.6.0 (C42-6 strict-flip) | GitHub username of the reviewing agent. Per CS35 C35-18. Same enforcement surface as `Implementer agent`. |

**Independence invariant (MUST):** `intersection({Implementer models}, {Reviewer model})` = ∅. Comparison is case-insensitive on the family + version pair. Violation blocks merge per A3.

**Agent-identity independence (MUST per CS35 C35-18 + CS41):** `Implementer agent` ≠ `Reviewer agent` (case-insensitive). v0.4.0 issued a warning when columns were absent; v0.5.0 (CS41) requires both columns and treats overlap as a hard error. The `--strict-agent-columns` flag on `check-review-evidence.mjs` defaults to `false` in v0.5.0 (warning-on-missing) so consumers have a one-cycle migration ramp; flips to `true` in v0.6.0 (hard error) per C42-6 strict-flip plan.

Example block (paste into the active CS file):

```
## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ah |
| Reviewer agent | copilot |
```

**Stale-diff doctrine (CS35 C35-3 + A4 gate):** A `Go` row whose
`analyzed_head` ≠ current HEAD is INVALID — re-review is required before
merge. The A4 PR-evidence gate (lands in CS36) enforces this mechanically.

**R1 / Rn distinction (CS35 + #145 Change 1):** R1 = first review on a given
HEAD; reviewer must enumerate every file under review. Rn = follow-up review
on a delta from the previous round; reviewer may enumerate ONLY the changed
files (delta-only enumeration permitted).

## Plan review (planned/active CS attestation)

Every clickstop file in `project/clickstops/planned/` and
`project/clickstops/active/` carries a `## Plan review` H2 section that
records each independent plan-review round before the file can be merged.
Done files are exempt (the close-out gate `## Plan-vs-implementation review`
already covers that surface).

This is the planning-phase counterpart of the close-out review gate.
Mechanical enforcement: `scripts/check-clickstop-plan-review.mjs` (CS35b),
wired into `harness lint` AND dispatched by the `harness pr-evidence`
aggregator as gate A6 (C35b-9). Doctrine + procedure: see
[OPERATIONS.md § Plan review attestation procedure (CS35b)](OPERATIONS.md).

**Required columns (per CS35b C35b-2):**

| Column | Description |
|---|---|
| Round | `R1`, `R2`, ... — first review then one row per amendment round |
| Reviewer model | The model ID that performed the review (e.g. `gpt-5.5`) |
| Plan author model(s) | Comma-separated model IDs the orchestrator used to author / amend |
| Reviewer agent | Agent identity that ran the review (e.g. `rubber-duck dispatched (orchestrator: yoga-ah)`) |
| Reviewed sections hash | 12-char SHA-256 prefix of trimmed Decisions + Deliverables bodies (`harness plan-review-hash <file>`) |
| Timestamp (UTC) | ISO-8601 UTC, `YYYY-MM-DDThh:mm:ssZ` |
| Verdict | `Go` / `Go-with-amendments` / `Needs-Fix` |
| Findings recap (≤200 chars) | Short summary of the review outcome |

**Independence invariant (C35b-4):** `Reviewer model` MUST NOT appear in
`Plan author model(s)` of the same row OR any earlier row (accumulated
across the file's history). The linter rejects any overlap.

**Hash freshness (C35b-3):** The latest row's `Reviewed sections hash` MUST
equal the current SHA-256-prefix-12 of the Decisions + Deliverables bodies.
Pure prose edits to other sections (Background, Risks, Tasks, Notes) do
NOT change the hash and do NOT require a fresh row. Material edits to
Decisions or Deliverables flip the hash and demand a new attestation
round before the file can be merged.

**Gate (C35b-5):** Latest row's verdict MUST be `Go` or `Go-with-amendments`.
A latest `Needs-Fix` blocks the merge of the plan file (file an amendment
and a new attestation row to clear).

**Narrow re-attest rounds (LRN-135):** When a follow-up plan-review round
addresses ONLY a trivial doc-only delta in response to a prior reviewer
finding — same reviewer model, same reviewer agent, no new scope — it
MAY be filed as a narrow re-attest row (R2/R3/...) with the same
verdict cadence as full rounds. See
[OPERATIONS.md § Narrow re-attest after trivial commits](OPERATIONS.md)
for the procedure, preconditions, and ledger requirements. The PR-side
counterpart is documented under § PR-evidence gates A4 (stale-diff
currency) — narrow re-attest is the recommended mitigation when the
delta would otherwise trigger a full re-review.

Example block (paste into the plan file after `## Decisions`, before
`## Deliverables`; compute the hash via `harness plan-review-hash <file>`):

```
## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.7 | rubber-duck dispatched (orchestrator: yoga-ah) | abcd1234ef56 | 2026-05-13T12:34:56Z | Go | Plan accepted on first round; no amendments. |
```

## PR-evidence gates (B1, A2–A6, A16 reference)

The PR-evidence subcommand (lands in CS36, wired to CI in CS38a) runs a fixed
set of mechanical gates against the diff + git log + PR body of a content PR.
Each gate is named (B1, A2..A6, A16) so CS plans, review log entries, and bug
reports can reference them by short name. The gates are layered: failure of an
earlier gate may shadow later gates' findings, but every reachable gate
reports independently.

| Gate | Name | Source CS | What it checks | C35 anchor |
|---|---|---|---|---|
| B1 | per-commit trailer | CS36 | Every commit in `git log <base>..<head>` (NOT squash-only) carries the `Co-authored-by: Copilot` trailer. The B-prefix is intentional: B-gates inspect the git **branch** / commit graph; A-gates inspect the active CS **audit** artefacts. | C35-5 |
| A2 | per-file enumeration | CS36 | The PR body's "Changes" section enumerates every changed file by path; no summary-pass on YAML/package.json bundles. | #145 PR #28 evidence |
| A3 | model-audit independence | CS36 | The active CS file's `## Model audit` block satisfies the independence invariant: `intersection({Implementer models}, {Reviewer model})` = ∅, case-insensitive on family + version. (Schema presence is implicit — if the table is missing or unparseable, A3 fails with a parse error.) | C35-4 |
| A4 | stale-diff currency | CS36 | The latest `Go` row in `## Review log` has `analyzed_head` equal to the current HEAD SHA of the PR. | C35-3 / C35-6 |
| A5 | review-after-implementation ordering | CS36 (local-Go ordering) + CS37 (Copilot ordering) | The latest local `Go` row's `timestamp` is AFTER the latest commit's authored timestamp on the branch (no review-before-fix). When a Copilot review is present (gate A16), the Copilot review's `submittedAt` must ALSO be after the latest local `Go` row's `timestamp` — the Copilot reviewer cannot precede the local sign-off. | C35-6, ADR-0004 § ADR4-5 |
| A6 | plan-review attestation (PR-time) | CS35b | Every planned/active CS file touched by the PR carries a `## Plan review` row whose `Reviewed sections hash` matches the current content hash AND whose `Reviewer model` ∉ `Plan author model(s)`. STRICT in v0.4.0+v0.5.0 — no `--strict` ramp on this gate (per CS35b C35b-9). | CS35b C35b-2..C35b-9 |
| A16 | Copilot review engagement | CS37 (verifier) + CS41 (engage wrapper) | A Copilot review (state ∈ `APPROVED`/`COMMENTED`/`CHANGES_REQUESTED`, NOT `PENDING`) by `copilot-pull-request-reviewer` (`__typename: Bot`) is present on the PR at the current HEAD per the Copilot engagement procedure (OPERATIONS.md § Copilot engagement procedure). Verification-only on CI via `harness pr-evidence` (which dispatches `scripts/check-copilot-review.mjs` from CS37); engagement mutation lives in `harness copilot-engage` per C35-10 (lands in CS41). | C35-10, ADR-0004 |

Skip-semantics for B1, A2..A6, A16 are centralized via `--skip-reasons <comma-list>`
on `harness pr-evidence` (per C35-19); valid reasons: `workboard-only`,
`bot-author` (C35-8), `fork-source` (C35-9). The CI workflow computes the
reasons from the GitHub event payload; `harness pr-evidence` itself MUST
NOT call `gh pr view`.

### Required PR-side gates

The `review-gates.yml` workflow turns the review doctrine above into four
required status checks for content PRs. PRs labeled `workboard-only` skip these
checks because they are claim/close-out bookkeeping PRs, not implementation
content.

| Status check | How to satisfy it |
|---|---|
| `review-log-evidence` | Fill `## Review log` with at least one non-placeholder row whose verdict is `Go` or `Conditional Go` (the historical `Go-with-amendments` spelling is accepted) and whose reviewer model is GPT-5.5, or an approved fallback with `## Model audit` `Fallback rationale` populated. |
| `copilot-review-attached` | Ensure the configured Copilot PR reviewer has submitted a review. If the gate fails because no review exists yet, it posts `@copilot review`; wait for Copilot to submit and rerun the check. If token permissions prevent the comment, the check remains failed and reports the posting error. Repos without Copilot reviews may set `reviews.require_copilot_review=false`. |
| `independence-invariant` | Fill `## Model audit` with `Implementer models` and `Reviewer model`. The reviewer model must not appear in the implementer list unless the reviewer is GPT-5.5 on a non-HIGH-RISK CS; HIGH-RISK CSs forbid overlap regardless. |
| `review-threads-resolved` | Resolve every GitHub PR review thread before merge. |

`harness init --enable-review-gates` and `harness sync --mode=apply` install the
workflow and add these four contexts to `infra/main-protection-ruleset.json`
when `reviews.enforce_gates=true`.

---

## Config schema: `reviews` vs `review_gates`

`harness.config.json` defines two top-level blocks that govern review
behaviour. They serve different runtimes and are NOT interchangeable —
confusion between them surfaced on SI PR #79 (LRN-136 chase).

| Block | When it runs | Who reads it | Purpose |
|---|---|---|---|
| `review_gates.*` | Install time (`harness init`, `harness sync`) and CI workflow time | `pr-evidence-lint.yml` workflow + install machinery | Configures the **PR-evidence CI gate set** (B1, A2..A6, A16) wired into every content PR. |
| `reviews.*` | Orchestrator runtime (`harness review <pr>`) and PR-side CI status checks | The `harness review` CLI + the PR-side `review-gates.yml` status checks | Configures the **rubber-duck reviewer model defaults**, fallback policy, Copilot trigger, gate enforcement toggles, and HIGH-RISK clickstop list. |

**Rule of thumb:** if you're wiring CI workflow gates or the gate set itself,
edit `review_gates.*`. If you're choosing reviewer models, Copilot wiring,
or PR-side status checks, edit `reviews.*`.

### `review_gates.*` (install-time / CI workflow)

> Per CS38a/CS37/CS36: configuration for the PR-evidence CI workflow that
> wires the harness's gate set into every consumer PR. When `enabled` is true,
> consumers should also land `template/managed/.github/workflows/pr-evidence-lint.yml`
> via `harness sync` (typically opt-in via `harness init --enable-review-gates`).

Fields (descriptions adapted from `schemas/harness.config.schema.json`; schema remains source-of-truth, see file for full constraints and defaults):

- `enabled` (boolean, default `true`): Master switch for the PR-evidence
  gate set. When false, the workflow runs but exits 0 unconditionally (a
  no-op shell). When true, the gate set listed below is enforced.
- `_opt_out_reason` (string): Required when `review_gates.enabled` is false
  in v0.5.0+; records the explicit reason this consumer is opting out of the
  default PR-evidence gate set.
- `copilot_required` (boolean, default `false`): When true, the CS37 A5+A16
  Copilot review gate is included in the gate set; when false, A5+A16 are
  skipped. Forced false when running on fork PRs (per ADR4-6 — forks cannot
  self-engage Copilot under their own token).
- `gate_set` (array of `B1`/`A2`/`A3`/`A4`/`A5`/`A6`/`A16`, default `[]`):
  Explicit list of PR-evidence gate short-names enforced by `harness
  pr-evidence`. Vocabulary defined in [§ PR-evidence gates](#pr-evidence-gates-b1-a2a6-a16-reference).
  Empty array = all gates skipped (gate set disabled). Default when `harness
  init --enable-review-gates` was invoked with the CS37 PASS spike outcome:
  `["B1","A3","A4","A5","A16","A6"]`.

### `reviews.*` (orchestrator-side `harness review` + PR-side gates)

> Configuration for the orchestrator-side `harness review <pr>` command
> (CS52) and CS51 REVIEWS.md PR-side status checks: rubber-duck reviewer
> model defaults, fallback policy inputs, Copilot trigger mode, timeout,
> gate enforcement toggles, and project-specific HIGH-RISK clickstops.

Fields (descriptions adapted from `schemas/harness.config.schema.json`; schema remains source-of-truth):

- `rubber_duck_model` (string, default `gpt-5.5`): Primary rubber-duck
  reviewer model used by `harness review` when `--model` is omitted.
  Defaults to GPT-5.5 per [§ 2.1](#21-review-model).
- `fallback_model` (string, default `sonnet-4.6`): Fallback rubber-duck
  reviewer model allowed only when [§ 2.2](#22-fallback-policy) permits
  fallback and the independence guard passes.
- `enforce_gates` (boolean, default `true`): When true, `harness
  init`/`sync` installs `review-gates.yml` and injects
  `review-log-evidence`, `copilot-review-attached`, `independence-invariant`,
  and `review-threads-resolved` into
  `infra/main-protection-ruleset.json` `required_checks`.
- `require_copilot_review` (boolean, default `true`): When true, the
  `copilot-review-attached` gate requires a submitted review by the
  configured Copilot reviewer; `harness review` also triggers and waits for
  Copilot review evidence unless `--rubber-duck-only` is supplied. Set false
  for repos where Copilot PR reviews are unavailable.
- `copilot_reviewer_slug` (string, default `copilot-pull-request-reviewer[bot]`):
  GitHub login/slug for the Copilot PR reviewer bot. Both the bare slug and
  `[bot]`-suffixed login are accepted by the checker.
- `copilot_trigger` (`mention` | `reviewer`, default `reviewer`):
  Deprecated/no-op. `harness review` always requests the Copilot reviewer via
  the reviewer-attachment path (the hardened `copilot-engage` REST `gh pr edit
  --add-reviewer` path), regardless of this value. `mention` is retained only
  for backward-compatibility — it once posted an ineffective `@copilot review`
  PR comment that never populated `requested_reviewers` (issue #422) — and now
  has no effect.
- `review_timeout_minutes` (number, default `30`): Maximum minutes `harness
  review` waits for required review evidence before returning a
  tooling/transport failure.
- `high_risk_clickstops` (array of CS-ids, default
  `["CS03","CS11","CS15a","CS18b","CS19"]`): Clickstop IDs (for example
  CS03 or CS15a) for which the `independence-invariant` gate forbids
  implementer/reviewer model overlap and `harness review` forbids fallback
  reviewer models unless an explicit user waiver is recorded.

---

## Phase 3 — Close-Out PR Review

The close-out PR archives the clickstop folder and updates `WORKBOARD.md`.
It is also `workboard-only`-labeled and auto-approved by the same workflow
as the claim PR.

Before raising the close-out PR the orchestrator must confirm:

- [ ] All Blocking findings from all content PR rounds are resolved or waived.
- [ ] All Non-blocking findings appear in the retrospective entry under
  `project/clickstops/done/<slug>/`.
- [ ] The WORKBOARD row for this CS is updated to `done`.
- [ ] Any LEARNINGS entries surfaced during the CS are filed in `LEARNINGS.md`.
- [ ] The `harness.lock.json` version is bumped if any managed or composed
  template changed.

---

## Review thread hygiene

All PR review threads must be resolved before merge. The squash-merge
policy (Decision #16, #17) applies:

- **Never** merge with unresolved review threads.
- **Never** merge-commit; squash only.
- If a thread represents a Suggestion the orchestrator has decided to discard,
  resolve it with a one-line explanation (e.g., "Suggestion noted; deferred to
  future CS.").

---

## Independence invariant — full statement

The independence invariant exists to prevent a model from reviewing its own
work. The invariant is violated when the same model (or
model/configuration pair) that materially implemented a CS is also the sole
reviewer of that CS.

"Materially implemented" means: the model authored non-trivial logic, not
merely mechanical find-replace or scaffolding.

Violation handling:
1. If GPT-5.5 is available: use GPT-5.5. No invariant concern (GPT-5.5 is
   never used as an implementer in this harness).
2. If GPT-5.5 is unavailable and Sonnet 4.6 is the candidate fallback:
   check the implementer-model-list for the CS. If Sonnet 4.6 did
   non-trivial implementation work, the fallback is forbidden. Escalate to
   GPT-5.5 retry or user waiver.
3. If neither is available: block the review, do not merge, escalate to user.

Beyond model independence (above), CS35 C35-18 introduces agent-identity
independence: the GitHub usernames of `Implementer agent` and `Reviewer agent`
MUST also differ. The CS41 linter `check-clickstop-implementer-not-reviewer`
enforces this. v0.5.0 ships the columns as required-with-warn-ramp (linters
warn but do not error when columns are missing); v0.6.0 flips to strict per
C42-6, after which missing columns become a hard failure.

---

## Phase matrix — quick reference

| Phase | PR type | Review required | Auto-merge eligible |
|---|---|---|---|
| Claim | `workboard-only` | No | Yes, via `workboard-auto-approve.yml` |
| Content | Normal | Yes — GPT-5.5 GO | No |
| Close-out | `workboard-only` | No (post-review confirmation only) | Yes, via `workboard-auto-approve.yml` |

---

<!-- harness:local-start id=reviews.project-gates -->

## Project-specific review gates

_No project-specific gates are defined yet. Add entries here for gates that
apply to this project but are not universal harness policy. Examples:_

- _"All clickstops that touch Azure deployment configuration require a manual
  approval step from the project owner before the close-out PR is raised."_
- _"Security-sensitive changes (cryptographic primitives, secret handling,
  auth flows) require a dedicated security review round in addition to the
  standard GPT-5.5 content review."_
- _"Any CS that modifies public-facing API schemas must include a
  backwards-compatibility attestation in the PR body."_

_Replace this placeholder paragraph with the actual gates for your project._

<!-- harness:local-end id=reviews.project-gates -->
