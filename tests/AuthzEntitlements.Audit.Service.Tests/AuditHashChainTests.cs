using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Audit.Service.Tests;

// Pure-domain tests over AuditHashChain. The chain is folded entirely in memory (no EF/DB),
// so every tamper-evidence property is exercised deterministically. AuditEntry is a plain
// POCO, so a verifiable chain can be materialized directly for Verify.
public sealed class AuditHashChainTests
{
    private static AuditPayload SamplePayload() => new(
        new DateTimeOffset(2026, 7, 4, 2, 0, 0, 123, TimeSpan.Zero).AddTicks(4567),
        TraceId: "0af7651916cd43dd8448eb211c80319c",
        Provider: "reference",
        SubjectId: "user:alice",
        Action: "account.transfer",
        ResourceType: "account",
        ResourceId: "acct-123",
        Decision: "Permit",
        Reason: "PermitOwnerAccess",
        Tenant: "acme",
        Producer: "pdp");

    // Fold a list of payloads onto the genesis chain, materializing persistable entries in
    // sequence order — the shape Verify consumes.
    private static List<AuditEntry> BuildChain(IReadOnlyList<AuditPayload> payloads)
    {
        var entries = new List<AuditEntry>();
        long previousSequence = 0;
        var previousRowHash = AuditHashChain.GenesisPrevHash;

        foreach (var payload in payloads)
        {
            var (sequence, prevHash, rowHash) =
                AuditHashChain.Append(previousSequence, previousRowHash, payload);
            entries.Add(ToEntry(sequence, prevHash, rowHash, payload));
            previousSequence = sequence;
            previousRowHash = rowHash;
        }

        return entries;
    }

