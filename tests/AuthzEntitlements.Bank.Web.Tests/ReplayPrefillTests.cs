using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS36 (LRN-057): the Audit Explorer's "Replay in Playground" reconstructs the ORIGINAL request from
// the row's canonical snapshot when present (faithful), and degrades gracefully to the CS15
// best-effort pre-fill (null) for a missing/malformed snapshot. ReplayPrefill is the pure parse at
// the heart of that flow.
public sealed class ReplayPrefillTests
{
    // Build a snapshot exactly as the PDP's RequestSnapshotSerializer would: the canonical camelCase
    // JSON of the full AccessRequest. The mirrored PdpAccessRequestDto shares that wire shape.
    private static string Snapshot(PdpAccessRequestDto request) =>
        JsonSerializer.Serialize(request, BankJson.Options);

    private static PdpAccessRequestDto SampleRequest() =>
        new(
            new PdpSubjectDto("user", "user-teller1", ["Teller", "BranchManager"], "CONTOSO", "NM-01"),
            new PdpActionDto("bank.transaction.create"),
            new PdpResourceDto(
                "transaction",
                Id: "txn-1",
                Tenant: "FABRIKAM",
                Branch: "NM-02",
                Amount: 15_000m,
                MakerId: "user-teller1",
                Status: "Pending"),
            new PdpContextDto(["bank.read", "bank.transactions.write"]));

    [Fact]
    public void FromSnapshot_Null_ReturnsNull() =>
        Assert.Null(ReplayPrefill.FromSnapshot(null));

    [Fact]
    public void FromSnapshot_Blank_ReturnsNull() =>
        Assert.Null(ReplayPrefill.FromSnapshot("   "));

    [Fact]
    public void FromSnapshot_MalformedJson_ReturnsNull() =>
        Assert.Null(ReplayPrefill.FromSnapshot("{ not valid json "));

    [Fact]
    public void FromSnapshot_MissingRequiredMembers_ReturnsNull() =>
        // A JSON object with none of subject/action/resource degrades to best-effort (null).
        Assert.Null(ReplayPrefill.FromSnapshot("{\"unrelated\":true}"));

    [Theory]
    // Objects present but a required scalar is blank/absent — the snapshot is non-hashed so a
    // partial/tampered one must fall back to best-effort rather than claim a "faithful" replay of an
    // invalid request.
    [InlineData("{\"subject\":{\"id\":\"\"},\"action\":{\"name\":\"a\"},\"resource\":{\"type\":\"account\"}}")]
    [InlineData("{\"subject\":{\"id\":\"u1\"},\"action\":{\"name\":\"  \"},\"resource\":{\"type\":\"account\"}}")]
    [InlineData("{\"subject\":{\"id\":\"u1\"},\"action\":{\"name\":\"a\"},\"resource\":{}}")]
    public void FromSnapshot_BlankRequiredScalar_ReturnsNull(string snapshot) =>
        Assert.Null(ReplayPrefill.FromSnapshot(snapshot));

    [Fact]
    public void FromSnapshot_FaithfullyReconstructsEveryField()
    {
        var input = ReplayPrefill.FromSnapshot(Snapshot(SampleRequest()));

        Assert.NotNull(input);
        Assert.Equal("user", input!.SubjectType);
        Assert.Equal("user-teller1", input.SubjectId);
        Assert.Equal("Teller, BranchManager", input.Roles);
        Assert.Equal("CONTOSO", input.Tenant);
        // Subject and resource branch are recovered independently (no longer collapsed).
        Assert.Equal("NM-01", input.Branch);
        Assert.Equal("NM-02", input.ResourceBranch);
        // No OBO actor in the sample => the actor fields stay blank (a direct/human call).
        Assert.Null(input.ActorType);
        Assert.Null(input.ActorId);
        Assert.Equal(string.Empty, input.ActorScopes);
        Assert.Equal("bank.transaction.create", input.Action);
        Assert.Equal("transaction", input.ResourceType);
        Assert.Equal("txn-1", input.ResourceId);
        Assert.Equal("FABRIKAM", input.ResourceTenant);
        Assert.Equal(15_000m, input.Amount);
        Assert.Equal("user-teller1", input.MakerId);
        Assert.Equal("Pending", input.Status);
        Assert.Equal("bank.read, bank.transactions.write", input.Scopes);
    }

    [Fact]
    public void FromSnapshot_RoundTripsBackToAnEquivalentRequest()
    {
        var original = SampleRequest();

        var reconstructed = ReplayPrefill.FromSnapshot(Snapshot(original));
        Assert.NotNull(reconstructed);
        var roundTripped = reconstructed!.ToRequestDto();

        Assert.Equal(original.Subject.Type, roundTripped.Subject.Type);
        Assert.Equal(original.Subject.Id, roundTripped.Subject.Id);
        Assert.Equal(original.Subject.Roles, roundTripped.Subject.Roles);
        Assert.Equal(original.Subject.Tenant, roundTripped.Subject.Tenant);
        Assert.Equal(original.Subject.Branch, roundTripped.Subject.Branch);
        Assert.Equal(original.Action.Name, roundTripped.Action.Name);
        Assert.Equal(original.Resource.Type, roundTripped.Resource.Type);
        Assert.Equal(original.Resource.Id, roundTripped.Resource.Id);
        // The cross-tenant resource tenant survives the round-trip (the CS15 pre-fill could not
        // recover it — it is the whole point of the snapshot).
        Assert.Equal(original.Resource.Tenant, roundTripped.Resource.Tenant);
        // The resource branch — DISTINCT from the subject branch — survives too, rather than being
        // collapsed to the subject's (the fidelity bug CS36 replay fixes).
        Assert.Equal(original.Resource.Branch, roundTripped.Resource.Branch);
        Assert.Equal(original.Resource.Amount, roundTripped.Resource.Amount);
        Assert.Equal(original.Resource.MakerId, roundTripped.Resource.MakerId);
        Assert.Equal(original.Resource.Status, roundTripped.Resource.Status);
        Assert.Equal(original.Context.Scopes, roundTripped.Context.Scopes);
    }

