using System.Net;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class EntitlementsClientTests
{
    private static HttpClient Client(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://entitlements-service") };

    [Fact]
    public async Task GetPlanAsync_requests_tenant_plan_and_maps()
    {
        const string json = """
        {
          "tenantCode": "CONTOSO",
          "planTier": "Enterprise",
          "seatLimit": 50,
          "seatsUsed": 3,
          "modules": ["accounts", "transactions"],
          "features": ["bulk-export"]
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new EntitlementsClient(Client(handler));

        var plan = await client.GetPlanAsync("CONTOSO");

        Assert.Equal("/api/entitlements/CONTOSO/plan", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(plan);
        Assert.Equal("Enterprise", plan!.PlanTier);
        Assert.Equal(50, plan.SeatLimit);
        Assert.Equal(2, plan.Modules.Length);
    }

    [Fact]
    public async Task GetFeatureAsync_requests_feature_path_and_maps()
    {
        const string json = """
        { "enabled": true, "planTier": "Enterprise", "reason": "feature enabled" }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new EntitlementsClient(Client(handler));

        var feature = await client.GetFeatureAsync("CONTOSO", "bulk-export");

        Assert.Equal(
            "/api/entitlements/CONTOSO/features/bulk-export",
            handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(feature);
        Assert.True(feature!.Enabled);
    }

    [Fact]
    public async Task GetPlanAsync_fails_closed_to_null_on_not_found()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.NotFound, "");
        var client = new EntitlementsClient(Client(handler));

        var plan = await client.GetPlanAsync("UNKNOWN");

        Assert.Null(plan);
    }
}
