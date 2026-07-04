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

> **Repo-health / DevEx maintenance (yoga-ae-c5 · 2026-07-04, no CS):** yoga-ae-c5 is hardening branch protection and CI merge-gating — wiring required build+test status checks into the "push to main" ruleset so PRs can't merge while CI is red, adding `.github/dependabot.yml`, and triaging the 15 open Dependabot alerts (2 HIGH + 13 medium on MessagePack / OpenTelemetry). Tracked as maintenance, not a clickstop. Other orchestrators: please don't duplicate this — ping yoga-ae-c5 before touching branch-protection, Dependabot, or `.github/workflows/`.

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
| CS23 | CS23 — Comparison matrix + market survey | 🟢 Active | yoga-ae-c2 | cs23/content | 2026-07-04 | — |
| CS21 | CS21 — Break-glass, delegation & on-behalf-of | 🟢 Active | yoga-ae | cs21/content | 2026-07-04 | — |
| CS34 | CS34 — Log-forging (CWE-117) sanitization at the 4 flagged audit-log sites | 🟢 Active | yoga-ae-c5 | cs34/content | 2026-07-04 | — |
| CS25 | CS25 — Managed-vs-self-host TCO + cloud move | 🟢 Active | yoga-ae-c4 | cs25/content | 2026-07-04 | — |
| CS33 | CS33 — Consolidate durable learnings into project-local convention/review doc blocks | 🟢 Active | yoga-ae-c3 | cs33/content | 2026-07-04 | — |

> **Note:** WORKBOARD shows live coordination state only — active orchestrators and their active work. The queue lives in `project/clickstops/planned/` (priority order via filename + per-file `**Depends on:**`); historical record lives in `project/clickstops/done/`. Do not duplicate either here.
