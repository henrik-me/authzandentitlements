using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;

namespace AuthzEntitlements.Audit.Service.Domain;

// A trusted checkpoint a caller retains out-of-band (e.g. the (Sequence, RowHash) returned by a
// prior ingest or verify). Supplying it to Verify closes the two gaps a bare hash chain cannot
// self-detect: tail truncation (deleting the newest rows leaves a still-contiguous prefix) and a
// full-suffix rewrite (an actor able to rewrite EVERY row from some point on re-links a
// self-consistent chain). Both change the row hash at the checkpoint sequence, so the anchor
// catches them.
public sealed record AuditCheckpoint(long Sequence, string RowHash);

// Outcome of verifying an ordered audit chain. BrokenAtSequence points at the first entry that
// fails a check (or the expected sequence for a gap / a failed checkpoint); it is null when Valid.
// TailSequence/TailRowHash echo the last verified row so a caller can keep them as its next
// trusted checkpoint.
public sealed record ChainVerificationResult(
    bool Valid,
    long EntryCount,
    long? BrokenAtSequence,
    string? Reason,
    long? TailSequence = null,
    string? TailRowHash = null);

// Pure, DB-free core of the tamper-evident audit log. Every function here is deterministic and
// side-effect-free so the chain semantics can be unit-tested by folding an in-memory chain, with
// no EF/Postgres dependency. The row hash binds the sequence, the previous row's hash, and EVERY
// persisted content field, so altering any of them — or reordering, dropping, or re-linking
// entries — is detectable by Verify.
public static class AuditHashChain
{
    // The prev-hash of the very first entry: 64 lowercase '0' hex chars (a SHA-256-width sentinel
    // that no real SHA-256 digest can collide with in practice).
    public const string GenesisPrevHash =
        "0000000000000000000000000000000000000000000000000000000000000000";

