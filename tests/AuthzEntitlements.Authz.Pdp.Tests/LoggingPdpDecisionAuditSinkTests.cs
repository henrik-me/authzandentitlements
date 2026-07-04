using AuthzEntitlements.Authz.Pdp.Audit;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Proves the default audit sink names EVERY field of the CS16-extended PdpDecisionAuditEvent —
// including the explainability fields (DeterminingRule, PolicyReferences, Narrative) — as
// structured log properties, so a log/OTel pipeline (CS13) captures the explanation, not just
// the decision/reason.
public sealed class LoggingPdpDecisionAuditSinkTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object>> State)> Entries { get; } = [];

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
            Entries.Add((logLevel, formatter(state, exception), kvps));
        }
    }

    private static PdpDecisionAuditEvent Event(
        string determiningRule,
        IReadOnlyList<string> policyReferences,
        string narrative) =>
        new(
            TimestampUtc: DateTimeOffset.UnixEpoch,
            TraceId: "trace-1",
            Provider: "reference",
            SubjectId: "user-1",
            Action: "bank.account.read",
            ResourceType: "account",
            ResourceId: "acct-1",
            Decision: "Deny",
            Reason: "TenantMismatch",
            Tenant: "contoso",
            DeterminingRule: determiningRule,
            PolicyReferences: policyReferences,
            Narrative: narrative);

    [Fact]
    public void Record_EmitsExplanationFields_AsStructuredProperties()
    {
        var logger = new CapturingLogger<LoggingPdpDecisionAuditSink>();
        var sink = new LoggingPdpDecisionAuditSink(logger);
        var evt = Event("tenant", ["rule:tenant"], "The subject's tenant does not match the resource's tenant.");

        sink.Record(evt);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(entry.State, kv => kv.Key == "DeterminingRule" && (string?)kv.Value == "tenant");
        Assert.Contains(entry.State, kv => kv.Key == "PolicyReferences" && (string?)kv.Value == "rule:tenant");
        Assert.Contains(entry.State, kv => kv.Key == "Narrative" && (string?)kv.Value == evt.Narrative);
        Assert.Contains("rule=tenant", entry.Message);
        Assert.Contains("policyReferences=rule:tenant", entry.Message);
    }

    [Fact]
    public void Record_JoinsMultiplePolicyReferences_IntoOneProperty()
    {
        var logger = new CapturingLogger<LoggingPdpDecisionAuditSink>();
        var sink = new LoggingPdpDecisionAuditSink(logger);
        var evt = Event(
            "segregation-of-duties",
            ["cedar-policy:approval.MakerEqualsChecker", "reason-code:MakerEqualsChecker"],
            "Segregation of duties: the checker may not be the maker of the transaction.");

        sink.Record(evt);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(
            entry.State,
            kv => kv.Key == "PolicyReferences"
                && (string?)kv.Value == "cedar-policy:approval.MakerEqualsChecker, reason-code:MakerEqualsChecker");
    }
}
