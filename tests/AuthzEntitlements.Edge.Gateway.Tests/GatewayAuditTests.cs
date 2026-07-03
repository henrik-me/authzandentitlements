using AuthzEntitlements.Edge.Gateway.Audit;
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
}
