# CS02 — Fintech back-office domain skeleton

**Status:** active
**Owner:** yoga-ae
**Branch:** cs02/content
**Started:** 2026-07-03
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
| Define domain entities | in_progress | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=pending \| learnings=0 |
| EF Core mapping + migrations | in_progress | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=pending \| learnings=0 |
| Seed scenario data | in_progress | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=pending \| learnings=0 |
| Expose minimal CRUD endpoints | in_progress | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=pending \| learnings=0 |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and any feature docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — populated at the close-out gate (GPT-5.5) before the active → done rename._
