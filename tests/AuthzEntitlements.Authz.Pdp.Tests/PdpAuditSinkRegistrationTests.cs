using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Guards the one-line rewire in PdpServiceCollectionExtensions: AddPdp must still resolve the
// deterministic, offline LoggingPdpDecisionAuditSink when no Audit config is present, so every
// existing PDP test and `aspire run` stay engine/network-free by default. Also confirms the
// opt-in path flips the whole composition root to the HTTP forwarder + background worker.
public sealed class PdpAuditSinkRegistrationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    [Fact]
    public void AddPdp_WithNoAuditConfig_KeepsLoggingSinkDefaultAndNoHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdp(Config());

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LoggingPdpDecisionAuditSink>(
            provider.GetRequiredService<IPdpDecisionAuditSink>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void AddPdp_WithHttpAuditConfig_UsesHttpForwardingSinkAndWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdp(Config(
            ("Audit:Sink", "http"),
            ("Audit:ServiceUrl", "http://audit-service")));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HttpForwardingPdpDecisionAuditSink>(
            provider.GetRequiredService<IPdpDecisionAuditSink>());
        Assert.Single(
            provider.GetServices<IHostedService>().OfType<AuditForwardingWorker>());
    }
}
