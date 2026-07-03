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
| Define domain entities | done | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=complete \| learnings=3 — tenant/region/branch/role/maker-checker/SoD model |
| EF Core mapping + migrations | done | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=complete \| learnings=3 — BankDbContext + InitialCreate |
| Seed scenario data | done | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=complete \| learnings=3 — deterministic idempotent seed, 3 maker-checker paths |
| Expose minimal CRUD endpoints | done | cs02-domain-impl | agent-id=cs02-domain-impl \| role=implementer \| report-status=complete \| learnings=3 — minimal APIs + approve/reject (SoD+role gate) |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and any feature docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; create planned follow-up CSs for unresolved issues |

## Notes / Learnings

- **Bank.Api** (`src/AuthzEntitlements.Bank.Api`, minimal APIs, net10.0) models the fintech
  back-office domain: `Tenant → Region → Branch`, `User`/`Role`/`UserRole` (RBAC),
  `Account`, `Transaction`, `Approval`. Money uses `decimal(18,2)`; enums persist as strings.
- **Maker-checker + SoD** live on the entities as testable pure logic: `Transaction.Create`
  applies the `BankPolicy.ApprovalThreshold` rule (≥ threshold ⇒ `Pending` + paired `Pending`
  approval; below ⇒ `Posted`); `Approval.Decide` enforces `checker != maker`
  (`SegregationOfDutiesViolationException`). Endpoints add checker-role eligibility
  (BranchManager/ComplianceOfficer). Runtime-verified: teller→403, manager→200 Posted,
  self-approve→409.
- **EF stack** pinned to the .NET 10 RC1 line (`Npgsql.EntityFrameworkCore.PostgreSQL`
  `10.0.0-rc.1`, EF Design `10.0.0-rc.1.25451.107`); plain Npgsql provider reads the
  Aspire-injected `bank` connection string. Migrate + idempotent seed on startup.
- **New advisory CVE-2025-55247** (GHSA-w3q9-fxm7-j8fq, MSBuild pulled transitively by EF
  Core Design) was **remediated — not suppressed** — by pinning patched
  `Microsoft.Build.Tasks.Core`/`Utilities.Core` `17.14.28` via CPM transitive pinning in
  `Directory.Packages.props` (per LRN-003 "pin patched versions via CPM"). Drop the pin in
  CS18 once EF Core Design ships against patched MSBuild. Full learnings filed at close-out.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — populated at the close-out gate (GPT-5.5) before the active → done rename._
