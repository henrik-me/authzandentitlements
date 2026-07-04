using AuthzEntitlements.Governance.Service.Data;
using AuthzEntitlements.Governance.Service.Endpoints;
using AuthzEntitlements.Governance.Service.Metering;
using AuthzEntitlements.Governance.Service.Sod;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// CS29 — proves the authorization boundary Decision 2 requires directly from the mapped
// endpoint metadata (no live server or database): the five access-request endpoints carry
// authorization metadata (RequireAuthorization), and every other governance endpoint stays
// anonymous so the intra-cluster read paths and the Compliance service keep working.
public sealed class GovernanceEndpointAuthorizationTests
{
    private const string RequestsPrefix = "/api/governance/requests";
    private const string GovernancePrefix = "/api/governance/";

    private static IReadOnlyList<RouteEndpoint> MapGovernanceRouteEndpoints()
    {
        var builder = WebApplication.CreateBuilder();

        // Materializing the mapped endpoints runs minimal-API metadata inference, which needs
        // the handlers' service-typed parameters to be registered so they are recognised as
        // services (not mistaken for an inferred request body). These placeholder factories
        // exist only so IServiceProviderIsService returns true; no handler runs and nothing is
        // resolved here, so a live database/PDP is not needed. If a handler later takes a new
        // service, this test will fail loudly — the intended signal to keep it in sync.
        builder.Services.AddScoped<GovernanceDbContext>(_ => null!);
        builder.Services.AddSingleton<GovernanceMetrics>(_ => null!);
        builder.Services.AddSingleton<IGovernanceAuditSink>(_ => null!);
        builder.Services.AddScoped<AccessApprovalService>(_ => null!);

        using var app = builder.Build();
        app.MapGovernanceEndpoints();

        // Read the mapped endpoints straight from the route builder's own data sources: this
        // reflects exactly what MapGovernanceEndpoints registered (with the group prefix applied)
        // without running the middleware pipeline.
        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static bool RequiresAuthorization(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

    [Fact]
    public void RequestEndpoints_RequireAuthorization()
    {
        var authorized = MapGovernanceRouteEndpoints()
            .Where(RequiresAuthorization)
            .Select(e => e.RoutePattern.RawText!)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        // Exactly the five access-request endpoints require authorization (POST/GET /requests,
        // GET /requests/{id}, approve, reject) — and nothing else.
        Assert.Equal(5, authorized.Length);
        Assert.All(authorized, raw =>
            Assert.StartsWith(RequestsPrefix, raw, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRequestEndpoint_IsAuthorized()
    {
        var requestEndpoints = MapGovernanceRouteEndpoints()
            .Where(e => e.RoutePattern.RawText!.StartsWith(RequestsPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(requestEndpoints);
        Assert.All(requestEndpoints, e =>
            Assert.True(RequiresAuthorization(e),
                $"{e.RoutePattern.RawText} should require authorization"));
    }

    [Theory]
    [InlineData("/api/governance/access-packages")]
    [InlineData("/api/governance/access-packages/{code}")]
    [InlineData("/api/governance/principals/{id}/grants")]
    [InlineData("/api/governance/principals/{id}/access")]
    [InlineData("/api/governance/grants/{id:guid}/revoke")]
    [InlineData("/api/governance/review-campaigns")]
    [InlineData("/api/governance/review-campaigns/{id:guid}")]
    [InlineData("/api/governance/review-campaigns/{id:guid}/run")]
    [InlineData("/api/governance/review-items/{id:guid}/decision")]
    public void NonRequestEndpoints_AreAnonymous(string rawText)
    {
        var endpoints = MapGovernanceRouteEndpoints()
            .Where(e => string.Equals(e.RoutePattern.RawText, rawText, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e =>
            Assert.False(RequiresAuthorization(e),
                $"{rawText} must stay anonymous (Compliance + intra-cluster reads depend on it)"));
    }

    [Fact]
    public void NoGovernanceEndpoint_OutsideRequests_RequiresAuthorization()
    {
        var leaked = MapGovernanceRouteEndpoints()
            .Where(e => e.RoutePattern.RawText!.StartsWith(GovernancePrefix, StringComparison.Ordinal))
            .Where(e => !e.RoutePattern.RawText!.StartsWith(RequestsPrefix, StringComparison.Ordinal))
            .Where(RequiresAuthorization)
            .Select(e => e.RoutePattern.RawText!)
            .ToArray();

        Assert.Empty(leaked);
    }
}
