namespace AuthzEntitlements.Audit.Service.Domain;

// Size guard for the non-hashed request snapshot persisted per audit row (CS36 / LRN-057,
// Decision #4). The ingest endpoint is anonymous and intra-cluster, so it must never accept an
// UNBOUNDED string that would then be persisted and returned by the query API. A snapshot longer
// than the cap degrades to null (fail-open, per Decision #3) — the row is still audited, and it
// replays via the CS15 best-effort path — rather than storing an over-size blob.
//
// This is the AUTHORITATIVE cap (the PDP's RequestSnapshotSerializer.MaxSnapshotChars mirrors it
// for producer-side reference). The snapshot is NOT part of the tamper-evident hash, so clamping it
// never affects ComputeRowHash or chain verification.
public static class RequestSnapshotGuard
{
    // 16 KB DEFAULT cap. A canonical AccessRequest snapshot is a few hundred bytes in practice; this
    // is a defensive ceiling, not an expected size. The AUTHORITATIVE cap at ingest is now
    // configurable (RequestSnapshotOptions.MaxSnapshotChars, bound from configuration) and defaults to
    // this value; this constant remains the default seed and the persisted column's schema width.
    public const int DefaultMaxSnapshotChars = 16384;

    // Returns the snapshot unchanged when null or within the cap; null when it exceeds the given cap.
    public static string? Clamp(string? snapshot, int maxChars) =>
        snapshot is not null && snapshot.Length > maxChars ? null : snapshot;

    // Convenience overload using the default cap, for callers/tests that do not configure a custom max.
    public static string? Clamp(string? snapshot) => Clamp(snapshot, DefaultMaxSnapshotChars);
}
