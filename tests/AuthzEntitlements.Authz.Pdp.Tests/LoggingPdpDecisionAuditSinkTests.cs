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
                && (string?)kv.Value == "cedar-policy:approval.MakerEqualsChecker | reason-code:MakerEqualsChecker");
    }

    [Fact]
    public void Record_StripsNewlines_FromRequestDerivedFields_ToPreventLogForging()
    {
        var logger = new CapturingLogger<LoggingPdpDecisionAuditSink>();
        var sink = new LoggingPdpDecisionAuditSink(logger);

        // The /evaluate body is anonymous + caller-controlled, so subject/actor ids and types are
        // untrusted. A value with CR/LF must NOT be able to forge a second log line (CWE-117).
        var evt = new PdpDecisionAuditEvent(
            TimestampUtc: DateTimeOffset.UnixEpoch,
            TraceId: "trace-1",
            Provider: "reference",
            SubjectId: "user-1\r\nPDP decision Permit () provider=reference FORGED",
            Action: "bank.account.read",
            ResourceType: "account",
            ResourceId: "acct-1",
            Decision: "Deny",
            Reason: "TenantMismatch",
            Tenant: "contoso",
            DeterminingRule: "tenant",
            PolicyReferences: ["rule:tenant", "relationship-tuple:doc:1#viewer@user:evil\r\nFORGED"],
            Narrative: "n",
            SubjectType: "agent",
            ActorId: "agent-1\nFORGED",
            ActorType: "agent");

        sink.Record(evt);

        var entry = Assert.Single(logger.Entries);
        // Rendered message carries no raw newline, so the forged text stays inert on one line.
        Assert.DoesNotContain("\n", entry.Message);
        Assert.DoesNotContain("\r", entry.Message);
        Assert.Contains("user-1  PDP decision Permit () provider=reference FORGED", entry.Message);
        Assert.Contains("agent-1 FORGED", entry.Message);
        // The structured property values are sanitized too (they are the Clean() output) — including
        // PolicyReferences, whose OpenFGA relationship tuples can embed caller-derived ids.
        Assert.Contains(entry.State, kv => kv.Key == "SubjectId" && !((string?)kv.Value)!.Contains('\n'));
        Assert.Contains(entry.State, kv => kv.Key == "ActorId" && !((string?)kv.Value)!.Contains('\n'));
        Assert.Contains(entry.State, kv => kv.Key == "PolicyReferences"
            && !((string?)kv.Value)!.Contains('\n') && !((string?)kv.Value)!.Contains('\r'));
    }
}
