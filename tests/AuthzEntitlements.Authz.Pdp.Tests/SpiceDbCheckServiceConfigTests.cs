using AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// SpiceDbCheckService fail-closed configuration validation, asserted fully OFFLINE: a blank or an
// https:// endpoint throws a clear, actionable InvalidOperationException from BuildClients BEFORE any
// gRPC channel is created or any network call is attempted (the adapter only speaks cleartext h2c).
// This guards the CI-invisible live-bootstrap path — the provider turns these throws into a fail-closed
// deny, so a misconfiguration never surfaces as a raw 500 or a cryptic low-level gRPC error.
public sealed class SpiceDbCheckServiceConfigTests
{
    private static SpiceDbCheckService Service(string endpoint) =>
        new(Options.Create(new SpiceDbOptions { Endpoint = endpoint, PresharedKey = "spicedb-dev-key" }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckAsync_WhenEndpointBlank_ThrowsConfigError(string endpoint)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(endpoint).CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("Pdp:SpiceDb:Endpoint", ex.Message);
    }

    [Theory]
    [InlineData("https://spicedb.example:50051")]
    [InlineData("HTTPS://SpiceDB.example:50051")]
    public async Task CheckAsync_WhenEndpointHttps_ThrowsClearH2cError_BeforeAnyNetwork(string endpoint)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(endpoint).CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("h2c", ex.Message);
    }
}
