using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("AuthzEntitlements.Authz.Pdp.Tests")]

namespace AuthzEntitlements.Authz.Pdp.Contracts;

// Canonical serializer for the full AccessRequest (CS36 / LRN-057). The PDP captures ONE
// deterministic JSON string of the whole request — subject (incl. roles/actor), action, resource
// (incl. amount/maker/status/tenant/branch), and context scopes — alongside each decision so the
// Audit Explorer can replay it faithfully. The string is persisted as a NON-hashed convenience
// column (like ReceivedAtUtc); it never enters the tamper-evident hash chain.
//
// Determinism: a single frozen JsonSerializerOptions on the web defaults (stable camelCase
// property names, no indentation, culture-invariant number handling). Records serialize their
// properties in declaration order, so the same request always yields byte-identical JSON, and the
// camelCase shape round-trips into the mirrored Bank.Web PdpAccessRequestDto for the replay pre-fill.
//
// Fail-OPEN to null (Decision #3): serialization must NEVER throw on the decision hot path or fail
// the audit write. Any serialization failure returns null, and the row is audited without a
// snapshot (replaying via the CS15 best-effort path). This is the deliberate, scoped exception to
// the repo's fail-closed parsing rule — the snapshot is a replay convenience, not the audit-of-record.
public static class RequestSnapshotSerializer
{
    // Upper bound the PRODUCER is aware of; the AUTHORITATIVE guard is server-side in
    // Audit.Service (configurable RequestSnapshotOptions.MaxSnapshotChars, default
    // RequestSnapshotGuard.DefaultMaxSnapshotChars), which drops an over-limit snapshot to null
    // before persisting so the intra-cluster ingest endpoint never stores an unbounded blob.
    // Kept in sync at the 16 KB default. Exposed here for producer-side reference/reuse.
    public const int MaxSnapshotChars = 16384;

    // Web defaults => camelCase, matching every other wire contract in the solution (and the
    // Bank.Web BankJson options that parse the snapshot back for replay). Frozen/shared so the
    // encoding is identical across every call.
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    // Serialize the whole request to canonical JSON, or null on any failure (fail-open).
    public static string? TrySerialize(AccessRequest request) => TrySerialize(request, SnapshotOptions);

    // Testable seam: the same fail-open contract over caller-supplied options, so a contrived
    // serialization failure (e.g. a constrained MaxDepth) can exercise the null path deterministically.
    internal static string? TrySerialize(AccessRequest request, JsonSerializerOptions options)
    {
        if (request is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(request, options);
        }
        catch (Exception)
        {
            // Fail-open (Decision #3): never throw on the decision path — an unserializable request
            // is audited with a null snapshot rather than dropping/faulting the audit write.
            return null;
        }
    }
}
