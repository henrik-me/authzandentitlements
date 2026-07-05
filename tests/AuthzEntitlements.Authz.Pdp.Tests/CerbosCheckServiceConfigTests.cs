using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CerbosCheckService fail-closed configuration validation, asserted fully OFFLINE: a blank, an https://,
// or a malformed endpoint throws a clear, actionable InvalidOperationException from the lazy client
// bootstrap BEFORE any gRPC channel is created or any network call is attempted (the adapter only
// speaks cleartext h2c). This guards the CI-invisible live-bootstrap path — CerbosDecisionProvider turns
// these throws into a fail-closed deny, so a misconfiguration never surfaces as a raw 500 or a cryptic
// low-level gRPC/Uri error.
public sealed class CerbosCheckServiceConfigTests
{
    private static readonly AccessRequest AnyRequest = new(
        new Subject("user", "teller1", ["Teller"], "CONTOSO"),
        new ActionRequest(ActionNames.AccountRead),
        new Resource("account", Tenant: "CONTOSO"),
        new EvaluationContext([ScopeNames.Read]));

    private static CerbosCheckService Service(string endpoint) =>
        new(Options.Create(new CerbosOptions { Endpoint = endpoint }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_WhenEndpointBlank_ThrowsConfigError(string endpoint)
    {
        using var service = Service(endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("Pdp:Cerbos:Endpoint", ex.Message);
    }

    [Theory]
    [InlineData("https://cerbos.example:3593")]
    [InlineData("HTTPS://Cerbos.example:3593")]
    public void Check_WhenEndpointHttps_ThrowsClearH2cError_BeforeAnyNetwork(string endpoint)
    {
        using var service = Service(endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("h2c", ex.Message);
    }

    [Theory]
    [InlineData("localhost:3593")]     // missing scheme — parses as scheme "localhost"
    [InlineData("not-a-url")]
    [InlineData("ftp://cerbos:3593")]  // wrong scheme
    public void Check_WhenEndpointMalformed_ThrowsClearConfigError(string endpoint)
    {
        using var service = Service(endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("http://", ex.Message);
    }

    [Fact]
    public void UnencryptedHttp2Support_IsEnabled_SoTheCleartextH2cChannelCanConnect()
    {
        // Touch the type so its static constructor runs (idempotent — other tests may already have).
        using var service = Service("http://localhost:3593");

        Assert.True(
            AppContext.TryGetSwitch(
                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", out var enabled) && enabled,
            "Cerbos serves gRPC over cleartext HTTP/2; without this switch every live check fails closed.");
    }
}
