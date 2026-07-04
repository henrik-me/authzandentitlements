# Work Board

Live coordination file for multi-agent work. Only orchestrating agents update this file.

> **Last updated:** 2026-07-04

## Orchestrators

Status vocabulary: `🟢 Active` (Last Seen within 24h), `🟡 Idle` (24h-7d), `⚪ Offline` (>7d). Agent-ID derivation per [TRACKING.md § Agent Identification](TRACKING.md#agent-identification).

| Agent ID | Machine | Repo Folder | Status | Last Seen |
|----------|---------|-------------|--------|-----------|
| yoga-ae-c3 | yoga | C:\src\authzandentitlements_copilot3 | 🟢 Active | 2026-07-04 |

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
| CS22 | CS22 — Compliance mapping (SOX/PCI-DSS/GDPR) | 🟢 Active | yoga-ae-c4 | cs22/content | 2026-07-04 | — |
| CS15 | CS15 — AuthZ playground + audit explorer | 🟢 Active | yoga-ae-c2 | cs15/content | 2026-07-04 | — |
| CS19 | CS19 — Agent + non-agent access | 🟢 Active | yoga-ae | cs19/content | 2026-07-04 | — |

> **Note:** WORKBOARD shows live coordination state only — active orchestrators and their active work. The queue lives in `project/clickstops/planned/` (priority order via filename + per-file `**Depends on:**`); historical record lives in `project/clickstops/done/`. Do not duplicate either here.
