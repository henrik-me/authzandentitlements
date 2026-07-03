using System;
using AuthzEntitlements.Bank.Api.Auth;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Guards the fine-grained audit classification and event contract. Bank.Api is the
// terminal fine decider, so its own 401/403 are its authorization decisions and any
// other status is an allow. ClassifyDecision is pure, so it is unit-tested directly.
public sealed class BankAuthorizationAuditTests
{
    [Theory]
    [InlineData(401, BankAuthorizationAuditMiddleware.DecisionDeny, BankAuthorizationAuditMiddleware.ReasonUnauthenticated)]
    [InlineData(403, BankAuthorizationAuditMiddleware.DecisionDeny, BankAuthorizationAuditMiddleware.ReasonForbidden)]
    [InlineData(200, BankAuthorizationAuditMiddleware.DecisionAllow, BankAuthorizationAuditMiddleware.ReasonAuthorized)]
    [InlineData(201, BankAuthorizationAuditMiddleware.DecisionAllow, BankAuthorizationAuditMiddleware.ReasonAuthorized)]
    [InlineData(400, BankAuthorizationAuditMiddleware.DecisionAllow, BankAuthorizationAuditMiddleware.ReasonAuthorized)]
    [InlineData(404, BankAuthorizationAuditMiddleware.DecisionAllow, BankAuthorizationAuditMiddleware.ReasonAuthorized)]
    [InlineData(409, BankAuthorizationAuditMiddleware.DecisionAllow, BankAuthorizationAuditMiddleware.ReasonAuthorized)]
    public void ClassifyDecision_MapsStatusToDecisionAndReason(int statusCode, string expectedDecision, string expectedReason)
    {
        var (decision, reason) = BankAuthorizationAuditMiddleware.ClassifyDecision(statusCode);

        Assert.Equal(expectedDecision, decision);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void BankAuditEvent_CarriesConstructedValues()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var evt = new BankAuditEvent(
            TimestampUtc: timestamp,
            TraceId: "trace-123",
            Method: "POST",
            Path: "/api/transactions",
            Decision: BankAuthorizationAuditMiddleware.DecisionDeny,
            Reason: BankAuthorizationAuditMiddleware.ReasonForbidden,
            Subject: "11111111-1111-1111-1111-111111111111",
            Tenant: "CONTOSO",
            StatusCode: 403);

        Assert.Equal(timestamp, evt.TimestampUtc);
        Assert.Equal("trace-123", evt.TraceId);
        Assert.Equal("POST", evt.Method);
        Assert.Equal("/api/transactions", evt.Path);
        Assert.Equal(BankAuthorizationAuditMiddleware.DecisionDeny, evt.Decision);
        Assert.Equal(BankAuthorizationAuditMiddleware.ReasonForbidden, evt.Reason);
        Assert.Equal("11111111-1111-1111-1111-111111111111", evt.Subject);
        Assert.Equal("CONTOSO", evt.Tenant);
        Assert.Equal(403, evt.StatusCode);
    }

    [Fact]
    public void BankAuditEvent_AllowsNullSubjectAndTenant()
    {
        // A request denied before authentication (401) carries no subject/tenant;
        // the record fails open to null rather than fabricating a value.
        var evt = new BankAuditEvent(
            TimestampUtc: DateTimeOffset.UtcNow,
            TraceId: "trace-xyz",
            Method: "GET",
            Path: "/api/accounts",
            Decision: BankAuthorizationAuditMiddleware.DecisionDeny,
            Reason: BankAuthorizationAuditMiddleware.ReasonUnauthenticated,
            Subject: null,
            Tenant: null,
            StatusCode: 401);

        Assert.Null(evt.Subject);
        Assert.Null(evt.Tenant);
    }
}
