using System.Diagnostics.Metrics;
using System.Security.Claims;
using AuthzEntitlements.Edge.Gateway.Audit;
using AuthzEntitlements.Edge.Gateway.Auth;
using AuthzEntitlements.Edge.Gateway.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// CS34 (CWE-117): the gateway audit log renders the caller's subject/tenant, which come from the
// (attacker-influenced) token. A subject carrying CR/LF must NOT forge a second coarse-decision log
// line — the emitted event must stay on one line.
public sealed class GatewayAuditLogForgingTests
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

    private static GatewayMetrics BuildMetrics() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());

    [Fact]
    public async Task InvokeAsync_StripsNewlines_FromSubjectAndTenant_InAuditLog()
    {
        var logger = new CapturingLogger<GatewayAuditMiddleware>();
        var middleware = new GatewayAuditMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            logger: logger,
            metrics: BuildMetrics(),
            configuration: new ConfigurationBuilder().Build());

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/accounts";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    GatewayClaims.SubjectClaimType,
                    "user-1\r\nGateway coarse decision allow (routed) FORGED"),
                new Claim(GatewayClaims.TenantClaimType, "contoso\nFORGED"),
            ],
            "TestAuth"));

        await middleware.InvokeAsync(context);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
        Assert.Contains("user-1  Gateway coarse decision allow (routed) FORGED", message);
    }
}
