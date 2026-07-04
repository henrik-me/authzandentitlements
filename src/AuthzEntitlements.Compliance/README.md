# AuthzEntitlements.Compliance

A standalone .NET console tool that produces a **compliance evidence pack** for the
authorization/entitlements lab, mapping shipped controls to regulatory frameworks
(SOX / PCI-DSS / GDPR) with **runnable evidence** produced by the system's own code.

It mirrors the `AuthzEntitlements.Benchmarks` harness: a zero-external-dependency console +
library that drives in-process PDP/audit surfaces, plus optional live probes that self-skip
when the target service is offline.

## The four report sections

1. **Segregation-of-duties evidence (deterministic, always produced).**
   Drives the pure `GovernanceSodPolicy.FindConflict` over every incompatible role pair and
   representative clean/single/empty sets, AND drives the in-process
   `ReferenceDecisionProvider` on `governance.access.request` (expecting `Deny` +
   `SodConflict` for toxic combinations and `Permit` for independent sets). Also maps the
   Bank.Api maker-checker control (checker ≠ maker; the 10,000 approval threshold) to its
   enforcement point in `ReferenceDecisionProvider`.

2. **Audit-integrity evidence (deterministic, always produced).**
   Folds a small in-memory chain with the pure `AuditHashChain`, verifies it (expects
   valid), then applies each tamper the append-only log must detect — a content-field
   mutation, a tail truncation caught by a trusted `AuditCheckpoint`, a sequence gap, and a
   broken prev-hash link — recording the resulting `ChainVerificationResult` for each.

3. **Access-certification evidence (live probe, self-skips offline).**
   When `--governance-url` is supplied and reachable, GETs
   `/api/governance/review-campaigns` and summarizes each campaign's items
   (certified / revoked / pending). Offline → a `collected=false` block with the exact
   reproduction command. A reached-but-malformed response fails closed (clear error, exit 1).

4. **Least-privilege attestation (live probe, self-skips offline).**
   When reachable, GETs `/api/governance/access-packages` and, for a representative seeded
   principal, `/api/governance/principals/{id}/grants`, attesting active vs expired
   (time-bound / JIT) access. Offline → a `collected=false` block with reproduction steps.

Sections 1 and 2 need no database, no Docker, and no live services — they are pure functions
of the shipped domain code, so the evidence is reproducible in CI.

## Running

Default — print the Markdown evidence pack (deterministic sections populated, live sections
self-skipped) to stdout:

```
dotnet run --project src/AuthzEntitlements.Compliance
```

Write `compliance-report.json` (camelCase, `schemaVersion`, fail-closed reader) and
`compliance-report.md` to a directory:

```
dotnet run --project src/AuthzEntitlements.Compliance -- --output ./artifacts/compliance
```

Collect the live sections against a running stack (start it with `aspire run`, then point the
tool at the Governance service base URL):

```
aspire run
dotnet run --project src/AuthzEntitlements.Compliance -- --governance-url http://localhost:5300
```

Probe a specific seeded principal for the least-privilege section:

```
dotnet run --project src/AuthzEntitlements.Compliance -- \
  --governance-url http://localhost:5300 --principal user-manager1
```

`--help` prints usage and exits 0.

## Exit codes

- `0` — report produced (live sections may be self-skipped).
- `1` — a reached Governance response could not be parsed (fail-closed), or an artifact could
  not be loaded.
- `2` — invalid command line.