    private static AuditEntry ToEntry(
        long sequence, string prevHash, string rowHash, AuditPayload payload) => new()
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
        };

    private static AuditPayload MakeSeries(int index) => SamplePayload() with
    {
        SubjectId = $"user:{index}",
        Action = $"action.{index}",
        Reason = $"reason-{index}",
    };

    [Fact]
    public void Genesis_AppendFromZero_YieldsSequenceOneAndGenesisPrevHash()
    {
        var (sequence, prevHash, rowHash) =
            AuditHashChain.Append(0, AuditHashChain.GenesisPrevHash, SamplePayload());

        Assert.Equal(1, sequence);
        Assert.Equal(AuditHashChain.GenesisPrevHash, prevHash);
        Assert.Equal(64, rowHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", rowHash);
    }

    [Fact]
    public void GenesisPrevHash_IsSixtyFourZeroChars()
    {
        Assert.Equal(64, AuditHashChain.GenesisPrevHash.Length);
        Assert.All(AuditHashChain.GenesisPrevHash, c => Assert.Equal('0', c));
    }

    [Fact]
    public void ComputeRowHash_IsDeterministic_ForIdenticalInputs()
    {
        var payload = SamplePayload();
        var a = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, payload);
        var b = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, payload);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeRowHash_MatchesPinnedKnownVector()
    {
        // Pinned regression vector: if the canonical encoding ever changes, this literal
        // breaks, flagging a chain-format change that would silently invalidate stored chains.
        const string expected =
            "5b0ea21d6272d81f24402a4f7a00dd59fdeb01d31ed1c6c8301090cdaf7a8550";

        var actual = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, SamplePayload());

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("traceId")]
    [InlineData("provider")]
    [InlineData("subjectId")]
    [InlineData("action")]
    [InlineData("resourceType")]
    [InlineData("resourceId")]
    [InlineData("decision")]
    [InlineData("reason")]
    [InlineData("tenant")]
    [InlineData("producer")]
    public void ComputeRowHash_ChangesWhenAnyPayloadFieldChanges(string field)
    {
        var baseline = SamplePayload();
        var mutated = field switch
        {
            "timestamp" => baseline with { TimestampUtc = baseline.TimestampUtc.AddTicks(1) },
            "traceId" => baseline with { TraceId = baseline.TraceId + "x" },
            "provider" => baseline with { Provider = "other" },
            "subjectId" => baseline with { SubjectId = "user:bob" },
            "action" => baseline with { Action = "account.close" },
            "resourceType" => baseline with { ResourceType = "loan" },
            "resourceId" => baseline with { ResourceId = "acct-999" },
            "decision" => baseline with { Decision = "Deny" },
            "reason" => baseline with { Reason = "DenyByPolicy" },
            "tenant" => baseline with { Tenant = "globex" },
            "producer" => baseline with { Producer = "entitlements" },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        var baseHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, baseline);
        var mutatedHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, mutated);

        Assert.NotEqual(baseHash, mutatedHash);
    }

    [Fact]
    public void ComputeRowHash_ChangesWhenSequenceChanges()
    {
        var payload = SamplePayload();
        var a = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, payload);
        var b = AuditHashChain.ComputeRowHash(2, AuditHashChain.GenesisPrevHash, payload);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeRowHash_ChangesWhenPrevHashChanges()
    {
        var payload = SamplePayload();
        var a = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, payload);
        var other = new string('1', 64);
        var b = AuditHashChain.ComputeRowHash(1, other, payload);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeRowHash_NullResourceId_DiffersFromEmptyString()
    {
        var withNull = SamplePayload() with { ResourceId = null };
        var withEmpty = SamplePayload() with { ResourceId = string.Empty };

        var nullHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, withNull);
        var emptyHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, withEmpty);

        Assert.NotEqual(nullHash, emptyHash);
    }

    [Fact]
    public void ComputeRowHash_NullTenant_DiffersFromEmptyString()
    {
        var withNull = SamplePayload() with { Tenant = null };
        var withEmpty = SamplePayload() with { Tenant = string.Empty };

        var nullHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, withNull);
        var emptyHash = AuditHashChain.ComputeRowHash(1, AuditHashChain.GenesisPrevHash, withEmpty);

        Assert.NotEqual(nullHash, emptyHash);
    }

    [Fact]
    public void Append_FoldingSeries_LinksEachPrevHashToPreviousRowHash()
    {
        var payloads = Enumerable.Range(0, 5).Select(MakeSeries).ToList();
        var chain = BuildChain(payloads);

        Assert.Equal(AuditHashChain.GenesisPrevHash, chain[0].PrevHash);
        for (var i = 1; i < chain.Count; i++)
        {
            Assert.Equal(i + 1, chain[i].Sequence);
            Assert.Equal(chain[i - 1].RowHash, chain[i].PrevHash);
        }
    }

    [Fact]
    public void Verify_WellFormedChain_IsValid()
    {
        var payloads = Enumerable.Range(0, 4).Select(MakeSeries).ToList();
        var chain = BuildChain(payloads);

        var result = AuditHashChain.Verify(chain);

        Assert.True(result.Valid);
        Assert.Equal(4, result.EntryCount);
        Assert.Null(result.BrokenAtSequence);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Verify_EmptyChain_IsValidWithZeroCount()
    {
        var result = AuditHashChain.Verify(Array.Empty<AuditEntry>());

        Assert.True(result.Valid);
        Assert.Equal(0, result.EntryCount);
        Assert.Null(result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_TamperedPayloadField_DetectsBreakAtThatEntry()
    {
        var chain = BuildChain(Enumerable.Range(0, 4).Select(MakeSeries).ToList());

        // Mutate a stored content field WITHOUT recomputing its row hash: the row-hash
        // recomputation over the entry's fields no longer matches.
        chain[2].Decision = "Deny";

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        Assert.Equal(3, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_TamperedRowHash_DetectsBreakAtThatEntry()
    {
        var chain = BuildChain(Enumerable.Range(0, 4).Select(MakeSeries).ToList());

        chain[1].RowHash = new string('a', 64);

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        // Entry 2's row hash no longer recomputes; the mismatch surfaces at sequence 2 before
        // the broken link to entry 3 is reached.
        Assert.Equal(2, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_BrokenPrevHashLink_DetectsBreakAtThatEntry()
    {
        var chain = BuildChain(Enumerable.Range(0, 4).Select(MakeSeries).ToList());

        // Re-point entry 3's prev-hash away from entry 2's row hash without touching entry 2,
        // so the link check (not the row-hash check) trips first at sequence 3.
        chain[2].PrevHash = new string('b', 64);
        chain[2].RowHash =
            AuditHashChain.ComputeRowHash(chain[2].Sequence, chain[2].PrevHash, AuditHashChain.PayloadOf(chain[2]));

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        Assert.Equal(3, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_NonContiguousSequence_DetectsGap()
    {
        var chain = BuildChain(Enumerable.Range(0, 5).Select(MakeSeries).ToList());

        // Drop the middle entry: the remaining list is ordered but its sequences skip a value.
        chain.RemoveAt(2);

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        Assert.Equal(3, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_WrongGenesisPrevHashOnFirstEntry_DetectsBreakAtSequenceOne()
    {
        var chain = BuildChain(Enumerable.Range(0, 3).Select(MakeSeries).ToList());

        chain[0].PrevHash = new string('c', 64);
        chain[0].RowHash =
            AuditHashChain.ComputeRowHash(chain[0].Sequence, chain[0].PrevHash, AuditHashChain.PayloadOf(chain[0]));

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        Assert.Equal(1, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_ReorderedEntries_DetectsBreak()
    {
        var chain = BuildChain(Enumerable.Range(0, 4).Select(MakeSeries).ToList());

        (chain[1], chain[2]) = (chain[2], chain[1]);

        var result = AuditHashChain.Verify(chain);

        Assert.False(result.Valid);
        // After the swap, position 1 holds the entry with Sequence 3, so the contiguity check
        // trips at the expected sequence 2.
        Assert.Equal(2, result.BrokenAtSequence);
    }

    [Fact]
    public void Verify_SingleEntryChain_IsValid()
    {
        var chain = BuildChain(new[] { SamplePayload() });

        var result = AuditHashChain.Verify(chain);

        Assert.True(result.Valid);
        Assert.Equal(1, result.EntryCount);
    }
}
