using AuthzEntitlements.Edge.Gateway.Audit;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Table-drives the pure decision classifier plus a sanity check that the
// audit record faithfully carries the values it is constructed with.
public sealed class GatewayAuditTests
{
    public static TheoryData<int, bool, string, string> Cases => new()
    {
        { 401, true, "deny", "unauthenticated" },
        { 401, false, "deny", "unauthenticated" },
        { 403, false, "deny", "missing-tenant" },
        { 403, true, "deny", "missing-scope" },
        { 200, true, "allow", "routed" },
        { 201, false, "allow", "routed" },
        { 404, true, "allow", "routed" },
        { 500, false, "allow", "routed" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ClassifyDecision_MapsStatusAndTenant(
        int statusCode, bool hasTenant, string expectedDecision, string expectedReason)
    {
        var (decision, reason) = GatewayAuditMiddleware.ClassifyDecision(statusCode, hasTenant);
        Assert.Equal(expectedDecision, decision);
        Assert.Equal(expectedReason, reason);
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
