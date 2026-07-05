using Aserto.Authorizer.V2;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Offline coverage of TopazCheckService.ExtractOutcome — the seam that reads the raw Rego decision object
// out of the authorizer's Query result binding — proving the malformed-obligations bug site directly,
// with NO live authorizer (ExtractOutcome is internal; InternalsVisibleTo the test assembly). The focus
// is the three obligations cases the provider relies on to fail closed correctly:
//   * PRESENT but not a JSON array (e.g. a bare string) → ObligationsMalformed == true, so the provider
//     denies a permit rather than silently dropping require_approval (a fail-OPEN on the maker-checker
//     threshold).
//   * a well-formed list                                → ObligationsMalformed == false + items mapped.
//   * ABSENT                                            → ObligationsMalformed == false + null list (a
//     legitimate no-obligation permit).
// A non-string element inside a list is NOT a malformed field (the field IS a list); it is surfaced as
// its protobuf kind name so the provider's obligation mapping fails closed on the unknown token.
public sealed class TopazCheckServiceExtractOutcomeTests
{
    // Wraps a raw Rego decision object in the { "result": [ { "bindings": { "x": <decision> } } ] } Struct
    // the Aserto authorizer returns, so ExtractOutcome navigates to it exactly as it does for a live query.
    private static QueryResponse ResponseWithDecision(Struct decision)
    {
        var bindings = new Struct();
        bindings.Fields["x"] = Value.ForStruct(decision);

        var firstResult = new Struct();
        firstResult.Fields["bindings"] = Value.ForStruct(bindings);

        var root = new Struct();
        root.Fields["result"] = Value.ForList(Value.ForStruct(firstResult));

        return new QueryResponse { Response = root };
    }

    [Fact]
    public void ObligationsPresentButNotAList_IsMalformed_AndYieldsNullList()
    {
        // The bug site: a Permit whose `obligations` is a bare string, not an array. The old GetStringList
        // returned null here (indistinguishable from an absent field), so the provider read it as a
        // no-obligation permit and dropped require_approval. ExtractOutcome must now flag it malformed.
        var decision = new Struct();
        decision.Fields["decision"] = Value.ForString("Permit");
        decision.Fields["reason"] = Value.ForString("Permit");
        decision.Fields["rule"] = Value.ForString("transaction.create.Permit");
        decision.Fields["obligations"] = Value.ForString("require_approval");

        var outcome = TopazCheckService.ExtractOutcome(ResponseWithDecision(decision));

        Assert.Equal("Permit", outcome.Decision);
        Assert.Equal("Permit", outcome.Reason);
        Assert.Equal("transaction.create.Permit", outcome.Rule);
        Assert.Null(outcome.Obligations);
        Assert.True(outcome.ObligationsMalformed);
    }

    [Fact]
    public void ObligationsList_IsWellFormed_AndMapsItems()
    {
        var decision = new Struct();
        decision.Fields["decision"] = Value.ForString("Permit");
        decision.Fields["reason"] = Value.ForString("Permit");
        decision.Fields["obligations"] = Value.ForList(
            Value.ForString("require_approval"), Value.ForString("post_immediately"));

        var outcome = TopazCheckService.ExtractOutcome(ResponseWithDecision(decision));

        Assert.False(outcome.ObligationsMalformed);
        Assert.Equal(new[] { "require_approval", "post_immediately" }, outcome.Obligations);
    }

    [Fact]
    public void ObligationsAbsent_IsWellFormed_WithNullList()
    {
        // No `obligations` field at all — a legitimate no-obligation permit (a read or a below-threshold
        // transaction). This must NOT be flagged malformed, so the provider still permits.
        var decision = new Struct();
        decision.Fields["decision"] = Value.ForString("Permit");
        decision.Fields["reason"] = Value.ForString("Permit");

        var outcome = TopazCheckService.ExtractOutcome(ResponseWithDecision(decision));

        Assert.False(outcome.ObligationsMalformed);
        Assert.Null(outcome.Obligations);
    }

    [Fact]
    public void ObligationsListWithNonStringElement_SurfacesKindName_NotMalformed()
    {
        // A non-string element in the obligations LIST is not a malformed FIELD (the field IS a list); it
        // is surfaced as its protobuf kind name so the provider's obligation mapping fails closed on the
        // unknown token rather than silently dropping it. The existing behaviour must be preserved.
        var decision = new Struct();
        decision.Fields["decision"] = Value.ForString("Permit");
        decision.Fields["reason"] = Value.ForString("Permit");
        decision.Fields["obligations"] = Value.ForList(
            Value.ForString("require_approval"), Value.ForNumber(42));

        var outcome = TopazCheckService.ExtractOutcome(ResponseWithDecision(decision));

        Assert.False(outcome.ObligationsMalformed);
        Assert.NotNull(outcome.Obligations);
        Assert.Equal(2, outcome.Obligations!.Count);
        Assert.Equal("require_approval", outcome.Obligations[0]);
        Assert.Equal(Value.KindOneofCase.NumberValue.ToString(), outcome.Obligations[1]);
    }
}
