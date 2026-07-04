using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the commercial ENTITLEMENTS / feature-gate page model. No server,
// Docker, or Keycloak required — they exercise the demo-key list, the gate labelling, and
// the fail-closed response mapping (a null response → DISABLED gate with a clear reason,
// never a silent allow).
public class EntitlementsModelTests
{
    [Fact]
    public void DemoFeatureKeys_are_the_two_catalog_features_in_order()
    {
        Assert.Equal(
            new[] { "high-value-transactions", "bulk-payments" },
            EntitlementsModel.DemoFeatureKeys);
    }

    [Theory]
    [InlineData(true, "Enabled")]
    [InlineData(false, "Gated (upgrade required)")]
    public void GateLabel_maps_enabled_flag(bool enabled, string expected)
    {
        Assert.Equal(expected, EntitlementsModel.GateLabel(enabled));
    }

    [Fact]
    public void FromResponse_maps_a_real_enabled_response()
    {
        var resp = new FeatureEntitlementResponse(true, "Professional", "feature enabled");

        var view = EntitlementsModel.FromResponse("high-value-transactions", resp);

        Assert.Equal("high-value-transactions", view.Key);
        Assert.True(view.Enabled);
        Assert.Equal("Professional", view.PlanTier);
        Assert.Equal("feature enabled", view.Reason);
    }

    [Fact]
    public void FromResponse_maps_a_real_disabled_response_without_relabelling_the_reason()
    {
        var resp = new FeatureEntitlementResponse(false, "Standard", "feature disabled");

        var view = EntitlementsModel.FromResponse("bulk-payments", resp);

        Assert.False(view.Enabled);
        Assert.Equal("Standard", view.PlanTier);
        Assert.Equal("feature disabled", view.Reason);
    }

    [Fact]
    public void FromResponse_fails_closed_to_disabled_when_response_is_null()
    {
        var view = EntitlementsModel.FromResponse("high-value-transactions", null);

        Assert.Equal("high-value-transactions", view.Key);
        Assert.False(view.Enabled);
        Assert.Equal("unknown", view.PlanTier);
        Assert.Equal("Entitlements unavailable — fail-closed.", view.Reason);
    }
}
