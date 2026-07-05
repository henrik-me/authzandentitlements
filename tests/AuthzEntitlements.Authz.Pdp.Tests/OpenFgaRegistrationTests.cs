using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The OpenFGA adapter's registration + selection through the CS05 seam. Verifies AddPdp wires
// the "openfga" provider so the factory selects it by name, that it coexists with the reference
// engine, and that registration/selection never touch a live server (blank ApiUrl is fine until
// an actual check). Follows the ProviderSelectionTests patterns.
public sealed class OpenFgaRegistrationTests
{
    private static ServiceProvider Build(string provider, string apiUrl = "")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pdp:Provider"] = provider,
                ["Pdp:OpenFga:ApiUrl"] = apiUrl,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddPdp_RegistersOpenFgaProvider_AmongProviders()
    {
        using var provider = Build("reference");

        var names = provider.GetServices<IAuthorizationDecisionProvider>().Select(p => p.Name).ToList();

        Assert.Contains("openfga", names);
        Assert.Contains("reference", names);
    }

    [Fact]
    public void AddPdp_SelectsOpenFga_WhenConfigured()
    {
        using var provider = Build("openfga");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        // CS45: openfga does not declare ISupportsExtendedAuthorizationContext, so the factory wraps it
        // in the fail-closed ExtendedContextGuardProvider. Selection is unchanged (Name still "openfga");
        // the resolved instance is the guard whose Inner is the concrete OpenFGA adapter.
        var active = factory.GetActiveProvider();
        Assert.Equal("openfga", active.Name);
        var guard = Assert.IsType<ExtendedContextGuardProvider>(active);
        Assert.IsType<OpenFgaProvider>(guard.Inner);
    }

    [Fact]
    public void AddPdp_SelectsOpenFga_CaseInsensitively()
    {
        using var provider = Build("OpenFGA");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        Assert.Equal("openfga", factory.GetActiveProvider().Name);
    }

    [Fact]
    public void UnknownProvider_StillFailsClosed_ListingOpenFga()
    {
        using var provider = Build("does-not-exist");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("openfga", ex.Message);
    }

    [Fact]
    public void OpenFgaOptions_BindFromPdpOpenFgaSection()
    {
        using var provider = Build("openfga", apiUrl: "http://localhost:8080");

        var options = provider.GetRequiredService<IOptions<OpenFgaOptions>>().Value;

        Assert.Equal("http://localhost:8080", options.ApiUrl);
        Assert.Equal("authz-rebac", options.StoreName);
        Assert.Equal("Pdp:OpenFga", OpenFgaOptions.SectionName);
    }

    [Fact]
    public void Registration_DoesNotRequireServer_BlankApiUrlIsFine()
    {
        // Building the container and resolving the provider must succeed with no OpenFGA server and
        // an empty ApiUrl — the live client is built lazily only on first actual check.
        using var provider = Build("openfga", apiUrl: "");

        var openfga = provider.GetServices<IAuthorizationDecisionProvider>()
            .Single(p => p.Name == "openfga");

        Assert.NotNull(openfga);
        Assert.NotNull(provider.GetRequiredService<OpenFgaRebacService>());
    }

    [Fact]
    public void Evaluate_FailsClosed_WhenEngineUnavailable()
    {
        // With a blank ApiUrl the service throws when the provider issues its Check; Evaluate must
        // DENY (fail closed) with a stable reason, never throw a 500 through /api/authz/evaluate.
        using var provider = Build("openfga", apiUrl: "");
        var openfga = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "openfga");

        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        var decision = openfga.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, decision.Reasons[0].Code);
    }
}