    // Canonicalize a decision timestamp for hashing AND storage: convert to UTC and truncate to
    // microsecond precision. This makes the row hash (a) offset-independent — the same instant in
    // any timezone hashes identically — and (b) STABLE across the Postgres `timestamptz` round-trip
    // (which keeps microseconds). Persisting the already-truncated value means the database never
    // re-rounds it, so a verify-time recompute matches the ingest-time hash exactly.
    // (1 microsecond = 10 ticks.)
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var truncated = utc.Ticks - (utc.Ticks % 10);
        return new DateTimeOffset(truncated, TimeSpan.Zero);
    }

    // Canonical row hash. Fields are written in a fixed order into a JSON object via Utf8JsonWriter,
    // giving an unambiguous, length-delimited UTF-8 byte representation: string values are
    // quoted/escaped so no field boundary can be forged by shifting content across fields, and null
    // string fields are written as JSON null (distinct from ""). The SHA-256 of those bytes is
    // returned as lowercase hex.
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
                "timestampUtc",
                NormalizeTimestamp(payload.TimestampUtc).ToString("O", CultureInfo.InvariantCulture));
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

    // Fold one payload onto the chain tail. Sequence is monotonic (+1); the new prev-hash is the
    // previous row's hash; the new row hash binds them to the payload. Passing (0, GenesisPrevHash,
    // ...) yields the genesis entry at sequence 1.
    public static (long Sequence, string PrevHash, string RowHash) Append(
        long previousSequence, string previousRowHash, AuditPayload payload)
    {
        var sequence = previousSequence + 1;
        var prevHash = previousRowHash;
        var rowHash = ComputeRowHash(sequence, prevHash, payload);
        return (sequence, prevHash, rowHash);
    }

    // Verify a chain presented in ascending sequence order. Checks: (a) sequences are contiguous
    // starting at 1, (b) the first entry links to the genesis prev-hash, (c) each entry's prev-hash
    // equals the previous entry's row hash, and (d) each entry's stored row hash matches a
    // recomputation over its own fields. An empty chain is trivially valid. When expectedTail is
    // supplied it ALSO enforces that the row at the checkpoint sequence still carries the checkpoint
    // hash — catching tail truncation and full-suffix rewrites a bare chain cannot self-detect.
    public static ChainVerificationResult Verify(
        IReadOnlyList<AuditEntry> orderedBySequence, AuditCheckpoint? expectedTail = null)
    {
        ArgumentNullException.ThrowIfNull(orderedBySequence);

        var verifier = new Verifier(expectedTail);
        foreach (var entry in orderedBySequence)
        {
            if (!verifier.Step(entry))
            {
                // Preserve the historical contract: on failure report the full input size.
                return verifier.Failure! with { EntryCount = orderedBySequence.Count };
            }
        }

        return verifier.Complete();
    }

    // Streaming counterpart of Verify: folds the chain incrementally so verification never
    // materializes the whole (ever-growing) table in memory. Same checks and checkpoint semantics.
    public static async Task<ChainVerificationResult> VerifyAsync(
        IAsyncEnumerable<AuditEntry> orderedBySequence,
        AuditCheckpoint? expectedTail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedBySequence);

        var verifier = new Verifier(expectedTail);
        await foreach (var entry in orderedBySequence.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!verifier.Step(entry))
            {
                return verifier.Failure!;
            }
        }

        return verifier.Complete();
    }

    // Extract the hashed content view of a persisted entry (everything except the chain linkage and
    // the server-only ReceivedAtUtc, which is not part of the hash).
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

    // Incremental chain checker shared by the list and streaming verifiers so both apply IDENTICAL
    // rules. Step returns false and records Failure on the first broken entry; Complete applies the
    // optional trusted-checkpoint check and returns the final result.
    private sealed class Verifier(AuditCheckpoint? expectedTail)
    {
        private string _previousRowHash = GenesisPrevHash;
        private long _count;
        private long _lastSequence;
        private string? _lastRowHash;
        private string? _checkpointRowHash;

        public ChainVerificationResult? Failure { get; private set; }

        public bool Step(AuditEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            var expectedSequence = _count + 1;

            if (entry.Sequence != expectedSequence)
            {
                return Fail(expectedSequence,
                    $"Non-contiguous sequence: expected {expectedSequence}, found {entry.Sequence}.");
            }

            if (entry.PrevHash != _previousRowHash)
            {
                return Fail(expectedSequence, _count == 0
                    ? "First entry prev-hash does not equal the genesis prev-hash."
                    : "Prev-hash does not equal the previous entry's row hash.");
            }

            var recomputed = ComputeRowHash(entry.Sequence, entry.PrevHash, PayloadOf(entry));
            if (recomputed != entry.RowHash)
            {
                return Fail(expectedSequence,
                    "Row hash does not match a recomputation over the entry's fields.");
            }

            _previousRowHash = entry.RowHash;
            _lastSequence = entry.Sequence;
            _lastRowHash = entry.RowHash;
            _count++;

            if (expectedTail is not null && entry.Sequence == expectedTail.Sequence)
            {
                _checkpointRowHash = entry.RowHash;
            }

            return true;
        }

        public ChainVerificationResult Complete()
        {
            if (expectedTail is not null)
            {
                if (_checkpointRowHash is null)
                {
                    return new ChainVerificationResult(false, _count, expectedTail.Sequence,
                        $"Chain does not reach the trusted checkpoint sequence {expectedTail.Sequence}; possible tail truncation.",
                        NullableSequence(), _lastRowHash);
                }

                if (_checkpointRowHash != expectedTail.RowHash)
                {
                    return new ChainVerificationResult(false, _count, expectedTail.Sequence,
                        "Row hash at the trusted checkpoint sequence does not match; history was rewritten.",
                        NullableSequence(), _lastRowHash);
                }
            }

            return new ChainVerificationResult(true, _count, null, null, NullableSequence(), _lastRowHash);
        }

        private bool Fail(long brokenAt, string reason)
        {
            Failure = new ChainVerificationResult(false, _count, brokenAt, reason, NullableSequence(), _lastRowHash);
            return false;
        }

        private long? NullableSequence() => _lastSequence == 0 ? null : _lastSequence;
    }
}
