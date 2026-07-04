using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// End-to-end proof of the LRN-013 uniform non-authz skip on the Bank.Api fine-grained gate.
// Drives the REAL BankAuthorizationAuditMiddleware.InvokeAsync with a hand-built context and
// asserts that routing non-decisions (an unmatched 404 with no endpoint, or a method-mismatch
// 405) emit NO audit event, while genuine decisions on a matched endpoint (allow / 401 / 403,
// and a business 404) each emit exactly one — matching the edge gate's discipline.
public sealed class BankAuthorizationAuditSkipTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public int Count { get; private set; }
        public IReadOnlyList<KeyValuePair<string, object?>>? Last { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Count++;
            Last = state as IReadOnlyList<KeyValuePair<string, object?>>;
        }
    }

    private static Endpoint SomeEndpoint(string displayName)
        => new(requestDelegate: null, EndpointMetadataCollection.Empty, displayName);

    private static object? Field(IReadOnlyList<KeyValuePair<string, object?>>? entry, string key)
        => entry?.FirstOrDefault(kv => kv.Key == key).Value;

    private static async Task<CapturingLogger<BankAuthorizationAuditMiddleware>> RunAsync(
        string method, int statusCode, Endpoint? endpoint)
    {
        var logger = new CapturingLogger<BankAuthorizationAuditMiddleware>();

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        };

        var middleware = new BankAuthorizationAuditMiddleware(next, logger);

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };
        context.Request.Method = method;
        context.Request.Path = "/api/accounts";
        if (endpoint is not null)
        {
            context.SetEndpoint(endpoint);
        }

        await middleware.InvokeAsync(context);
        return logger;
    }

    [Fact]
    public async Task UnmatchedPath_404_WithNoEndpoint_IsNotAudited()
    {
        var logger = await RunAsync("GET", StatusCodes.Status404NotFound, endpoint: null);
        Assert.Equal(0, logger.Count);
    }

    [Fact]
    public async Task MethodMismatch_405_IsNotAudited()
    {
        // ASP.NET sets a synthetic 405 endpoint; the gate skips on the 405 status regardless.
        var logger = await RunAsync(
            "DELETE", StatusCodes.Status405MethodNotAllowed, SomeEndpoint("405 HTTP Method Not Supported"));
        Assert.Equal(0, logger.Count);
    }

    [Fact]
    public async Task MatchedEndpoint_Allow_IsAudited()
    {
        var logger = await RunAsync("GET", StatusCodes.Status200OK, SomeEndpoint("GET /api/accounts"));

        Assert.Equal(1, logger.Count);
        Assert.Equal(BankAuthorizationAuditMiddleware.DecisionAllow, Field(logger.Last, "Decision"));
        Assert.Equal(BankAuthorizationAuditMiddleware.ReasonAuthorized, Field(logger.Last, "Reason"));
    }

    [Fact]
    public async Task MatchedEndpoint_401_IsAuditedAsDeny()
    {
        var logger = await RunAsync(
            "GET", StatusCodes.Status401Unauthorized, SomeEndpoint("GET /api/accounts"));

        Assert.Equal(1, logger.Count);
        Assert.Equal(BankAuthorizationAuditMiddleware.DecisionDeny, Field(logger.Last, "Decision"));
        Assert.Equal(BankAuthorizationAuditMiddleware.ReasonUnauthenticated, Field(logger.Last, "Reason"));
    }

    [Fact]
    public async Task MatchedEndpoint_403_IsAuditedAsDeny()
    {
        var logger = await RunAsync(
            "POST", StatusCodes.Status403Forbidden, SomeEndpoint("POST /api/accounts"));

        Assert.Equal(1, logger.Count);
        Assert.Equal(BankAuthorizationAuditMiddleware.DecisionDeny, Field(logger.Last, "Decision"));
        Assert.Equal(BankAuthorizationAuditMiddleware.ReasonForbidden, Field(logger.Last, "Reason"));
    }

    [Fact]
    public async Task MatchedEndpoint_BusinessNotFound_404_IsAuditedAsAllow()
    {
        // A matched endpoint whose handler returns 404 (resource not found) IS a genuine
        // allow decision — authorization permitted the request — so it must still be audited.
        var logger = await RunAsync("GET", StatusCodes.Status404NotFound, SomeEndpoint("GET /api/accounts/{id}"));

        Assert.Equal(1, logger.Count);
        Assert.Equal(BankAuthorizationAuditMiddleware.DecisionAllow, Field(logger.Last, "Decision"));
        Assert.Equal(BankAuthorizationAuditMiddleware.ReasonAuthorized, Field(logger.Last, "Reason"));
    }
}
