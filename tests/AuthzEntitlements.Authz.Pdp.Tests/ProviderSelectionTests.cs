using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The name-based provider-selection seam (CS06-CS09 plug in here). Resolves the configured
// engine case-insensitively, and fails CLOSED with a clear message when the configured name
// is not registered — never silently defaulting to some engine.
public sealed class ProviderSelectionTests
{
    // A minimal stand-in engine so selection-among-several can be tested without a real
    // out-of-process adapter.
    private sealed class StubProvider(string name) : IAuthorizationDecisionProvider
    {
        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request) =>
            AccessDecision.Deny(new Reason(ReasonCodes.UnknownAction, "stub"));
    }

    private static AuthorizationDecisionProviderFactory Factory(
        string configuredProvider, params IAuthorizationDecisionProvider[] providers) =>
        new(providers, Options.Create(new PdpOptions { Provider = configuredProvider }));

    [Fact]
    public void GetActiveProvider_ResolvesConfiguredProvider()
    {
        var factory = Factory("reference", new ReferenceDecisionProvider());

        Assert.Equal("reference", factory.GetActiveProvider().Name);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("Reference")]
    [InlineData("REFERENCE")]
    [InlineData("ReFeReNcE")]
    public void GetActiveProvider_MatchesName_CaseInsensitively(string configured)
    {
        var factory = Factory(configured, new ReferenceDecisionProvider());

        Assert.Equal("reference", factory.GetActiveProvider().Name);
    }

    [Fact]
    public void GetActiveProvider_SelectsNamedProvider_AmongSeveral()
    {
        var factory = Factory(
            "casbin", new ReferenceDecisionProvider(), new StubProvider("casbin"));

        Assert.Equal("casbin", factory.GetActiveProvider().Name);
    }

    [Fact]
    public void GetActiveProvider_UnknownProvider_ThrowsNamingItAndAvailable()
    {
        var factory = Factory("casbin", new ReferenceDecisionProvider());

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("casbin", ex.Message);
        Assert.Contains("reference", ex.Message);
    }

    [Fact]
    public void GetActiveProvider_EmptyProviderList_ThrowsFailClosed()
    {
        var factory = Factory("reference");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("reference", ex.Message);
        Assert.Contains("none registered", ex.Message);
    }

    [Fact]
    public void PdpOptions_DefaultsToReferenceProvider()
    {
        Assert.Equal("reference", new PdpOptions().Provider);
    }

    [Fact]
    public void PdpOptions_SectionName_IsPdp()
    {
        Assert.Equal("Pdp", PdpOptions.SectionName);
    }

    [Fact]
    public void TryGetProvider_TrimsSurroundingWhitespace()
    {
        var factory = Factory("reference", new ReferenceDecisionProvider());

        Assert.True(factory.TryGetProvider("  reference  ", out var provider));
        Assert.Equal("reference", provider.Name);
    }

    [Fact]
    public void TryGetProvider_BlankName_FailsClosed()
    {
        var factory = Factory("reference", new ReferenceDecisionProvider());

        Assert.False(factory.TryGetProvider("   ", out _));
    }

    [Fact]
    public void ProviderNames_ListsAllRegisteredProviders()
    {
        var factory = Factory("reference", new ReferenceDecisionProvider(), new StubProvider("casbin"));

        Assert.Equal(new[] { "reference", "casbin" }, factory.ProviderNames);
    }
}
