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
| Close-out: docs + restart state | done | yoga-ae | WORKBOARD row removed; CONTEXT.md refreshed (CS02 done, next claimable, test count) |
| Close-out: learnings + follow-ups | done | yoga-ae | Filed LRN-004..007 in LEARNINGS.md (EF/Npgsql xmin, CVE-2025-55247 CPM pin, harness review bug, review-verification) |

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

**Reviewer:** GPT-5.5 (rubber-duck, independent — sub-agent `cs02-pvi`)
**Date:** 2026-07-03T05:40:00Z
**Outcome:** GO

Per-deliverable outcome (plan vs. merged content `dc5c116..b095bb5`):

| Planned item | Outcome | Evidence / rationale |
|---|---|---|
| Bank.Api project + EF Core model (Tenants, Users, Roles, Branches/Regions, Accounts, Transactions, Approvals) | match | `BankDbContext` exposes all planned DbSets + mappings. |
| Postgres migrations | match | `InitialCreate` creates the domain tables + FKs; EF reports no pending model changes. |
| Seed data (teller/manager/compliance/auditor users, sample accounts + transactions) | match | `BankSeeder` creates roles/users/accounts + the three transaction scenarios. |
| Exit: migrations apply and seed populates | match | Startup calls `MigrateAsync()` then `BankSeeder.SeedAsync()`; runtime-verified against Postgres 17. |
| Exit: Bank.Api runs under AppHost with CRUD/core endpoints | match | AppHost wires `bank-api` to the `bank` DB; endpoints map accounts/reference/transactions/approve-reject. |
| Amendment: explicit maker-checker threshold | match | `Transaction.Create` posts below-threshold, `Pending` + `Approval` at/above threshold. |
| Amendment: segregation of duties | match | `Approval.Decide` rejects `checker == maker`; unit-tested. |
| Amendment: branch/tenant attributes | match | Transaction derives tenant/branch/currency from the account; tenant/branch FKs enforced. |
| Amendment: seed scenarios | match | Seeder includes posted-below, pending-at/above, and manager-approved paths. |
| Optimistic approval concurrency | added | Approval `xmin` row-version + endpoint 409 on conflict. |
| Tenant-integrity / FK hardening | added | Account + transaction tenant/branch FKs (Restrict). |
| CVE-2025-55247 remediation via CPM pins | added | Patched MSBuild transitive pins in `Directory.Packages.props`. |

**Test coverage:** sufficient — CS01 had 0 tests; CS02 adds 7 xUnit tests covering maker-checker,
SoD, approval/rejection transitions, threshold behaviour, and tenant/branch/currency stamping.
Endpoint-level tenant-scoping + optimistic concurrency were runtime-verified (not unit-tested) —
acceptable for a foundational domain skeleton; later authz/API CSs should add integration coverage.

**Review row:** model=`gpt-5.5` · branch HEAD SHA=`b095bb5d29245aa2b0e57fc9c946f1a61e672058` ·
R-round=`R1` · verdict=`Go` · evidence=`src/AuthzEntitlements.Bank.Api/Program.cs:22-29` /
commit `b095bb5`.
