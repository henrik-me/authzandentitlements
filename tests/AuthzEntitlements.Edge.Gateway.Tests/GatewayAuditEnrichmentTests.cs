using System.Diagnostics.Metrics;
using System.Security.Claims;
using AuthzEntitlements.Edge.Gateway.Audit;
using AuthzEntitlements.Edge.Gateway.Auth;
using AuthzEntitlements.Edge.Gateway.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// End-to-end proof of the LRN-013 edge-denial enrichment. On a short-circuit 401/403 deny,
// UseAuthorization short-circuits before the YARP proxy pipeline runs, so the
// IReverseProxyFeature is unset — the pre-CS32 source of RouteId/RequiredScope. This drives
// the REAL GatewayAuditMiddleware.InvokeAsync with a hand-built context that mirrors that
// state (the matched endpoint carries the YARP RouteModel because routing already ran; no
// proxy feature; the marker absent) and asserts the emitted audit event now carries both
// RouteId and RequiredScope, recovered from the endpoint metadata.
public sealed class GatewayAuditEnrichmentTests
{
    // Captures the structured state of each emitted log entry so a test can read the named
    // audit fields (RouteId, RequiredScope, Decision, Reason, ...) by key.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyList<KeyValuePair<string, object?>>> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                Entries.Add(values);
            }
        }
    }

    private static GatewayMetrics NewMetrics()
    {
        var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        return new GatewayMetrics(provider.GetRequiredService<IMeterFactory>());
    }

    private static Endpoint RouteEndpoint(string routeId, string? authorizationPolicy)
    {
        var model = new RouteModel(
            new RouteConfig { RouteId = routeId, AuthorizationPolicy = authorizationPolicy },
            cluster: null,
            HttpTransformer.Empty);
        return new Endpoint(requestDelegate: null, new EndpointMetadataCollection(model), routeId);
    }

    private static object? Field(IReadOnlyList<KeyValuePair<string, object?>> entry, string key)
        => entry.FirstOrDefault(kv => kv.Key == key).Value;

    // Runs the middleware against a short-circuit deny: next() sets the deny status but never
    // sets the edge-authorized marker or the IReverseProxyFeature (the proxy never ran).
    private static async Task<IReadOnlyList<KeyValuePair<string, object?>>> RunDenyAsync(
        int statusCode, Endpoint endpoint, ClaimsPrincipal user)
    {
        var logger = new CapturingLogger<GatewayAuditMiddleware>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GatewayAuthenticationSetup.AudienceConfigKey] = "bank-api",
            })
            .Build();

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };

        var middleware = new GatewayAuditMiddleware(next, logger, NewMetrics(), config);

        var context = new DefaultHttpContext { User = user };
        context.Request.Method = "POST";
        context.Request.Path = "/api/transactions";
        context.SetEndpoint(endpoint);

        await middleware.InvokeAsync(context);

        return Assert.Single(logger.Entries);
    }

    [Fact]
    public async Task Deny401_Unauthenticated_CarriesRouteIdAndRequiredScopeFromEndpoint()
    {
        // Anonymous request → coarse 401. No proxy feature; RouteId/RequiredScope must come
        // from the matched endpoint's RouteModel.
        var entry = await RunDenyAsync(
            StatusCodes.Status401Unauthorized,
            RouteEndpoint("transactions-create", CoarseAuthorization.TransactionsWritePolicy),
            new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal("transactions-create", Field(entry, "RouteId"));
        Assert.Equal(CoarseAuthorization.TransactionsWriteScope, Field(entry, "RequiredScope"));
        Assert.Equal(GatewayTelemetry.DecisionDeny, Field(entry, "Decision"));
        Assert.Equal(GatewayTelemetry.ReasonUnauthenticated, Field(entry, "Reason"));
    }

    [Fact]
    public async Task Deny403_MissingScope_CarriesRouteIdAndRequiredScopeFromEndpoint()
    {
        // Authenticated + tenant present but scope missing → coarse 403 missing-scope.
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(GatewayClaims.TenantClaimType, "CONTOSO"));

        var entry = await RunDenyAsync(
            StatusCodes.Status403Forbidden,
            RouteEndpoint("transactions-create", CoarseAuthorization.TransactionsWritePolicy),
            new ClaimsPrincipal(identity));

        Assert.Equal("transactions-create", Field(entry, "RouteId"));
        Assert.Equal(CoarseAuthorization.TransactionsWriteScope, Field(entry, "RequiredScope"));
        Assert.Equal(GatewayTelemetry.DecisionDeny, Field(entry, "Decision"));
        Assert.Equal(GatewayTelemetry.ReasonMissingScope, Field(entry, "Reason"));
    }

    [Fact]
    public async Task Deny403_MissingTenant_CarriesRouteIdAndRequiredScopeFromEndpoint()
    {
        // Authenticated but no tenant claim → coarse 403 missing-tenant. Route metadata is
        // still recovered from the endpoint on this deny path.
        var entry = await RunDenyAsync(
            StatusCodes.Status403Forbidden,
            RouteEndpoint("read-catch-all", CoarseAuthorization.ReadPolicy),
            new ClaimsPrincipal(new ClaimsIdentity("test")));

        Assert.Equal("read-catch-all", Field(entry, "RouteId"));
        Assert.Equal(CoarseAuthorization.ReadScope, Field(entry, "RequiredScope"));
        Assert.Equal(GatewayTelemetry.DecisionDeny, Field(entry, "Decision"));
        Assert.Equal(GatewayTelemetry.ReasonMissingTenant, Field(entry, "Reason"));
    }
}
