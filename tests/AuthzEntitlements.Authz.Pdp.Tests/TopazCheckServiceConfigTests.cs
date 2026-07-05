using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// TopazCheckService fail-closed configuration validation, asserted fully OFFLINE: a blank or a malformed
// endpoint throws a clear, actionable InvalidOperationException from the lazy client bootstrap BEFORE any
// gRPC channel is created or any network call is attempted. This guards the CI-invisible live-bootstrap
// path — TopazDecisionProvider turns these throws into a fail-closed deny, so a misconfiguration never
// surfaces as a raw 500 or a cryptic low-level gRPC/Uri error.
//
// Unlike Cerbos (cleartext h2c only), Topaz's authorizer serves TLS by default, so an https:// endpoint
// is VALID here — there is deliberately no https-rejection test. Its live TLS/gRPC path is exercised by
// the env-gated TopazIntegrationTests.
public sealed class TopazCheckServiceConfigTests
{
    private static readonly AccessRequest AnyRequest = new(
        new Subject("user", "teller1", ["Teller"], "CONTOSO"),
        new ActionRequest(ActionNames.AccountRead),
        new Resource("account", Tenant: "CONTOSO"),
        new EvaluationContext([ScopeNames.Read]));

    private static TopazCheckService Service(string endpoint) =>
        new(Options.Create(new TopazOptions { Endpoint = endpoint }), NullLoggerFactory.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_WhenEndpointBlank_ThrowsConfigError(string endpoint)
    {
        using var service = Service(endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("Pdp:Topaz:Endpoint", ex.Message);
    }

    [Theory]
    [InlineData("localhost:8282")]     // missing scheme — parses as scheme "localhost"
    [InlineData("not-a-url")]
    [InlineData("ftp://topaz:8282")]   // wrong scheme
    public void Check_WhenEndpointMalformed_ThrowsClearConfigError(string endpoint)
    {
        using var service = Service(endpoint);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("http(s)://", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Check_WhenTimeoutSecondsNonPositive_ThrowsConfigError(int timeoutSeconds)
    {
        // A non-positive timeout would defeat the bounded fail-closed wait (Task.WaitAsync rejects a
        // zero/negative TimeSpan, and an unbounded query wait lets a hung authorizer block evaluation).
        // The lazy client bootstrap rejects it with a clear, actionable message BEFORE any query — the
        // endpoint itself is valid, isolating the timeout guard — and the provider turns the throw into a
        // fail-closed deny.
        using var service = new TopazCheckService(
            Options.Create(new TopazOptions
            {
                Endpoint = "https://localhost:8282",
                TimeoutSeconds = timeoutSeconds,
            }),
            NullLoggerFactory.Instance);

        var ex = Assert.Throws<InvalidOperationException>(() => service.Check(AnyRequest));

        Assert.Contains("Pdp:Topaz:TimeoutSeconds", ex.Message);
    }

    [Fact]
    public void TimeoutSeconds_DefaultsToFive_WhenBoundFromPdpTopazSection()
    {
        // The bounded fail-closed timeout has a safe default (5s), mirroring OpaOptions, so an operator who
        // configures only the endpoint still gets a prompt fail-closed on a hung authorizer. Binding a
        // Pdp:Topaz section that omits TimeoutSeconds must preserve that default (mirrors how the
        // endpoint-binding test resolves TopazOptions through AddPdp).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pdp:Provider"] = "topaz",
                ["Pdp:Topaz:Endpoint"] = "https://localhost:8282",
            })
            .Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TopazOptions>>().Value;

        Assert.Equal(5, options.TimeoutSeconds);
    }

    [Fact]
    public void UnencryptedHttp2Support_IsEnabled_OnlyWhenACleartextHttpChannelIsBuilt()
    {
        // The h2c opt-in is no longer flipped process-wide at type load (there is no static
        // constructor); it is set LAZILY, only when a cleartext http:// channel is actually built.
        // Building the channel makes no network call, so this stays fully offline. Once built, the switch
        // must be enabled so a real http:// Topaz check can connect. The default TLS (https://) path never
        // sets it.
        using var channel = TopazCheckService.BuildChannel(new Uri("http://localhost:8282"));

        Assert.True(
            AppContext.TryGetSwitch(
                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", out var enabled) && enabled,
            "A cleartext-h2c Topaz needs this switch enabled once its http:// channel is built.");
    }
}
