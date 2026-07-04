using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests.Playground;

// Endpoint-boundary coverage for POST /api/authz/playground/fanout (CS15). The PDP test project has
// no in-process WebApplicationFactory host, so — per the CS15 dispatch — we assert the exact 400
// building blocks the endpoint composes rather than spinning up a new host/framework:
//   * a structurally-incomplete request body is rejected by AccessRequestValidation (400), and
//   * every named engine is validated up front via factory.TryGetProvider (unknown ⇒ 400).
// The happy-path fan-out behaviour is covered by PlaygroundFanoutServiceTests.
public sealed class PlaygroundEndpointsTests
{
    private static AuthorizationDecisionProviderFactory Factory() => LifecycleTestSupport.RbacFactory();

    [Fact]
    public void NullRequestBody_FailsValidation()
    {
        // The endpoint 400s on a null body; the same guard rejects a null AccessRequest.
        Assert.NotNull(AccessRequestValidation.Validate(null));
    }

    [Fact]
    public void IncompleteRequest_FailsValidation()
    {
        // Mirrors what System.Text.Json produces for a "{}" fan-out request body.
        var empty = new AccessRequest(null!, null!, null!, null!);

        Assert.NotNull(AccessRequestValidation.Validate(empty));
    }

    [Fact]
    public void CompleteRequest_PassesValidation()
    {
        Assert.Null(AccessRequestValidation.Validate(LifecycleTestSupport.PermitLargeTxn()));
    }

    [Fact]
    public void UnknownEngineName_IsRejectedByFactory()
    {
        // The endpoint validates each named engine with TryGetProvider before fanning out.
        Assert.False(Factory().TryGetProvider("does-not-exist", out _));
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    [InlineData("cedar")]
    public void KnownEngineNames_AreAcceptedByFactory(string engine)
    {
        Assert.True(Factory().TryGetProvider(engine, out var provider));
        Assert.Equal(engine, provider!.Name);
    }

    [Fact]
    public void EngineName_Matching_IsCaseAndWhitespaceInsensitive()
    {
        // The endpoint delegates name matching to the factory, which trims + compares case-insensitively.
        Assert.True(Factory().TryGetProvider("  CEDAR  ", out var provider));
        Assert.Equal("cedar", provider!.Name);
    }
}
