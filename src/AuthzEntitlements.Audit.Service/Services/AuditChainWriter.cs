using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Audit.Service.Services;

// Serializes appends onto the tamper-evident chain. The read-tail -> compute-next ->
// insert -> commit sequence must be atomic per append or two concurrent writers could
// compute the same Sequence/PrevHash and fork the chain. A process-wide SemaphoreSlim(1,1)
// held for the whole sequence enforces a single logical writer.
//
// SINGLE-INSTANCE ASSUMPTION: this in-process lock only serializes writers WITHIN one service
// instance. The append endpoint is therefore designed to run as a single writer instance. The
// unique index on RowHash and the Sequence primary key are the database-level backstop that
// makes a concurrent second-instance insert fail loudly (fail closed) rather than silently
// fork the chain; scaling out the writer would require a DB-level advisory lock or an
// append-only sequence generator, which is out of scope for CS13.
public sealed class AuditChainWriter(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AuditEntry> AppendAsync(
        AuditPayload payload, string? requestSnapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Canonicalize the decision timestamp (UTC, microsecond precision) so the value we HASH is
        // exactly the value we PERSIST: the Postgres timestamptz round-trip cannot then re-round it
        // and make a later /verify recompute a different hash (false tamper).
        payload = payload with { TimestampUtc = AuditHashChain.NormalizeTimestamp(payload.TimestampUtc) };

        await _gate.WaitAsync(ct);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var tail = await db.AuditEntries
                .AsNoTracking()
                .OrderByDescending(e => e.Sequence)
                .FirstOrDefaultAsync(ct);

            var previousSequence = tail?.Sequence ?? 0;
            var previousRowHash = tail?.RowHash ?? AuditHashChain.GenesisPrevHash;

            var (sequence, prevHash, rowHash) =
                AuditHashChain.Append(previousSequence, previousRowHash, payload);

            var entry = CreateEntry(sequence, prevHash, rowHash, payload, requestSnapshot);

            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Materialize the persistable row. RowHash is computed by the caller from the PAYLOAD ONLY, so
    // the non-hashed RequestSnapshot (CS36) is attached here WITHOUT ever entering the tamper-evident
    // hash — the same posture as the server-stamped ReceivedAtUtc. Kept internal + static so the
    // mapping (and the hash-independence of the snapshot) is unit-testable without a database.
    internal static AuditEntry CreateEntry(
        long sequence, string prevHash, string rowHash, AuditPayload payload, string? requestSnapshot) =>
        new()
        {
            Sequence = sequence,
            TimestampUtc = payload.TimestampUtc,
            TraceId = payload.TraceId,
            Provider = payload.Provider,
            SubjectId = payload.SubjectId,
            Action = payload.Action,
            ResourceType = payload.ResourceType,
            ResourceId = payload.ResourceId,
            Decision = payload.Decision,
            Reason = payload.Reason,
            Tenant = payload.Tenant,
            Producer = payload.Producer,
            PrevHash = prevHash,
            RowHash = rowHash,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            RequestSnapshot = requestSnapshot,
        };
}
