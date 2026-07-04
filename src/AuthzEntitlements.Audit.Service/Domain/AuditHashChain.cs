using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;

namespace AuthzEntitlements.Audit.Service.Domain;

// Outcome of verifying an ordered audit chain. BrokenAtSequence points at the first entry
// that fails a check (or the expected sequence number for a gap); it is null when Valid.
public sealed record ChainVerificationResult(
    bool Valid, long EntryCount, long? BrokenAtSequence, string? Reason);

// Pure, DB-free core of the tamper-evident audit log. Every function here is deterministic
// and side-effect-free so the chain semantics can be unit-tested by folding an in-memory
// chain, with no EF/Postgres dependency. The row hash binds the sequence, the previous row's
// hash, and EVERY persisted content field, so altering any of them — or reordering, dropping,
// or re-linking entries — is detectable by Verify.
public static class AuditHashChain
{
    // The prev-hash of the very first entry: 64 lowercase '0' hex chars (a SHA-256-width
    // sentinel that no real SHA-256 digest can collide with in practice).
    public const string GenesisPrevHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    // Canonical row hash. Fields are written in a fixed order into a JSON object via
    // Utf8JsonWriter, giving an unambiguous, length-delimited UTF-8 byte representation:
    // string values are quoted/escaped so no field boundary can be forged by shifting content
    // across fields, and null string fields are written as JSON null (distinct from ""). The
    // SHA-256 of those bytes is returned as lowercase hex.
    public static string ComputeRowHash(long sequence, string prevHash, AuditPayload payload)
    {
        ArgumentNullException.ThrowIfNull(prevHash);
        ArgumentNullException.ThrowIfNull(payload);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", sequence);
            writer.WriteString("prevHash", prevHash);
            writer.WriteString(
                "timestampUtc", payload.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("traceId", payload.TraceId);
            writer.WriteString("provider", payload.Provider);
            writer.WriteString("subjectId", payload.SubjectId);
            writer.WriteString("action", payload.Action);
            writer.WriteString("resourceType", payload.ResourceType);
            WriteNullableString(writer, "resourceId", payload.ResourceId);
            writer.WriteString("decision", payload.Decision);
            writer.WriteString("reason", payload.Reason);
            WriteNullableString(writer, "tenant", payload.Tenant);
            writer.WriteString("producer", payload.Producer);
            writer.WriteEndObject();
        }

        var digest = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexStringLower(digest);
    }

    // Fold one payload onto the chain tail. Sequence is monotonic (+1); the new prev-hash is
    // the previous row's hash; the new row hash binds them to the payload. Passing
    // (0, GenesisPrevHash, ...) yields the genesis entry at sequence 1.
    public static (long Sequence, string PrevHash, string RowHash) Append(
        long previousSequence, string previousRowHash, AuditPayload payload)
    {
        var sequence = previousSequence + 1;
        var prevHash = previousRowHash;
        var rowHash = ComputeRowHash(sequence, prevHash, payload);
        return (sequence, prevHash, rowHash);
    }

    // Verify a chain presented in ascending sequence order. Checks: (a) sequences are
    // contiguous starting at 1, (b) the first entry links to the genesis prev-hash, (c) each
    // entry's prev-hash equals the previous entry's row hash, and (d) each entry's stored row
    // hash matches a recomputation over its own fields. An empty chain is trivially valid.
    public static ChainVerificationResult Verify(IReadOnlyList<AuditEntry> orderedBySequence)
    {
        ArgumentNullException.ThrowIfNull(orderedBySequence);

        if (orderedBySequence.Count == 0)
        {
            return new ChainVerificationResult(true, 0, null, null);
        }

        var previousRowHash = GenesisPrevHash;

        for (var index = 0; index < orderedBySequence.Count; index++)
        {
            var entry = orderedBySequence[index];
            var expectedSequence = index + 1;

            if (entry.Sequence != expectedSequence)
            {
                return new ChainVerificationResult(
                    false, orderedBySequence.Count, expectedSequence,
                    $"Non-contiguous sequence: expected {expectedSequence}, found {entry.Sequence}.");
            }

            if (entry.PrevHash != previousRowHash)
            {
                var reason = index == 0
                    ? "First entry prev-hash does not equal the genesis prev-hash."
                    : "Prev-hash does not equal the previous entry's row hash.";
                return new ChainVerificationResult(false, orderedBySequence.Count, entry.Sequence, reason);
            }

            var recomputed = ComputeRowHash(entry.Sequence, entry.PrevHash, PayloadOf(entry));
            if (recomputed != entry.RowHash)
            {
                return new ChainVerificationResult(
                    false, orderedBySequence.Count, entry.Sequence,
                    "Row hash does not match a recomputation over the entry's fields.");
            }

            previousRowHash = entry.RowHash;
        }

        return new ChainVerificationResult(true, orderedBySequence.Count, null, null);
    }

    // Extract the hashed content view of a persisted entry (everything except the chain
    // linkage and the server-only ReceivedAtUtc, which is not part of the hash).
    public static AuditPayload PayloadOf(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new AuditPayload(
            entry.TimestampUtc,
            entry.TraceId,
            entry.Provider,
            entry.SubjectId,
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            entry.Decision,
            entry.Reason,
            entry.Tenant,
            entry.Producer);
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
