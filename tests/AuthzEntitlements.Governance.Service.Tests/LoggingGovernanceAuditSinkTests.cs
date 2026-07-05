using AuthzEntitlements.Governance.Service.Metering;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Proves the default governance audit sink strips CR/LF from request-derived string fields before they
// reach the ILogger message template, so untrusted input (tenant/principal/target/reason/correlationId
// from anonymous request bodies) cannot inject a newline to forge a fake audit log line (CWE-117 log
// injection) — mirrors LoggingPdpDecisionAuditSink.
public sealed class LoggingGovernanceAuditSinkTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(string Message, IReadOnlyList<KeyValuePair<string, object>> State)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var kvps = state as IReadOnlyList<KeyValuePair<string, object>> ?? [];
            Entries.Add((formatter(state, exception), kvps));
        }
    }

    [Fact]
    public void Record_StripsCrLf_FromRequestDerivedFields_ToPreventLogForging()
    {
        var logger = new CapturingLogger<LoggingGovernanceAuditSink>();
        var sink = new LoggingGovernanceAuditSink(logger);

        // Every string here can originate from an anonymous request body; a CR/LF must never survive to
        // the rendered log line (which would let an attacker append a forged "GovernanceDecision" entry).
        sink.Record(new GovernanceDecision(
            "CONTOSO\r\nFORGED tenant",
            "user-1\r\nFORGED principal",
            GovernanceDecisionType.Grant,
            "pkg\r\nFORGED target",
            GovernanceOutcome.GrantIssued,
            "reason\r\nFORGED",
            "corr\r\nFORGED",
            DateTimeOffset.UnixEpoch));

        var entry = Assert.Single(logger.Entries);

        // The rendered log line carries NO raw newline, so the forged "FORGED …" text stays inert on the
        // same line instead of appearing as a separate, attacker-authored audit entry.
        Assert.DoesNotContain("\r", entry.Message);
        Assert.DoesNotContain("\n", entry.Message);
        Assert.Contains("CONTOSO  FORGED tenant", entry.Message);   // \r\n collapsed to two spaces
        Assert.Contains("user-1  FORGED principal", entry.Message);
    }
}
