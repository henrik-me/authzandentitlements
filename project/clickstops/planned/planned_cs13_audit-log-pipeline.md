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

## Deliverables

- Audit.Service; hash-chained Postgres audit store (prev-hash + payload -> row-hash).
- Ingestion from PDP decisions, entitlement checks, JIT grants, approvals.
- Chain-verification endpoint + query API.

## Exit criteria

- Decisions produce audit rows; chain verification detects tampering; query API works.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Design hash-chain schema | pending | — | |
| Writer + ingestion | pending | — | |
| Verification endpoint | pending | — | |
| Wire producers | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
