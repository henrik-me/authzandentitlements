# Test coverage

This repository measures code coverage with **coverlet** (via the built-in
`XPlat Code Coverage` data collector) and reports it with **ReportGenerator**.
The infrastructure was introduced in CS52 Wave 0 (Decisions #4/#5/#6). No
coverage tooling existed before that point, so the numbers below are the first
recorded baseline.

## How to run coverage locally

From the repository root:

```powershell
# 1. Collect Cobertura coverage for the whole solution.
dotnet test AuthzEntitlements.sln --settings coverage.runsettings --collect:"XPlat Code Coverage"

# 2. Restore the local dotnet tools (one-time; includes ReportGenerator).
dotnet tool restore

# 3. Merge every per-project Cobertura file into a human-readable summary + HTML.
dotnet tool run reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report -reporttypes:"TextSummary;Html"
```

Each test project drops a `coverage.cobertura.xml` under
`tests/<project>/TestResults/<guid>/`. ReportGenerator merges all of them and
writes `coverage-report/Summary.txt` (per-assembly line %) plus a browsable
`coverage-report/index.html`. Both `TestResults/` and `coverage-report/` are
build artifacts — do not commit them.

The collector configuration lives in [`coverage.runsettings`](../../coverage.runsettings)
at the repository root; the `coverlet.collector` package is pinned centrally in
`Directory.Packages.props` and wired into every test project through
`tests/Directory.Build.props`, so no per-project edit is needed.

## Scope: business-logic assemblies + exclusions (Decision #5)

Coverage is targeted at the nine **business-logic assemblies**, where the
number is meaningful:

- `AuthzEntitlements.Authz.Pdp`
- `AuthzEntitlements.Bank.Api`
- `AuthzEntitlements.Bank.Web`
- `AuthzEntitlements.Entitlements.Service`
- `AuthzEntitlements.Governance.Service`
- `AuthzEntitlements.Audit.Service`
- `AuthzEntitlements.Compliance`
- `AuthzEntitlements.Edge.Gateway`
- `AuthzEntitlements.ServiceDefaults`

The following are **excluded** because a line-coverage target over them would
be a false signal that drives low-value tests:

| Excluded | How | Why |
| --- | --- | --- |
| `*.AppHost` | `<Exclude>[*.AppHost]*` | Aspire topology / orchestration — asserted by app-model smoke tests, not line coverage. |
| `*.Benchmarks` | `<Exclude>[*.Benchmarks]*` | Performance harness, not shipped logic. |
| EF `Migrations/*.cs` | `<ExcludeByFile>**/Migrations/*.cs` + `<Exclude>[*]*.Migrations.*` | Generated migration code. |
| Test assemblies | `<Exclude>[*.Tests]*` | The tests themselves are not the subject under test. |
| Generated / opt-out members | `<ExcludeByAttribute>Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute,ExcludeFromCodeCoverage` | Compiler-generated and explicitly opted-out code. |

## Baseline (captured on `cs52/content`, 2026-07-05)

Baseline captured with the .NET SDK `10.0.100-rc.1` toolchain, coverlet
`6.0.4`, and ReportGenerator `5.4.7`, over the offline default test path
(live-engine adapter tests self-skip when their servers are unreachable, so
adapter branches exercised only against a running server are not represented
here). `ServiceDefaults` has effectively no tests yet — its low number is
recorded honestly and is the motivation for `ServiceDefaults.Tests` in Wave A
(Decision #8).

| Assembly | Line % | Branch % |
| --- | ---: | ---: |
| AuthzEntitlements.Audit.Service | 81.2 | 100.0 |
| AuthzEntitlements.Authz.Pdp | 81.9 | 84.2 |
| AuthzEntitlements.Bank.Api | 33.2 | 86.2 |
| AuthzEntitlements.Bank.Web | 35.0 | 33.8 |
| AuthzEntitlements.Compliance | 89.6 | 75.0 |
| AuthzEntitlements.Edge.Gateway | 79.8 | 93.9 |
| AuthzEntitlements.Entitlements.Service | 43.4 | 73.7 |
| AuthzEntitlements.Governance.Service | 91.2 | 81.6 |
| AuthzEntitlements.ServiceDefaults | 1.4 | 14.3 |
| **Overall** | **69.8** | **71.0** |

Overall counted totals: 5322 / 7614 coverable lines; 1342 / 1888 branches.

## Ratchet policy (Decision #6)

Enforcement is a **ratchet, not a big-bang**:

- Per-assembly floors **start at the baseline** above (a non-regression gate):
  a PR may not drop any assembly below its recorded floor.
- Floors are **raised per wave** toward the target of **95% line / 90% branch**
  on the business-logic assemblies.
- Floors are **monotonic** — once raised, they are never lowered.
- The CI gate ships **report-only first** (one PR, to shake out collector/SDK
  issues), then flips to **blocking** in a separate, reviewed step.

## Wave plan (Decision #1 / D4)

Implementation is deliberately sequenced into reviewable waves; Wave 0 (this
change) delivers only the measurement infrastructure and baseline.

- **Wave 0 (D0/D1) — this change:** coverlet + `coverage.runsettings` +
  ReportGenerator tool + `tests/Directory.Build.props` + baseline + this doc.
- **Wave A (D3/D4):** `ServiceDefaults.Tests` (Decision #8) + Entitlements.Service
  endpoint/metering/feature tests + Bank.Api HTTP-level tests and seams
  (highest deficit × risk).
- **Wave B (D3/D4):** Governance endpoint + Bank.Web page-handler tests +
  Edge/Audit branch gaps.
- **Wave C (D3/D4):** Compliance, Benchmarks, AppHost app-model assertions, and
  residual PDP branches.

## CI enforcement — deferred, coordinated follow-up

Wiring a coverage step (report-only first, then blocking) into
`.github/workflows/dotnet-ci.yml` is a **deliberately deferred, coordinated
follow-up**. It is intentionally **not** included in this change: workflow-file
coordination is owned by a separate orchestrator (yoga-ae-c5), and the CI step
lands only after sign-off from that owner. Until then, coverage is a
locally-run, report-only measurement.
