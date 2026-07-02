# CS13 — Tamper-evident audit log pipeline

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 4 — Observability + audit
**Lane:** Observability
**Depends on:** CS05

## Goal

Implement a tamper-evident, append-only audit log covering every authz/entitlement decision.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 295b3f9cb653 | 2026-07-02T19:47:54Z | Go-with-amendments | Blocker resolved; PDP audit rows are deliverable with CS05, but task wording should clarify PDP producer first. |

## Deliverables

- Audit.Service; hash-chained Postgres audit store (prev-hash + payload -> row-hash).
- Ingestion from PDP decisions first; entitlement/JIT/approval producers wire in as CS10/CS11 land.
- Chain-verification endpoint + query API.

## Exit criteria

- PDP decisions produce audit rows; chain verification detects tampering; query API works (other producers wired as they land).

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Design hash-chain schema | pending | — | |
| Writer + ingestion | pending | — | |
| Verification endpoint | pending | — | |
| Wire producers | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
