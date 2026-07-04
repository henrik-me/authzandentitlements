# NuGet audit re-evaluation — 2026-07-04 (CS30)

> **Scope:** the CS30 supply-chain re-evaluation of this repo's `NuGetAuditSuppress`
> entries and CPM transitive pins. It records which advisories were **remediated** by
> moving to patched package versions (so their suppressions were dropped) and which pin
> is **retained** with a dated justification, so the build-time audit gate stays
> meaningful rather than accumulating stale suppressions. It closes the **LRN-003** and
> **LRN-005** re-evaluation debt tracked by the **CS30** clickstop plan.

## Status & scope

- **As of:** CS30 (supply-chain re-evaluation). Fixes **LRN-003** (15 suppressed
  advisories) and **LRN-005** (the MSBuild transitive pin).
- **Principle:** remediate, don't perpetually suppress. A `NuGetAuditSuppress` entry
  acknowledges a *live* advisory; a patched-version pin *removes* it. Where a patched
  stable version exists, we drop the suppression and move to the fix; where it does not,
  we retain the pin with a dated "still required" note plus the advisory link (CS30
  Decision 2).
- **Enforcement point:** `Directory.Build.props` sets `TreatWarningsAsErrors=true`,
  which promotes the NuGet audit warnings NU1902 (moderate/low) and NU1903
  (high/critical) to build **errors** at restore time. Any NEW advisory — on these or
  any other package — still fails the build.

## Method

1. Enumerate the current restore graph and its advisories:
   `dotnet list AuthzEntitlements.sln package --vulnerable --include-transitive`.
2. Look up each advisory's patched version via the GitHub Advisory API and check for an
   available **stable** (non-preview) release that clears it.
3. Apply the fix — a direct version bump or a CPM transitive pin — in
   `Directory.Packages.props`, then re-run the scan.
4. Confirm fail-closed: `dotnet build AuthzEntitlements.sln` stays clean under
   `TreatWarningsAsErrors` and `--vulnerable` reports nothing new.

## Disposition

| Package (source) | Advisories | Prior handling | Disposition (2026-07-04) |
|---|---|---|---|
| OpenTelemetry — Exporter / Extensions.Hosting / Instrumentation.AspNetCore / Http / Runtime (direct, via the aspire-servicedefaults template) | 4 moderate: [`GHSA-q834-8qmm-v933`](https://github.com/advisories/GHSA-q834-8qmm-v933), [`GHSA-mr8r-92fq-pj8p`](https://github.com/advisories/GHSA-mr8r-92fq-pj8p), [`GHSA-4625-4j76-fww9`](https://github.com/advisories/GHSA-4625-4j76-fww9), [`GHSA-g94r-2vxg-569j`](https://github.com/advisories/GHSA-g94r-2vxg-569j) | 4 `NuGetAuditSuppress` on 1.14.0 | **Resolved.** Bumped the OTel stack to **1.16.0** (Exporter, Extensions.Hosting, Instrumentation.AspNetCore, Http); Instrumentation.Runtime to **1.15.1** — its latest stable, since 1.16.0 is not published for that package, and it only needs to sit above the OTel.Api patched floor that the 1.16.0 core packages already satisfy transitively. All four advisories have a first-patched version of 1.15.2 or 1.15.3, so the 1.16.0 bump clears them. Suppressions dropped. |
| MessagePack (transitive, via `Aspire.AppHost.Sdk` dashboard/DCP) | 11 (2 high + 9 moderate): [`GHSA-hv8m-jj95-wg3x`](https://github.com/advisories/GHSA-hv8m-jj95-wg3x), [`GHSA-vh6j-jc39-fggf`](https://github.com/advisories/GHSA-vh6j-jc39-fggf), [`GHSA-2f33-pr97-265q`](https://github.com/advisories/GHSA-2f33-pr97-265q), [`GHSA-v72x-2h86-7f8m`](https://github.com/advisories/GHSA-v72x-2h86-7f8m), [`GHSA-2x83-8g95-xh59`](https://github.com/advisories/GHSA-2x83-8g95-xh59), [`GHSA-cj9g-3mj2-g8vv`](https://github.com/advisories/GHSA-cj9g-3mj2-g8vv), [`GHSA-wfr3-xj75-pfwh`](https://github.com/advisories/GHSA-wfr3-xj75-pfwh), [`GHSA-w567-gjr2-hm5j`](https://github.com/advisories/GHSA-w567-gjr2-hm5j), [`GHSA-cxmj-83gh-fp49`](https://github.com/advisories/GHSA-cxmj-83gh-fp49), [`GHSA-q2h6-ghwm-5qm8`](https://github.com/advisories/GHSA-q2h6-ghwm-5qm8), [`GHSA-qhmf-xw27-6rqr`](https://github.com/advisories/GHSA-qhmf-xw27-6rqr) | 11 `NuGetAuditSuppress` on transitive 2.5.192 | **Resolved.** CPM transitive pin **MessagePack 2.5.302** — 2.5.301 is the first patched 2.x release; 2.5.302 is the latest 2.x, staying on the 2.x major for Aspire compatibility. This *remediates*, not suppresses. All eleven are cleared; suppressions dropped. |
| Microsoft.Build.Tasks.Core / Microsoft.Build.Utilities.Core (transitive, via `Microsoft.EntityFrameworkCore.Design` 10.0.0-rc.1) | 1 high: [`GHSA-w3q9-fxm7-j8fq`](https://github.com/advisories/GHSA-w3q9-fxm7-j8fq) (CVE-2025-55247; Linux-only design-time MSBuild temp-dir DoS) | CPM transitive pin **17.14.28** | **Retained.** EF Core Design 10.0.0-rc.1 still resolves these packages to the vulnerable **17.14.8** without the pin (verified: dropping it makes the NU1903 High reappear). Kept at the patched **17.14.28** with a dated re-confirmation note. |

## Outcome

- All **15** prior `NuGetAuditSuppress` entries (4 OpenTelemetry + 11 MessagePack) are
  dropped from `Directory.Build.props`; no suppression entries remain.
- `dotnet list AuthzEntitlements.sln package --vulnerable --include-transitive` reports
  **no vulnerable packages** across all 20 projects.
- `dotnet build AuthzEntitlements.sln` is clean (**0 warnings / 0 errors**) under
  `TreatWarningsAsErrors`, proving the dropped suppressions were safe to remove.
- One transitive pin — Microsoft.Build.* **17.14.28** — is **retained** with a dated
  justification (a still-live advisory, no GA fix available yet).

## Next re-evaluation trigger

Revisit this note when either upstream ships a fix that lets a pin drop:

- When the EF Core / .NET stack moves off **10.0.0-rc.1** to a GA build that references
  patched MSBuild packages, drop the Microsoft.Build.* 17.14.28 pin and re-run the
  `--vulnerable` scan.
- When a **stable** Aspire release ships a patched MessagePack (≥ 2.5.301 transitively),
  drop the MessagePack 2.5.302 pin.

## References

- **CS30 clickstop plan** (`project/clickstops/` — `active/` while in flight, `done/` after
  close-out) — goal, decisions, deliverables, and exit criteria.
- [`Directory.Packages.props`](../../Directory.Packages.props) — the OTel version bumps
  and the MessagePack / Microsoft.Build.* transitive pins.
- [`Directory.Build.props`](../../Directory.Build.props) — `TreatWarningsAsErrors` and
  the (now suppression-free) audit posture.
- [threat-model.md](threat-model.md) — the STRIDE threat model this note complements.
