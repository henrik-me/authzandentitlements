using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;
using AuthzEntitlements.Audit.Service.Endpoints;
using AuthzEntitlements.Audit.Service.Services;
using Xunit;

namespace AuthzEntitlements.Audit.Service.Tests;

// CS36 (LRN-057) crown-jewel invariant: the persisted request snapshot is a NON-hashed convenience
// column. These prove (a) the size guard fails open, (b) the snapshot is attached to the row WITHOUT
// entering ComputeRowHash or affecting Verify, and (c) the query projection carries it through — all
// DB-free, consistent with this project's pure-domain test posture.
public sealed class RequestSnapshotTests
{
    private const string SampleSnapshot =
        "{\"subject\":{\"type\":\"user\",\"id\":\"user-teller1\",\"roles\":[\"Teller\"],\"tenant\":\"CONTOSO\"}," +
        "\"action\":{\"name\":\"bank.transaction.create\"}," +
        "\"resource\":{\"type\":\"transaction\",\"tenant\":\"FABRIKAM\",\"amount\":15000,\"makerId\":\"user-teller1\"}," +
        "\"context\":{\"scopes\":[\"bank.transactions.write\"]}}";

    private static AuditPayload SamplePayload() => new(
        new DateTimeOffset(2026, 7, 4, 2, 0, 0, 123, TimeSpan.Zero).AddTicks(4567),
        TraceId: "0af7651916cd43dd8448eb211c80319c",
        Provider: "reference",
        SubjectId: "user:alice",
        Action: "bank.transaction.create",
        ResourceType: "transaction",
        ResourceId: "txn-123",
        Decision: "Permit",
        Reason: "PermitOwnerAccess",
        Tenant: "acme",
        Producer: "pdp");

    private static AuditEntry BuildEntry(AuditPayload payload, string? snapshot)
    {
        var (sequence, prevHash, rowHash) =
            AuditHashChain.Append(0, AuditHashChain.GenesisPrevHash, payload);
        return AuditChainWriter.CreateEntry(sequence, prevHash, rowHash, payload, snapshot);
    }

    [Fact]
    public void Clamp_ReturnsNull_ForNull() =>
        Assert.Null(RequestSnapshotGuard.Clamp(null));

    [Fact]
    public void Clamp_ReturnsSnapshot_WhenUnderCap() =>
        Assert.Equal(SampleSnapshot, RequestSnapshotGuard.Clamp(SampleSnapshot));

    [Fact]
    public void Clamp_ReturnsSnapshot_AtExactlyCap()
    {
        var atCap = new string('x', RequestSnapshotGuard.DefaultMaxSnapshotChars);

        Assert.Equal(atCap, RequestSnapshotGuard.Clamp(atCap));
    }

    [Fact]
    public void Clamp_FailsOpenToNull_WhenOverCap()
    {
        var overCap = new string('x', RequestSnapshotGuard.DefaultMaxSnapshotChars + 1);

        Assert.Null(RequestSnapshotGuard.Clamp(overCap));
    }

    [Fact]
    public void Clamp_HonorsConfiguredMax_DroppingOverCustomCap()
    {
        // A custom (smaller) cap drops a snapshot the default 16 KB cap would keep — proving the
        // authoritative ingest bound is honored from configuration, not a hard-coded constant.
        var snapshot = new string('x', 100);

        Assert.Null(RequestSnapshotGuard.Clamp(snapshot, maxChars: 50));
    }

    [Fact]
    public void Clamp_HonorsConfiguredMax_KeepingAtCustomCap()
    {
        var atCustomCap = new string('x', 50);

        Assert.Equal(atCustomCap, RequestSnapshotGuard.Clamp(atCustomCap, maxChars: 50));
    }

    [Fact]
    public void RequestSnapshotOptions_DefaultsTo16Kb() =>
        Assert.Equal(
            RequestSnapshotGuard.DefaultMaxSnapshotChars,
            new RequestSnapshotOptions().MaxSnapshotChars);

