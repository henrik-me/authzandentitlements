namespace AuthzEntitlements.Audit.Service.Domain;

// Configuration for the non-hashed request snapshot persisted per audit row (CS36 / LRN-057,
// Decision #4). Bound from the "Audit:RequestSnapshot" configuration section so the authoritative
// ingest size cap is operator-tunable rather than a hard-coded constant, while defaulting to the
// same 16 KB ceiling (RequestSnapshotGuard.DefaultMaxSnapshotChars) — the persisted column's schema
// width. Lowering it drops smaller snapshots to null earlier (fail-open, per Decision #3); it is a
// defensive bound, not an expected size.
public sealed class RequestSnapshotOptions
{
    public const string SectionName = "Audit:RequestSnapshot";

    // Maximum persisted snapshot length in characters. A longer snapshot degrades to null (the row
    // is still audited; replay falls back to best-effort) rather than persisting an over-size blob.
    public int MaxSnapshotChars { get; set; } = RequestSnapshotGuard.DefaultMaxSnapshotChars;

    // The AUTHORITATIVE ingest cap actually applied: clamped to [1, DefaultMaxSnapshotChars] so a
    // misconfigured MaxSnapshotChars can never exceed the persisted column's schema width. Without
    // this clamp a configured value > the column width would let an over-column snapshot through the
    // guard and then FAIL the SaveChanges — the opposite of the fail-open contract (Decision #3).
    // Configuration can only LOWER the effective cap, never raise it above the column width.
    public int EffectiveMaxSnapshotChars =>
        Math.Clamp(MaxSnapshotChars, 1, RequestSnapshotGuard.DefaultMaxSnapshotChars);
}
