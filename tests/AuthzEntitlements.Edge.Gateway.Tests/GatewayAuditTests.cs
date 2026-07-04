using AuthzEntitlements.Edge.Gateway.Audit;
using AuthzEntitlements.Edge.Gateway.Auth;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Table-drives the pure decision classifier plus a sanity check that the
// audit record faithfully carries the values it is constructed with.
public sealed class GatewayAuditTests
{
    // edgeAuthorized = false: the request was denied AT the edge (UseAuthorization
    // short-circuited), so the status maps to the coarse deny reason.
    public static TheoryData<int, bool, string, string> EdgeDeniedCases => new()
    {
        { 401, true, "deny", "unauthenticated" },
        { 401, false, "deny", "unauthenticated" },
        { 403, false, "deny", "missing-tenant" },
        { 403, true, "deny", "missing-scope" },
    };

    [Theory]
    [MemberData(nameof(EdgeDeniedCases))]
    public void ClassifyDecision_WhenDeniedAtEdge_MapsStatusAndTenant(
        int statusCode, bool hasTenant, string expectedDecision, string expectedReason)
    {
        var (decision, reason) =
            GatewayAuditMiddleware.ClassifyDecision(statusCode, hasTenant, edgeAuthorized: false);
        Assert.Equal(expectedDecision, decision);
        Assert.Equal(expectedReason, reason);
    }

    // edgeAuthorized = true: the request cleared the coarse policy and was routed,
    // so EVERY resulting status is an edge allow/routed — including a downstream
    // fine-grained 401/403 from Bank.Api. This is the case a status-only classifier
    // got wrong: a non-BranchManager POST /api/accounts is edge-allowed, then 403'd
    // by Bank.Api, and must NOT be audited as a coarse edge deny.
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(403)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public void ClassifyDecision_WhenRouted_IsAlwaysAllowRouted(int downstreamStatus)
    {
        foreach (var hasTenant in new[] { true, false })
        {
            var (decision, reason) =
                GatewayAuditMiddleware.ClassifyDecision(downstreamStatus, hasTenant, edgeAuthorized: true);
            Assert.Equal("allow", decision);
            Assert.Equal("routed", reason);
        }
    }

    // A request is audited only when it is a coarse decision: routed (edgeAuthorized)
    // or short-circuited at the edge (401/403). An unmatched /api method that 404s is
    // NOT a coarse decision and must not be audited (and never as a false allow/routed).
    [Theory]
    [InlineData(true, 200, true)]
    [InlineData(true, 403, true)]
    [InlineData(true, 404, true)]
    [InlineData(false, 401, true)]
    [InlineData(false, 403, true)]
    [InlineData(false, 404, false)]
    [InlineData(false, 405, false)]
    [InlineData(false, 200, false)]
    public void ShouldAudit_OnlyForCoarseDecisions(bool edgeAuthorized, int statusCode, bool expected)
    {
        Assert.Equal(expected, GatewayAuditMiddleware.ShouldAudit(edgeAuthorized, statusCode));
    }

    [Fact]
    public void GatewayAuditEvent_CarriesConstructedValues()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var evt = new GatewayAuditEvent(
            TimestampUtc: timestamp,
            TraceId: "trace-1",
            Method: "POST",
            Path: "/api/transactions",
            RouteId: "transactions-create",
            Decision: "deny",
            Reason: "missing-scope",
            Subject: "user-123",
            Tenant: "CONTOSO",
            RequiredScope: "bank.transactions.write",
            Audience: "bank-api");

