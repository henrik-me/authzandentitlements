using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.AspNetCore;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Casbin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The CS06 exit criterion: both engine adapters are selectable at runtime alongside the
// reference provider. (a) a real DI container built via AddPdp resolves the configured adapter
// by "Pdp:Provider"; (b) AddPdp registers all three engines; (c) the selection factory picks
// each adapter by name among all three.
public sealed class AdapterProviderSelectionTests
{
    private static ServiceProvider BuildProvider(string configuredProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pdp:Provider"] = configuredProvider,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPdp(configuration);
        return services.BuildServiceProvider();
    }

    private static AuthorizationDecisionProviderFactory FactoryWithAllThree(string configured) =>
        new(
            new IAuthorizationDecisionProvider[]
            {
                new ReferenceDecisionProvider(),
                new AspNetCorePolicyProvider(),
                new CasbinDecisionProvider(),
            },
            Options.Create(new PdpOptions { Provider = configured }));

    [Theory]
    [InlineData("reference")]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    public void AddPdp_ResolvesConfiguredAdapter_AtRuntime(string configuredProvider)
    {
        using var provider = BuildProvider(configuredProvider);

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        Assert.Equal(configuredProvider, factory.GetActiveProvider().Name);
    }

    [Fact]
    public void AddPdp_RegistersReferenceAndBothAdapters()
    {
        using var provider = BuildProvider("reference");

        var names = provider.GetServices<IAuthorizationDecisionProvider>()
            .Select(p => p.Name)
            .ToArray();

        // The reference engine and both CS06 adapters must be registered. Asserted as membership
        // (not an exact set) so later engine adapters — e.g. CS08's "opa" — registering alongside
        // them do not break this CS06 selection test.
        Assert.Contains("reference", names);
        Assert.Contains("aspnet", names);
        Assert.Contains("casbin", names);

        // Names must stay unique so config-driven selection is unambiguous. Compared
        // case-insensitively to match AuthorizationDecisionProviderFactory.ValidateProviderNames
        // (which rejects duplicates via StringComparer.OrdinalIgnoreCase), so this also catches a
        // case-only duplicate the factory would reject.
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    public void Factory_SelectsAdapter_AmongAllThree(string configured)
    {
        var factory = FactoryWithAllThree(configured);

        Assert.Equal(configured, factory.GetActiveProvider().Name);
    }
}
