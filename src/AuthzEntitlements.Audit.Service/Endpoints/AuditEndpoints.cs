using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;
using AuthzEntitlements.Audit.Service.Services;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Audit.Service.Endpoints;

// The tamper-evident audit API. Ingestion appends hash-chained rows; /verify recomputes the
// whole chain to detect tampering; /entries is a filtered, paged read model. Endpoints are
// anonymous: this service is called intra-cluster by decision producers (the PDP sink today);
// edge/token concerns are handled in other CSs.
public static class AuditEndpoints
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit");

        group.MapPost("/decisions", IngestDecisionAsync);
        group.MapGet("/verify", VerifyChainAsync);
        group.MapGet("/entries", QueryEntriesAsync);

        return app;
    }

    private static async Task<IResult> IngestDecisionAsync(
        IngestDecisionRequest request,
        AuditChainWriter writer,
        CancellationToken ct)
    {
        // Producer is stamped server-side ("pdp") so a caller cannot spoof the producer
        // identity recorded in the tamper-evident chain. ReceivedAtUtc is stamped by the
        // writer at insert time.
        var payload = new AuditPayload(
            request.TimestampUtc,
            request.TraceId,
            request.Provider,
            request.SubjectId,
            request.Action,
            request.ResourceType,
            request.ResourceId,
            request.Decision,
            request.Reason,
            request.Tenant,
            Producer: "pdp");

        var entry = await writer.AppendAsync(payload, ct);

        return TypedResults.Created(
            $"/api/audit/entries?sequence={entry.Sequence}",
            new IngestDecisionResponse(entry.Sequence, entry.PrevHash, entry.RowHash));
    }

    private static async Task<IResult> VerifyChainAsync(AuditDbContext db, CancellationToken ct)
    {
        var entries = await db.AuditEntries
            .AsNoTracking()
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        var result = AuditHashChain.Verify(entries);

        return TypedResults.Ok(new ChainVerificationResponse(
            result.Valid, result.EntryCount, result.BrokenAtSequence, result.Reason));
    }

    private static async Task<IResult> QueryEntriesAsync(
        AuditDbContext db,
        string? subject,
        string? action,
        string? decision,
        string? tenant,
        string? trace,
        string? producer,
        int? limit,
        int? offset,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var skip = Math.Max(offset ?? 0, 0);

        var query = db.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(e => e.SubjectId == subject);
        }

        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(e => e.Action == action);
        }

        if (!string.IsNullOrEmpty(decision))
        {
            query = query.Where(e => e.Decision == decision);
        }

        if (!string.IsNullOrEmpty(tenant))
        {
            query = query.Where(e => e.Tenant == tenant);
        }

        if (!string.IsNullOrEmpty(trace))
        {
            query = query.Where(e => e.TraceId == trace);
        }

        if (!string.IsNullOrEmpty(producer))
        {
            query = query.Where(e => e.Producer == producer);
        }

        var entries = await query
            .OrderBy(e => e.Sequence)
            .Skip(skip)
            .Take(take)
            .Select(e => new AuditEntryView(
                e.Sequence,
                e.TimestampUtc,
                e.TraceId,
                e.Provider,
                e.SubjectId,
                e.Action,
                e.ResourceType,
                e.ResourceId,
                e.Decision,
                e.Reason,
                e.Tenant,
                e.Producer,
                e.PrevHash,
                e.RowHash))
            .ToListAsync(ct);

        return TypedResults.Ok((IReadOnlyList<AuditEntryView>)entries);
    }
}