        Assert.Equal(timestamp, evt.TimestampUtc);
        Assert.Equal("trace-1", evt.TraceId);
        Assert.Equal("POST", evt.Method);
        Assert.Equal("/api/transactions", evt.Path);
        Assert.Equal("transactions-create", evt.RouteId);
        Assert.Equal("deny", evt.Decision);
        Assert.Equal("missing-scope", evt.Reason);
        Assert.Equal("user-123", evt.Subject);
        Assert.Equal("CONTOSO", evt.Tenant);
        Assert.Equal("bank.transactions.write", evt.RequiredScope);
        Assert.Equal("bank-api", evt.Audience);
    }

    // ---- ResolveRouteMetadata: LRN-013 edge-denial RouteId/RequiredScope enrichment ----

    // Builds an endpoint carrying a YARP RouteModel exactly as ProxyEndpointFactory does
    // (endpointBuilder.Metadata.Add(route)), so the resolver reads the same metadata that
    // routing leaves on the context after a short-circuit deny.
    private static Endpoint EndpointWithRoute(string routeId, string? authorizationPolicy)
    {
        var model = new RouteModel(
            new RouteConfig { RouteId = routeId, AuthorizationPolicy = authorizationPolicy },
            cluster: null,
            HttpTransformer.Empty);
        return new Endpoint(requestDelegate: null, new EndpointMetadataCollection(model), routeId);
    }

    // The proxy-feature route config (present on an edge allow/routed) always wins over the
    // endpoint fallback, so the routed/allow path keeps its existing behavior unchanged.
    [Fact]
    public void ResolveRouteMetadata_WhenProxyFeatureConfigPresent_TakesPrecedenceOverEndpoint()
    {
        var (routeId, requiredScope) = GatewayAuditMiddleware.ResolveRouteMetadata(
            new RouteConfig
            {
                RouteId = "transactions-create",
                AuthorizationPolicy = CoarseAuthorization.TransactionsWritePolicy,
            },
            EndpointWithRoute("read-catch-all", CoarseAuthorization.ReadPolicy));

        Assert.Equal("transactions-create", routeId);
        Assert.Equal(CoarseAuthorization.TransactionsWriteScope, requiredScope);
    }

    // On a short-circuit 401/403 deny the IReverseProxyFeature is unset, so the resolver
    // recovers RouteId + RequiredScope from the matched endpoint's YARP RouteModel — the
    // LRN-013 gap. coarse.authenticated carries no required scope, so its scope stays null.
    public static TheoryData<string, string, string> DenyFallbackCases => new()
    {
        { "transactions-create", CoarseAuthorization.TransactionsWritePolicy, CoarseAuthorization.TransactionsWriteScope },
        { "approvals-approve", CoarseAuthorization.ApprovalsWritePolicy, CoarseAuthorization.ApprovalsWriteScope },
        { "read-catch-all", CoarseAuthorization.ReadPolicy, CoarseAuthorization.ReadScope },
    };

    [Theory]
    [MemberData(nameof(DenyFallbackCases))]
    public void ResolveRouteMetadata_WhenProxyFeatureNull_FallsBackToEndpointRouteModel(
        string routeId, string policy, string expectedScope)
    {
        var (actualRouteId, actualScope) = GatewayAuditMiddleware.ResolveRouteMetadata(
            proxyRouteConfig: null, EndpointWithRoute(routeId, policy));

        Assert.Equal(routeId, actualRouteId);
        Assert.Equal(expectedScope, actualScope);
    }

    [Fact]
    public void ResolveRouteMetadata_FallbackForAuthenticatedPolicy_HasRouteIdButNoScope()
    {
        var (routeId, requiredScope) = GatewayAuditMiddleware.ResolveRouteMetadata(
            proxyRouteConfig: null,
            EndpointWithRoute("accounts-create", CoarseAuthorization.AuthenticatedPolicy));

        Assert.Equal("accounts-create", routeId);
        Assert.Null(requiredScope);
    }

    // The unmatched-404 (null endpoint) and method-mismatch-405 (ASP.NET's synthetic
    // endpoint has no RouteModel) non-decisions resolve to (null, null). These are skipped
    // by ShouldAudit before enrichment runs, but the resolver must still fail safe.
    [Fact]
    public void ResolveRouteMetadata_WhenNoEndpoint_IsNull()
    {
        var (routeId, requiredScope) =
            GatewayAuditMiddleware.ResolveRouteMetadata(proxyRouteConfig: null, endpoint: null);

        Assert.Null(routeId);
        Assert.Null(requiredScope);
    }

    [Fact]
    public void ResolveRouteMetadata_WhenEndpointHasNoRouteModel_IsNull()
    {
        var endpoint = new Endpoint(
            requestDelegate: null, EndpointMetadataCollection.Empty, "405 HTTP Method Not Supported");

        var (routeId, requiredScope) =
            GatewayAuditMiddleware.ResolveRouteMetadata(proxyRouteConfig: null, endpoint);

        Assert.Null(routeId);
        Assert.Null(requiredScope);
    }
}
