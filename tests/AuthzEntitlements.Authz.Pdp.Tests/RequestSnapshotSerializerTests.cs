using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Contracts;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS36 (LRN-057): the canonical request snapshot must be deterministic (so the same request always
// yields byte-identical JSON), round-trip faithfully (so the Audit Explorer can reconstruct every
// ABAC input), and fail OPEN to null (so a serialization failure never throws on the decision path
// or drops the audit write).
public sealed class RequestSnapshotSerializerTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    // A rich request that exercises every field the snapshot must carry: subject roles + tenant +
    // branch, action, and the ABAC resource inputs (amount/maker/status/tenant/branch) plus context
    // scopes — precisely the inputs the CS15 best-effort replay could NOT recover.
    private static AccessRequest SampleRequest() =>
        new(
            new Subject("user", "user-teller1", ["Teller", "BranchManager"], "CONTOSO", "NM-01"),
            new ActionRequest("bank.transaction.create"),
            new Resource(
                "transaction",
                Id: "txn-1",
                Tenant: "FABRIKAM",
                Branch: "NM-02",
                Amount: 15_000m,
                MakerId: "user-teller1",
                Status: "Pending"),
            new EvaluationContext(["bank.read", "bank.transactions.write"]));

    [Fact]
    public void MaxSnapshotChars_Is16Kb() =>
        Assert.Equal(16384, RequestSnapshotSerializer.MaxSnapshotChars);

    [Fact]
    public void TrySerialize_IsDeterministic_AcrossRepeatedCalls()
    {
        var request = SampleRequest();

        var first = RequestSnapshotSerializer.TrySerialize(request);
        var second = RequestSnapshotSerializer.TrySerialize(request);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TrySerialize_TwoEquivalentRequests_ProduceIdenticalJson() =>
        Assert.Equal(
            RequestSnapshotSerializer.TrySerialize(SampleRequest()),
            RequestSnapshotSerializer.TrySerialize(SampleRequest()));

    [Fact]
    public void TrySerialize_RoundTrips_ToEqualFields()
    {
        var request = SampleRequest();

        var json = RequestSnapshotSerializer.TrySerialize(request);
        Assert.NotNull(json);
        var restored = JsonSerializer.Deserialize<AccessRequest>(json!, WebOptions);

        Assert.NotNull(restored);
        Assert.Equal(request.Subject.Type, restored!.Subject.Type);
        Assert.Equal(request.Subject.Id, restored.Subject.Id);
        Assert.Equal(request.Subject.Roles, restored.Subject.Roles);
        Assert.Equal(request.Subject.Tenant, restored.Subject.Tenant);
        Assert.Equal(request.Subject.Branch, restored.Subject.Branch);
        Assert.Equal(request.Action.Name, restored.Action.Name);
        Assert.Equal(request.Resource.Type, restored.Resource.Type);
        Assert.Equal(request.Resource.Id, restored.Resource.Id);
        Assert.Equal(request.Resource.Tenant, restored.Resource.Tenant);
        Assert.Equal(request.Resource.Branch, restored.Resource.Branch);
        Assert.Equal(request.Resource.Amount, restored.Resource.Amount);
        Assert.Equal(request.Resource.MakerId, restored.Resource.MakerId);
        Assert.Equal(request.Resource.Status, restored.Resource.Status);
        Assert.Equal(request.Context.Scopes, restored.Context.Scopes);
    }

    [Fact]
    public void TrySerialize_CapturesEveryAbacInput()
    {
        var json = RequestSnapshotSerializer.TrySerialize(SampleRequest());

        Assert.NotNull(json);
        Assert.Contains("15000", json);
        Assert.Contains("user-teller1", json);
        Assert.Contains("Teller", json);
        Assert.Contains("BranchManager", json);
        Assert.Contains("bank.transactions.write", json);
        Assert.Contains("FABRIKAM", json);
        Assert.Contains("Pending", json);
    }

    [Fact]
    public void TrySerialize_RoundTrips_TheOboActor()
    {
        var request = SampleRequest() with
        {
            Subject = new Subject(
                "user",
                "user-1",
                ["Teller"],
                "CONTOSO",
                Branch: null,
                Actor: new Actor("agent", "agent-copilot-1", ["agent.bank.read"])),
        };

        var json = RequestSnapshotSerializer.TrySerialize(request);
        Assert.NotNull(json);
        var restored = JsonSerializer.Deserialize<AccessRequest>(json!, WebOptions);

        Assert.NotNull(restored!.Subject.Actor);
        Assert.Equal("agent", restored.Subject.Actor!.Type);
        Assert.Equal("agent-copilot-1", restored.Subject.Actor.Id);
        Assert.Equal(new[] { "agent.bank.read" }, restored.Subject.Actor.Scopes);
    }

    [Fact]
    public void TrySerialize_FailsOpenToNull_OnSerializationFailure()
    {
        // Contrived deterministic failure: a MaxDepth shallower than the nested AccessRequest forces
        // the writer to throw, and the fail-open contract must swallow it to null (Decision #3) so
        // the decision path never faults and the row is audited without a snapshot.
        var shallow = new JsonSerializerOptions(JsonSerializerDefaults.Web) { MaxDepth = 1 };

        Assert.Null(RequestSnapshotSerializer.TrySerialize(SampleRequest(), shallow));
    }

    [Fact]
    public void TrySerialize_NeverThrows_OnDegenerateSurrogateInput()
    {
        // A lone UTF-16 surrogate has no valid JSON encoding. Whatever the runtime does with it, the
        // serializer must fail open rather than throw on the decision hot path.
        var request = SampleRequest() with
        {
            Subject = new Subject("user", "\uD800", ["Teller"], "CONTOSO"),
        };

        var exception = Record.Exception(() => RequestSnapshotSerializer.TrySerialize(request));

        Assert.Null(exception);
    }

    [Fact]
    public void TrySerialize_ProducesCompactJson_WithoutIndentation()
    {
        var json = RequestSnapshotSerializer.TrySerialize(SampleRequest());

        Assert.NotNull(json);
        Assert.DoesNotContain("\n", json);
    }
}
