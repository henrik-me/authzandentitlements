# CS02 — Fintech back-office domain skeleton

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 0 — Foundations
**Lane:** Foundation
**Depends on:** CS01

## Goal

Model the fintech back-office domain (accounts, transactions, approvals) that exercises every authz layer.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 3286a9c7a517 | 2026-07-02T19:47:54Z | Go-with-amendments | Dependency is right; add explicit maker-checker, SoD, branch/tenant attributes and seed scenarios for later PDP work. |

## Deliverables

- Bank.Api project + EF Core model: Tenants(banks), Users, Roles, Branches/Regions, Accounts, Transactions, Approvals.
- Postgres migrations.
- Seed data: teller/manager/compliance/auditor users, sample accounts + transactions for scenarios.

## Exit criteria

- Migrations apply and seed populates.
- Bank.Api runs under the AppHost with CRUD for core entities.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Define domain entities | pending | — | |
| EF Core mapping + migrations | pending | — | |
| Seed scenario data | pending | — | |
| Expose minimal CRUD endpoints | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
