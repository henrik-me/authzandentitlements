# Learnings

Learnings filed during the project. See [`RETROSPECTIVES.md`](RETROSPECTIVES.md) for harvest procedure and entry format.

---

> **ID sequencing:** Use sequential IDs starting from LRN-001. The linter emits
> warnings for gaps in the sequence but treats them as non-fatal; gaps do not
> cause exit code 1.

---

## Open

### LRN-001

```yaml
id: LRN-001
date: 2026-07-03
category: tooling
source_cs: CS01
status: open
tags: [aspire, dotnet, scaffolding, windows]
```

**Problem:** Scaffolding the Aspire foundations required generating the AppHost/ServiceDefaults projects and adding the Postgres hosting integration from a non-interactive agent shell.

**Finding:** Aspire CLI 13.1.0 `aspire add <integration>` aborts with exit 5 ("Interactive input is not supported in this environment") even with `--non-interactive --version` — use `dotnet add package Aspire.Hosting.<X> --version <apphost-sdk-band>` instead. The `aspire-apphost` template emits `AppHost.cs` (not `Program.cs`) and a single-SDK csproj (`Sdk="Aspire.AppHost.Sdk/13.1.0"`, no explicit `Aspire.Hosting.AppHost` PackageReference). `dotnet new` on Windows emits CRLF, so authored files must be normalized to LF/no-BOM for the text-encoding gate.

**Evidence:** PR #2 (commit `3919a03`); sub-agent `cs01-aspire-scaffold` report; two failed `aspire add postgres` invocations (exit 5).

**Implications carried forward:**
- CS02+ scaffolding should use `dotnet add package` for Aspire integrations and normalize generated files to LF.

### LRN-002

```yaml
id: LRN-002
date: 2026-07-03
category: tooling
source_cs: CS01
status: open
tags: [nuget, cpm, build, aspire]
```

**Problem:** With `TreatWarningsAsErrors=true` and Central Package Management, a clean `dotnet build` on preview / .NET 10 RC packages was impossible because NuGet audit advisories are promoted to build errors at restore time.

**Finding:** Suppress the SPECIFIC advisory IDs via `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-..." />` items in `Directory.Build.props` — NOT a blanket `NoWarn=NU1902;NU1903`, which would also mask future advisories in later projects. Per-advisory suppression keeps auditing active so any new advisory still fails the build.

**Evidence:** PR #2; `Directory.Build.props`; GPT-5.5 review (sub-agent `cs01-review`) finding #1; `dotnet list package --vulnerable --include-transitive`.

**Implications carried forward:**
- CS02+ adding preview packages will hit the same and can reuse the per-advisory pattern.

### LRN-003

```yaml
id: LRN-003
date: 2026-07-03
category: architectural
source_cs: CS01
status: open
tags: [security, nuget, aspire, opentelemetry]
claim_area: security-hardening
```

**Problem:** CS01 ships with known-vulnerable preview packages whose advisories are suppressed to achieve a clean build; this defers real supply-chain risk rather than resolving it.

**Finding:** 15 NuGet audit advisories across 3 packages are suppressed: MessagePack 2.5.192 (2 High + 9 Moderate, transitive via `Aspire.AppHost.Sdk` dashboard/DCP — not directly controllable) and OpenTelemetry 1.14.0 exporter + Api (4 Moderate, direct via the `aspire-servicedefaults` template). They are localhost-only dev-loop packages in CS01 with no untrusted-input path.

**Evidence:** PR #2; `Directory.Build.props` NuGetAuditSuppress list; `done/done_cs01_aspire-foundations.md` Notes.

**Implications carried forward:**
- CS18 (security hardening) must revisit: drop suppression entries as non-vulnerable stable Aspire 13 / OTel packages ship, or pin patched versions via CPM.

### LRN-004

```yaml
id: LRN-004
date: 2026-07-03
category: tooling
source_cs: CS02
status: open
tags: [efcore, npgsql, concurrency, dotnet]
```

**Problem:** CS02 needed Postgres optimistic concurrency on the `Approval` row to close a maker-checker double-decide race, but the standard Npgsql helper was unavailable on the pinned RC stack.

