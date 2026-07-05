using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS36 (LRN-057): every PDP decision must carry a canonical snapshot of the request that produced
// it, so the Audit Explorer can replay it faithfully. These prove the capture point wires the
// serializer into the audit event without disturbing the decision.
public sealed class RequestSnapshotCaptureTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private sealed class CapturingAuditSink : IPdpDecisionAuditSink
    {
        public List<PdpDecisionAuditEvent> Events { get; } = [];

        public void Record(PdpDecisionAuditEvent decisionEvent) => Events.Add(decisionEvent);
    }

    // Captures warning-level lines so a test can assert the snapshot fail-open warning fires (or, on
    // the happy path, does NOT fire spuriously on every decision).
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private static PdpDecisionService CreateService(IPdpDecisionAuditSink sink) =>
        new(
            new AuthorizationDecisionProviderFactory(
                [new ReferenceDecisionProvider()],
                Options.Create(new PdpOptions { Provider = "reference" })),
            sink);

    [Fact]
    public void Evaluate_CapturesRequestSnapshot_CarryingEveryAbacInput()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource(
                "transaction",
                Tenant: PdpRequests.Contoso,
                Amount: 15_000m,
                MakerId: "maker",
                Status: "Pending"),
            ScopeNames.TransactionsWrite);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.False(string.IsNullOrWhiteSpace(evt.RequestSnapshot));
        var restored = JsonSerializer.Deserialize<AccessRequest>(evt.RequestSnapshot!, WebOptions);
        Assert.NotNull(restored);
        Assert.Equal("maker", restored!.Subject.Id);
        Assert.Equal(PdpRequests.Contoso, restored.Subject.Tenant);
        Assert.Contains(RoleNames.Teller, restored.Subject.Roles);
        Assert.Equal(ActionNames.TransactionCreate, restored.Action.Name);
        Assert.Equal(15_000m, restored.Resource.Amount);
        Assert.Equal("maker", restored.Resource.MakerId);
        Assert.Equal("Pending", restored.Resource.Status);
        Assert.Contains(ScopeNames.TransactionsWrite, restored.Context.Scopes);
    }

    [Fact]
    public void Evaluate_SnapshotEqualsTheDirectSerialization()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Id: "acct-1", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.Equal(RequestSnapshotSerializer.TrySerialize(request), evt.RequestSnapshot);
    }

    [Fact]
    public void Evaluate_OnDeny_StillCapturesSnapshot()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Fabrikam),
            ScopeNames.Read);

        var decision = service.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        var evt = Assert.Single(sink.Events);
        Assert.False(string.IsNullOrWhiteSpace(evt.RequestSnapshot));
        Assert.Contains(PdpRequests.Fabrikam, evt.RequestSnapshot);
    }

    [Fact]
    public void Evaluate_OnSuccessfulSnapshot_LogsNoWarning()
    {
        // The fail-open snapshot warning must fire ONLY when serialization degrades to null — never
        // on a normal decision whose snapshot captures fine, so the log stays quiet on the hot path.
        var sink = new CapturingAuditSink();
        var logger = new CapturingLogger<PdpDecisionService>();
        var service = new PdpDecisionService(
            new AuthorizationDecisionProviderFactory(
                [new ReferenceDecisionProvider()],
                Options.Create(new PdpOptions { Provider = "reference" })),
            sink,
            logger);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Id: "acct-1", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.False(string.IsNullOrWhiteSpace(evt.RequestSnapshot));
        Assert.Empty(logger.Warnings);
    }
}
