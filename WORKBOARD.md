# Work Board

Live coordination file for multi-agent work. Only orchestrating agents update this file.

> **Last updated:** 2026-07-04

## Orchestrators

Status vocabulary: `🟢 Active` (Last Seen within 24h), `🟡 Idle` (24h-7d), `⚪ Offline` (>7d). Agent-ID derivation per [TRACKING.md § Agent Identification](TRACKING.md#agent-identification).

| Agent ID | Machine | Repo Folder | Status | Last Seen |
|----------|---------|-------------|--------|-----------|
| yoga-ae-c3 | yoga | C:\src\authzandentitlements_copilot3 | 🟢 Active | 2026-07-04 |
| yoga-ae-c4 | yoga | C:\src\authzandentitlements_copilot4 | 🟢 Active | 2026-07-04 |
| yoga-ae-c5 | yoga | C:\src\authzandentitlements_copilot5 | 🟢 Active | 2026-07-04 |

> **Repo-health / DevEx maintenance (yoga-ae-c5 · 2026-07-04, no CS):** Branch protection + Dependabot hardened — required build+test checks on the "push to main" ruleset, `.github/dependabot.yml` added, and all 15 open Dependabot alerts fixed. **CS34** (log-forging CWE-117 sanitization) done; **CS40** (review & PR merge-gate hardening) claimed. Removed the ruleset's **"Restrict updates"** rule that was forcing every merge through admin bypass — normal PRs can now merge once their required checks pass. Other orchestrators: ping yoga-ae-c5 before touching branch-protection, Dependabot, or `.github/workflows/`.
>
> **Update (yoga-ae-c5 · 2026-07-05, CS40):** the "push to main" ruleset now **requires 5 status checks** — `build-test`, `structural-gate`, `read-only-gates`, `copilot-review-attached`, `independence-invariant` — plus thread-resolution. **Impact on in-flight PRs:** to merge, your PR now needs (a) a proper review-evidence PR body (`## Model audit` + `## Review log`), (b) an attached **Copilot review** (request `Copilot` as reviewer), and (c) reviewer model ≠ every implementer model. Apply the `workboard-only` label **at PR creation** for workboard PRs so the review-evidence jobs skip cleanly. Policy: `docs/ci/review-pr-hardening.md` (in review, PR #143).

## Active Work

<!--
  Canonical empty state for the Active Work table is **header-only** — when no
  CS is being worked, leave the rows below the separator empty. The
  `check-workboard.mjs` linter accepts the header-only form as the empty state.

  An alternative form using a single em-dash placeholder row
  (`| — | no active CS — populate when claiming | — | — | — | _(set on claim)_ | _(none)_ |`)
  is also accepted by the linter for backward compatibility, but the
  header-only form (the table below this comment, with header + separator
  rows only and no data rows) is the canonical / recommended seed.

  IMPORTANT: do NOT use `_(none)_` (or any other non-em-dash placeholder) in
  the CS-Task ID column — `check-workboard.mjs` requires either a real
  `CS\d{2,}[a-z]?` ID, or an em-dash placeholder paired with a Title that
  contains "no active CS".
-->

| CS-Task ID | Title | State | Owner | Branch | Last Updated | Blocked Reason |
|------------|-------|-------|-------|--------|--------------|----------------|
| CS26 | CS26 — Expansion engines (SpiceDB/Cerbos/Keto/Oso/Topaz) | 🟢 Active | yoga-ae-c4 | cs26/content | 2026-07-04 | — |
| CS40 | CS40 — Review & PR merge-gate hardening (bypass-free normal merges) | 🟢 Active | yoga-ae-c5 | cs40/content | 2026-07-05 | — |
| CS49 | CS49 — Refresh README + ARCHITECTURE to shipped reality (docs) | 🟢 Active | yoga-ae-c3 | cs49/docs-readme-architecture-refresh | 2026-07-04 | — |

> **Note:** WORKBOARD shows live coordination state only — active orchestrators and their active work. The queue lives in `project/clickstops/planned/` (priority order via filename + per-file `**Depends on:**`); historical record lives in `project/clickstops/done/`. Do not duplicate either here.
