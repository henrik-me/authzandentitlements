using AuthzEntitlements.Authz.Pdp.Providers.Keto;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// KetoCheckService fail-closed configuration validation, asserted fully OFFLINE: a blank or a malformed
// endpoint throws a clear, actionable InvalidOperationException from the lazy bootstrap BEFORE any REST
// client call is attempted. This guards the CI-invisible live path — the provider turns these throws
// into a fail-closed deny, so a misconfiguration never surfaces as a raw 500 or a cryptic low-level
// error. Unlike the SpiceDB adapter, Keto is plain HTTP REST, so https:// is accepted equally with
// http:// (there is no h2c/HTTP2-unencrypted transport switch to guard) — the dropped SpiceDB cases.
// Keto validates BOTH ports: the read endpoint is checked first, then the write endpoint.
public sealed class KetoCheckServiceConfigTests
{
    private static KetoCheckService Service(string readEndpoint, string writeEndpoint = "http://localhost:4467") =>
        new(Options.Create(new KetoOptions { ReadEndpoint = readEndpoint, WriteEndpoint = writeEndpoint }));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckAsync_WhenReadEndpointBlank_ThrowsConfigError(string readEndpoint)
    {
        using var service = Service(readEndpoint);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("Pdp:Keto:ReadEndpoint", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckAsync_WhenWriteEndpointBlank_ThrowsConfigError(string writeEndpoint)
    {
        // A valid read endpoint but a blank write endpoint must still fail closed: the bootstrap needs
        // the write port to seed relationships, so it validates both before any network call.
        using var service = Service("http://localhost:4466", writeEndpoint);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("Pdp:Keto:WriteEndpoint", ex.Message);
    }

    [Theory]
    [InlineData("localhost:4466")]  // missing scheme — parses as scheme "localhost"
    [InlineData("not-a-url")]
    [InlineData("ftp://keto:4466")] // wrong scheme
    public async Task CheckAsync_WhenReadEndpointMalformed_ThrowsClearConfigError(string readEndpoint)
    {
        using var service = Service(readEndpoint);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("http://", ex.Message);
    }

    [Theory]
    [InlineData("localhost:4467")]
    [InlineData("not-a-url")]
    [InlineData("ftp://keto:4467")]
    public async Task CheckAsync_WhenWriteEndpointMalformed_ThrowsClearConfigError(string writeEndpoint)
    {
        using var service = Service("http://localhost:4466", writeEndpoint);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAsync("teller-1", "can_view", "acme-checking"));

        Assert.Contains("http://", ex.Message);
    }
}
