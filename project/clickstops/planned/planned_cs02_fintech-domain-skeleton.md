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
