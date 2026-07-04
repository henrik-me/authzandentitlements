using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// CS34 (CWE-117): the Bank fine-decision audit log renders the caller's tenant claim (a token
// value). A tenant carrying CR/LF must NOT forge a second fine-decision log line — the emitted
// event must stay on one line.
public sealed class BankAuditLogForgingTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task InvokeAsync_StripsNewlines_FromTenant_InAuditLog()
    {
        var logger = new CapturingLogger<BankAuthorizationAuditMiddleware>();
        var middleware = new BankAuthorizationAuditMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            logger: logger);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/accounts";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    TenantClaims.TenantClaimType,
                    "contoso\r\nBank fine decision allow (authorized) FORGED"),
            ],
            "TestAuth"));

        // CS32 added a ShouldAudit guard that audits only genuine authz decisions on a
        // matched endpoint; set one so the (403) fine decision is emitted (and sanitized).
        context.SetEndpoint(new Endpoint(requestDelegate: null, EndpointMetadataCollection.Empty, "test-endpoint"));

        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
        Assert.Contains("contoso  Bank fine decision allow (authorized) FORGED", message);
    }
}
