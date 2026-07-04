using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// LRN-031: OpenFgaOptions.AuthorizationModelId lets a deployment PIN an existing authorization-model
// version so EnsureBootstrappedAsync reuses it instead of writing the embedded model every boot
// (which would accrue a new immutable model VERSION per boot on a persistent shared store). The pin
// DECISION is the pure static OpenFgaRebacService.ResolvePinnedModelId, unit-testable offline without
// a live server; the live "no new model version is written" behaviour is asserted by the self-skipping
// OpenFgaRebacIntegrationTests. Fail-safe: a blank/whitespace id is treated as unset (write-then-pin),
// so a misconfigured empty string never pins a bogus id.
public sealed class OpenFgaModelPinTests
{
    [Fact]
    public void ResolvePinnedModelId_Default_IsNull_WriteThenPin()
    {
        Assert.Null(OpenFgaRebacService.ResolvePinnedModelId(new OpenFgaOptions()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ResolvePinnedModelId_BlankOrWhitespace_IsNull_FailSafe(string configured)
    {
        var options = new OpenFgaOptions { AuthorizationModelId = configured };

        Assert.Null(OpenFgaRebacService.ResolvePinnedModelId(options));
    }

    [Fact]
    public void ResolvePinnedModelId_RealId_ReturnsThatId()
    {
        var options = new OpenFgaOptions { AuthorizationModelId = "01JABCDEF0123456789MODEL" };

        Assert.Equal("01JABCDEF0123456789MODEL", OpenFgaRebacService.ResolvePinnedModelId(options));
    }

    [Fact]
    public void ResolvePinnedModelId_PaddedId_IsTrimmed()
    {
        // A padded value from config/env is trimmed so it pins the exact id (never a whitespace-bearing
        // id the server would reject).
        var options = new OpenFgaOptions { AuthorizationModelId = "  01JABCDEF0123456789MODEL  " };

        Assert.Equal("01JABCDEF0123456789MODEL", OpenFgaRebacService.ResolvePinnedModelId(options));
    }

    [Fact]
    public void AuthorizationModelId_BindsFrom_PdpOpenFgaSection()
    {
        using var provider = BuildProvider(modelId: "01JPINNEDMODELID000000000");

        var options = provider.GetRequiredService<IOptions<OpenFgaOptions>>().Value;

        Assert.Equal("01JPINNEDMODELID000000000", options.AuthorizationModelId);
        Assert.Equal(
            "01JPINNEDMODELID000000000", OpenFgaRebacService.ResolvePinnedModelId(options));
    }

    [Fact]
    public void AuthorizationModelId_DefaultsEmpty_WhenNotConfigured()
    {
        using var provider = BuildProvider(modelId: null);

        var options = provider.GetRequiredService<IOptions<OpenFgaOptions>>().Value;

        Assert.Equal(string.Empty, options.AuthorizationModelId);
        Assert.Null(OpenFgaRebacService.ResolvePinnedModelId(options));
    }

    private static ServiceProvider BuildProvider(string? modelId)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Pdp:Provider"] = "openfga",
            ["Pdp:OpenFga:ApiUrl"] = "http://localhost:8080",
        };
        if (modelId is not null)
        {
            settings["Pdp:OpenFga:AuthorizationModelId"] = modelId;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();
    }
}