**Finding:** `UseXminAsConcurrencyToken()` was **removed** in `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0-rc.1 (verified absent from the assembly). The replacement is a shadow row-version mapped to the hidden system column: `entity.Property<uint>("xmin").IsRowVersion();`. Npgsql maps it to the Postgres system `xmin` column, so the generated SQL (`dotnet ef migrations script`) has **no physical `xmin` column** even though the migration C# shows `xmin = table.Column<uint>(type:"xid", rowVersion:true)`. A Copilot review flagged the migration C# as creating a physical column — a false positive (empirical `CREATE TABLE "Approvals"` SQL has no such column, and the migration applies cleanly).

**Evidence:** PR #5 (fix commit `9420dc4`); `src/AuthzEntitlements.Bank.Api/Data/BankDbContext.cs:161`; `dotnet ef migrations script` output.

**Implications carried forward:**
- Later EF Core optimistic-concurrency work on Postgres should use `Property<uint>("xmin").IsRowVersion()` and verify the generated SQL (not the migration C#) to confirm the system-column mapping.

### LRN-005

```yaml
id: LRN-005
date: 2026-07-03
category: tooling
source_cs: CS02
status: open
tags: [nuget, cpm, security, efcore, msbuild]
claim_area: security-hardening
```

**Problem:** Under `TreatWarningsAsErrors` + CPM, a clean build broke when EF Core Design 10.0.0-rc.1 dragged in a newly-advisory'd transitive MSBuild package.

**Finding:** `Microsoft.EntityFrameworkCore.Design` 10.0.0-rc.1 pulls `Microsoft.Build.Tasks.Core`/`Microsoft.Build.Utilities.Core` 17.14.8, carrying **CVE-2025-55247 / GHSA-w3q9-fxm7-j8fq** (High; Linux-only, design-time MSBuild temp-dir DoS). Remediate — do NOT suppress — by pinning the patched `17.14.28` on the same minor line via CPM **transitive pinning** (`CentralPackageTransitivePinningEnabled=true` + `PackageVersion` entries). This removes the advisory from `dotnet list package --vulnerable` (a real fix), unlike `NuGetAuditSuppress`.

**Evidence:** PR #5; `Directory.Packages.props` (transitive pins + `CentralPackageTransitivePinningEnabled`); `dotnet list package --vulnerable --include-transitive` (CVE absent post-pin).

**Implications carried forward:**
- CS18 (security hardening): drop the MSBuild pin once EF Core Design ships against patched MSBuild.
- Prefer patched-version CPM transitive pinning over suppression for any new transitive advisory (extends LRN-002/003).

### LRN-006

```yaml
id: LRN-006
date: 2026-07-03
category: tooling
source_cs: CS02
status: open
tags: [harness, review, cli, escalation]
```

**Problem:** The `harness review <pr>` verb could not be used for the CS02 content review.

**Finding:** `harness review <pr>` (agent-harness v0.12.0) non-dry-run path aborts with "Could not find clickstop file for CS02 under project/clickstops/{active,planned,done}" even though `active_cs02_fintech-domain-skeleton.md` exists on both the PR branch and `main`; the `--dry-run` variant succeeds. Worked around by dispatching the GPT-5.5 reviewer sub-agent directly with the canonical reviewer preamble (OPERATIONS.md § Reviewer dispatch) and recording the verdict manually in the PR Review log. This is a harness (`lib/`) bug — out of scope to fix in-band (Hard Rule §3); escalate to the harness maintainer (filed upstream as `henrik-me/agent-harness#407`).

**Evidence:** this session; `harness review 5 --rubber-duck-only --no-poll` → exit 2 with the lookup error; `--dry-run` variant → exit 0; file present via `git ls-files`.

**Implications carried forward:**
- Until fixed upstream, run content-PR rubber-duck reviews by dispatching the reviewer sub-agent directly (canonical preamble) rather than via `harness review`.
- Tracked upstream at `henrik-me/agent-harness#407`; revisit this workaround once that issue is resolved and the harness pin is bumped.

### LRN-007

```yaml
id: LRN-007
date: 2026-07-03
category: process
source_cs: CS02
status: open
tags: [review, verification, efcore]
```

**Problem:** CS02 drew findings from two independent reviewers of very different quality; acting on all blindly — or dismissing all blindly — would both have been wrong.

**Finding:** The independent GPT-5.5 review (R1) caught two REAL blocking bugs (in-memory-only double-decide race; caller-controlled, unconstrained tenant/branch). Copilot's 6 comments were ALL false positives, disproven empirically: (a) `.Select(x => x.ToDto()).ToListAsync()` on an EF `IQueryable` does NOT throw — EF Core client-evaluates the **top-level projection** (all list endpoints returned 200 with data); (b) the `xmin` row-version maps to the Postgres system column (no physical column; see LRN-004). Lesson: **verify review comments against the running system** (build/test/curl) before either fixing or dismissing — a confident "will throw at runtime" claim was falsified by one `GET`.

**Evidence:** PR #5 Copilot review (6 comments, all resolved with an evidence comment); live `GET /api/{tenants,branches,accounts,transactions}` → 200; `dotnet ef migrations script` (no physical `xmin` column).

**Implications carried forward:**
- Keep the independent-model rubber-duck as the review-of-record; treat automated-reviewer comments as leads to verify, not directives.

## Applied

_(no entries yet)_

## Obsolete

_(no entries yet)_

## Deferred

_(no entries yet)_