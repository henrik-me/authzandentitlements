using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Endpoints;
using Xunit;

namespace AuthzEntitlements.Audit.Service.Tests;

// Offline, DB-free tests for the /entries `sequence` filter. AuditEndpoints.ApplyEntryFilters is a
// pure IQueryable transform, so it is exercised over an in-memory List<AuditEntry>.AsQueryable()
// (LINQ-to-objects) — the same predicates EF translates against Postgres, but deterministic here.
public sealed class AuditEntriesSequenceFilterTests
{
    private static AuditEntry Entry(long sequence, string subject = "user:alice") => new()
    {
        Sequence = sequence,
        TimestampUtc = new DateTimeOffset(2026, 7, 4, 2, 0, 0, TimeSpan.Zero).AddMinutes(sequence),
        TraceId = $"trace-{sequence}",
        Provider = "reference",
        SubjectId = subject,
        Action = "account.transfer",
        ResourceType = "account",
        ResourceId = $"acct-{sequence}",
        Decision = "Permit",
        Reason = "PermitOwnerAccess",
        Tenant = "acme",
        Producer = "pdp",
        PrevHash = new string('0', 64),
        RowHash = new string('a', 64),
        ReceivedAtUtc = DateTimeOffset.UtcNow,
    };

    private static IQueryable<AuditEntry> SampleRows() => new List<AuditEntry>
    {
        Entry(1, "user:alice"),
        Entry(2, "user:bob"),
        Entry(3, "user:carol"),
        Entry(4, "user:alice"),
        Entry(5, "user:dave"),
    }.AsQueryable();

    private static IQueryable<AuditEntry> Apply(
        IQueryable<AuditEntry> rows,
        long? sequence = null,
        string? subject = null,
        string? action = null,
        string? decision = null,
        string? tenant = null,
        string? trace = null,
        string? producer = null) =>
        AuditEndpoints.ApplyEntryFilters(rows, sequence, subject, action, decision, tenant, trace, producer);

    [Fact]
    public void Sequence_MatchesExactlyOneRow()
    {
        var result = Apply(SampleRows(), sequence: 3).ToList();

        Assert.Single(result);
        Assert.Equal(3, result[0].Sequence);
    }

    [Fact]
    public void Sequence_ForEachExistingValue_ReturnsThatRow()
    {
        foreach (var seq in new long[] { 1, 2, 3, 4, 5 })
        {
            var result = Apply(SampleRows(), sequence: seq).ToList();
            Assert.Single(result);
            Assert.Equal(seq, result[0].Sequence);
        }
    }

    [Fact]
    public void Sequence_NonExistentValue_ReturnsEmpty()
    {
        Assert.Empty(Apply(SampleRows(), sequence: 999).ToList());
    }

    [Fact]
    public void Sequence_CombinedWithMatchingSubject_ReturnsThatRow()
    {
        var result = Apply(SampleRows(), sequence: 4, subject: "user:alice").ToList();

        Assert.Single(result);
        Assert.Equal(4, result[0].Sequence);
    }

    [Fact]
    public void Sequence_CombinedWithNonMatchingSubject_ReturnsEmpty()
    {
        // Row 3 is user:carol, so requiring subject=user:alice AND sequence=3 yields nothing.
        Assert.Empty(Apply(SampleRows(), sequence: 3, subject: "user:alice").ToList());
    }

    [Fact]
    public void Sequence_Zero_IsIgnored_ReturnsSameAsNoFilter()
    {
        var withZero = Apply(SampleRows(), sequence: 0).Select(e => e.Sequence).ToList();
        var noFilter = Apply(SampleRows()).Select(e => e.Sequence).ToList();

        Assert.Equal(noFilter, withZero);
        Assert.Equal(5, withZero.Count);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-100L)]
    [InlineData(long.MinValue)]
    public void Sequence_Negative_IsIgnored_ReturnsSameAsNoFilter(long negative)
    {
        var withNegative = Apply(SampleRows(), sequence: negative).Select(e => e.Sequence).ToList();
        var noFilter = Apply(SampleRows()).Select(e => e.Sequence).ToList();

        Assert.Equal(noFilter, withNegative);
    }

    [Fact]
    public void Sequence_Null_IsIgnored_ReturnsAllRows()
    {
        Assert.Equal(5, Apply(SampleRows(), sequence: null).Count());
    }

    [Fact]
    public void OtherFilters_StillWork_WhenSequenceOmitted()
    {
        var bySubject = Apply(SampleRows(), subject: "user:alice").Select(e => e.Sequence).ToList();
        Assert.Equal(new long[] { 1, 4 }, bySubject.OrderBy(s => s));

        var byTrace = Apply(SampleRows(), trace: "trace-2").ToList();
        Assert.Single(byTrace);
        Assert.Equal(2, byTrace[0].Sequence);

        var byDecision = Apply(SampleRows(), decision: "Permit").ToList();
        Assert.Equal(5, byDecision.Count);

        var byTenant = Apply(SampleRows(), tenant: "nope").ToList();
        Assert.Empty(byTenant);
    }

    [Fact]
    public void Sequence_ComposesWithProducerAndAction_AndSemantics()
    {
        var match = Apply(SampleRows(), sequence: 2, producer: "pdp", action: "account.transfer").ToList();
        Assert.Single(match);
        Assert.Equal(2, match[0].Sequence);

        var noMatch = Apply(SampleRows(), sequence: 2, producer: "other").ToList();
        Assert.Empty(noMatch);
    }
}