    [Fact]
    public void EffectiveMax_ClampsAboveColumnWidth_ToDefault()
    {
        // A misconfigured cap ABOVE the persisted column width must NOT be honored verbatim, or an
        // over-column snapshot would pass the guard and then fail SaveChanges (breaking fail-open).
        var options = new RequestSnapshotOptions { MaxSnapshotChars = 20_000 };
        Assert.Equal(RequestSnapshotGuard.DefaultMaxSnapshotChars, options.EffectiveMaxSnapshotChars);

        // A snapshot between the (bad) configured cap and the column width still degrades to null.
        var overColumn = new string('x', RequestSnapshotGuard.DefaultMaxSnapshotChars + 1);
        Assert.Null(RequestSnapshotGuard.Clamp(overColumn, options.EffectiveMaxSnapshotChars));
    }

    [Fact]
    public void EffectiveMax_HonorsLowerConfiguredCap_AndFloorsAtOne()
    {
        Assert.Equal(50, new RequestSnapshotOptions { MaxSnapshotChars = 50 }.EffectiveMaxSnapshotChars);
        Assert.Equal(1, new RequestSnapshotOptions { MaxSnapshotChars = 0 }.EffectiveMaxSnapshotChars);
        Assert.Equal(1, new RequestSnapshotOptions { MaxSnapshotChars = -5 }.EffectiveMaxSnapshotChars);
    }

    [Fact]
    public void CreateEntry_AttachesSnapshot_WithoutTouchingTheRowHash()
    {
        var payload = SamplePayload();

        // Two rows with identical hashed fields but different snapshots (null vs a large JSON blob).
        var withoutSnapshot = BuildEntry(payload, null);
        var withSnapshot = BuildEntry(payload, SampleSnapshot);

        Assert.Null(withoutSnapshot.RequestSnapshot);
        Assert.Equal(SampleSnapshot, withSnapshot.RequestSnapshot);
        // The snapshot is not part of the chain: both rows carry the SAME row hash...
        Assert.Equal(withoutSnapshot.RowHash, withSnapshot.RowHash);
        // ...and each row's stored hash still matches a recomputation over its (snapshot-free) payload.
        Assert.Equal(
            AuditHashChain.ComputeRowHash(
                withSnapshot.Sequence, withSnapshot.PrevHash, AuditHashChain.PayloadOf(withSnapshot)),
            withSnapshot.RowHash);
    }

    [Fact]
    public void CreateEntry_PreservesServerStampedProducer_IndependentOfSnapshot()
    {
        // The Producer is stamped server-side into the payload ("pdp") by the ingest endpoint; the
        // snapshot must not perturb it.
        var entry = BuildEntry(SamplePayload(), SampleSnapshot);

        Assert.Equal("pdp", entry.Producer);
    }

    [Fact]
    public void Verify_RemainsValid_RegardlessOfSnapshotValues()
    {
        // A well-formed two-entry chain whose rows carry differing snapshots (one populated, one
        // null) still verifies: Verify recomputes over PayloadOf, which excludes RequestSnapshot.
        var first = SamplePayload();
        var second = SamplePayload() with { SubjectId = "user:bob", ResourceId = "txn-456" };

        var firstAppend = AuditHashChain.Append(0, AuditHashChain.GenesisPrevHash, first);
        var firstEntry = AuditChainWriter.CreateEntry(
            firstAppend.Sequence, firstAppend.PrevHash, firstAppend.RowHash, first, SampleSnapshot);
        var secondAppend = AuditHashChain.Append(firstAppend.Sequence, firstAppend.RowHash, second);
        var secondEntry = AuditChainWriter.CreateEntry(
            secondAppend.Sequence, secondAppend.PrevHash, secondAppend.RowHash, second, null);

        var result = AuditHashChain.Verify([firstEntry, secondEntry]);

        Assert.True(result.Valid);
        Assert.Equal(2, result.EntryCount);
    }

    [Fact]
    public void ToEntryView_Projection_CarriesTheSnapshot()
    {
        var entry = BuildEntry(SamplePayload(), SampleSnapshot);

        var view = AuditEndpoints.ToEntryView.Compile()(entry);

        Assert.Equal(entry.Sequence, view.Sequence);
        Assert.Equal(entry.RowHash, view.RowHash);
        Assert.Equal(SampleSnapshot, view.RequestSnapshot);
    }

    [Fact]
    public void ToEntryView_Projection_CarriesNullSnapshot()
    {
        var entry = BuildEntry(SamplePayload(), null);

        var view = AuditEndpoints.ToEntryView.Compile()(entry);

        Assert.Null(view.RequestSnapshot);
    }
}
