# Work Board

Live coordination file for multi-agent work. Only orchestrating agents update this file.

> **Last updated:** _(set on first edit)_

## Orchestrators

Status vocabulary: `🟢 Active` (Last Seen within 24h), `🟡 Idle` (24h-7d), `⚪ Offline` (>7d). Agent-ID derivation per [TRACKING.md § Agent Identification](TRACKING.md#agent-identification).

| Agent ID | Machine | Repo Folder | Status | Last Seen |
|----------|---------|-------------|--------|-----------|
| _(placeholder — replace with real agent)_ | _(hostname)_ | _(path)_ | ⚪ Offline | _(never)_ |

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
| CS07 | CS07 — Adapter: OpenFGA (ReBAC / Zanzibar) | 🟢 Active | yoga-ae-c2 | cs07/content | 2026-07-03 | — |
| CS08 | CS08 — Adapter: OPA / Rego (policy / ABAC) | 🟢 Active | yoga-ae | cs08/content | 2026-07-03 | — |

> **Note:** WORKBOARD shows live coordination state only — active orchestrators and their active work. The queue lives in `project/clickstops/planned/` (priority order via filename + per-file `**Depends on:**`); historical record lives in `project/clickstops/done/`. Do not duplicate either here.