    [Fact]
    public void FromSnapshot_MapsSubjectAndResourceBranch_Independently()
    {
        // Subject carries no branch but the resource does: the two map to SEPARATE fields now, so the
        // resource branch is recovered without being (wrongly) written back onto the subject branch.
        var request = SampleRequest() with
        {
            Subject = new PdpSubjectDto("user", "user-1", ["Teller"], "CONTOSO", Branch: null),
        };

        var input = ReplayPrefill.FromSnapshot(Snapshot(request));

        Assert.NotNull(input);
        Assert.Null(input!.Branch);
        Assert.Equal("NM-02", input.ResourceBranch);

        // And it round-trips faithfully: subject branch stays null, resource branch stays "NM-02".
        var roundTripped = input.ToRequestDto();
        Assert.Null(roundTripped.Subject.Branch);
        Assert.Equal("NM-02", roundTripped.Resource.Branch);
    }

    [Fact]
    public void FromSnapshot_ReconstructsOboActor_AndRoundTripsIt()
    {
        var request = SampleRequest() with
        {
            Subject = new PdpSubjectDto(
                "user",
                "user-1",
                ["Teller"],
                "CONTOSO",
                "NM-01",
                new PdpActorDto("agent", "agent-copilot-1", ["agent.bank.read", "agent.bank.write"])),
        };

        var input = ReplayPrefill.FromSnapshot(Snapshot(request));

        Assert.NotNull(input);
        Assert.Equal("agent", input!.ActorType);
        Assert.Equal("agent-copilot-1", input.ActorId);
        Assert.Equal("agent.bank.read, agent.bank.write", input.ActorScopes);

        // The OBO actor round-trips 1:1 — a delegated decision replays as the SAME on-behalf-of
        // request rather than collapsing to a direct/human call.
        var actor = input.ToRequestDto().Subject.Actor;
        Assert.NotNull(actor);
        Assert.Equal("agent", actor!.Type);
        Assert.Equal("agent-copilot-1", actor.Id);
        Assert.Equal(new[] { "agent.bank.read", "agent.bank.write" }, actor.Scopes);
    }

    [Fact]
    public void FromSnapshot_NoActor_RoundTripsToNullActor()
    {
        // A direct/human request (no actor in the snapshot) must reconstruct a null Actor — byte-
        // identical to a hand-authored non-OBO request.
        var input = ReplayPrefill.FromSnapshot(Snapshot(SampleRequest()));

        Assert.NotNull(input);
        Assert.Null(input!.ToRequestDto().Subject.Actor);
    }

    [Fact]
    public void FromSnapshot_LargeSnapshotOverUrlLimit_StillReconstructsFaithfully()
    {
        // A snapshot in the 2001–16384-char band used to be silently dropped from the URL-based
        // pre-fill (falling back to the "no snapshot" banner). Replay now fetches the entry by
        // sequence, so a large-but-valid snapshot must still reconstruct 1:1 — proving the mapping
        // itself never drops it.
        var manyScopes = Enumerable.Range(0, 300).Select(i => $"bank.scope.{i:D4}").ToArray();
        var request = SampleRequest() with
        {
            Context = new PdpContextDto(manyScopes),
        };
        var json = Snapshot(request);

        Assert.True(json.Length > 2000, $"expected a >2000-char snapshot, got {json.Length}.");

        var input = ReplayPrefill.FromSnapshot(json);

        Assert.NotNull(input);
        Assert.Equal(request.Context.Scopes, input!.ToRequestDto().Context.Scopes);
        // Every ABAC input still survives — the row is faithful, not best-effort.
        Assert.Equal("FABRIKAM", input.ResourceTenant);
        Assert.Equal("NM-02", input.ResourceBranch);
    }

    [Fact]
    public void FromSnapshot_HandlesMinimalRequest_WithEmptyRolesAndScopes()
    {
        var request = new PdpAccessRequestDto(
            new PdpSubjectDto("user", "user-1", []),
            new PdpActionDto("bank.account.read"),
            new PdpResourceDto("account"),
            new PdpContextDto([]));

        var input = ReplayPrefill.FromSnapshot(Snapshot(request));

        Assert.NotNull(input);
        Assert.Equal(string.Empty, input!.Roles);
        Assert.Equal(string.Empty, input.Scopes);
        Assert.Null(input.Tenant);
        Assert.Null(input.Amount);
        Assert.Equal("account", input.ResourceType);
    }
}
