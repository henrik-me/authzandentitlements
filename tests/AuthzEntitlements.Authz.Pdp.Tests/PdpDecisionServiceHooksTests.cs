using System.Diagnostics;
using System.Diagnostics.Metrics;
using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using AuthzEntitlements.Authz.Pdp.Telemetry;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Proves the service wraps the selected provider with the audit + OTel hooks: exactly one
// audit event per decision (carrying the right fields), the counter increments with the
// low-cardinality tag vocabulary, and the span carries the decision/reason tags. Telemetry
// primitives are process-wide statics, so the metric/span tests filter by a unique probe
// action to stay robust against any parallel emitter.
public sealed class PdpDecisionServiceHooksTests
{
    private sealed class CapturingAuditSink : IPdpDecisionAuditSink
    {
        public List<PdpDecisionAuditEvent> Events { get; } = [];

        public void Record(PdpDecisionAuditEvent decisionEvent) => Events.Add(decisionEvent);
    }

    private static PdpDecisionService CreateService(IPdpDecisionAuditSink sink) =>
        new(
            new AuthorizationDecisionProviderFactory(
                [new ReferenceDecisionProvider()],
                Options.Create(new PdpOptions { Provider = "reference" })),
            sink);

    [Fact]
    public void ProviderName_ReflectsSelectedProvider()
    {
        var service = CreateService(new CapturingAuditSink());

        Assert.Equal("reference", service.ProviderName);
    }

    [Fact]
    public void Evaluate_OnPermit_RecordsExactlyOneAuditEvent_WithAllFields()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Id: "acct-1", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = service.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        var evt = Assert.Single(sink.Events);
        Assert.Equal("reference", evt.Provider);
        Assert.Equal("user-teller1", evt.SubjectId);
        Assert.Equal(ActionNames.AccountRead, evt.Action);
        Assert.Equal("account", evt.ResourceType);
        Assert.Equal("acct-1", evt.ResourceId);
        Assert.Equal("Permit", evt.Decision);
        Assert.Equal(ReasonCodes.Permit, evt.Reason);
        Assert.Equal(PdpRequests.Contoso, evt.Tenant);
    }

    [Fact]
    public void Evaluate_OnDeny_RecordsOneAuditEvent_CarryingDenyReason()
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
        Assert.Equal("Deny", evt.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, evt.Reason);
        Assert.Null(evt.ResourceId);
    }

    [Fact]
    public void Evaluate_ReturnsSameDecisionAsProviderDirectly()
    {
        var service = CreateService(new CapturingAuditSink());
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 15_000m, MakerId: "maker"),
            ScopeNames.TransactionsWrite);

        var viaService = service.Evaluate(request);
        var viaProvider = new ReferenceDecisionProvider().Evaluate(request);

        Assert.Equal(viaProvider.Decision, viaService.Decision);
        Assert.Equal(viaProvider.Reasons[0].Code, viaService.Reasons[0].Code);
        Assert.Equal(
            viaProvider.Obligations.Select(o => o.Id),
            viaService.Obligations.Select(o => o.Id));
    }

    [Fact]
    public void Evaluate_RecordsOneAuditEventPerCall()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);
        service.Evaluate(request);
        service.Evaluate(request);

        var recorded = sink.Events.Count;
        Assert.Equal(3, recorded);
    }

    [Fact]
    public void Evaluate_IncrementsDecisionCounter_WithProviderActionDecisionReasonTags()
    {
        // A unique probe action keeps this test's measurement unambiguous even if another
        // test emits on the same shared counter concurrently.
        var probeAction = $"bank.metric.probe.{Guid.NewGuid():N}";
        var gate = new object();
        var captured = new List<KeyValuePair<string, object?>[]>();
        var total = 0L;

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PdpTelemetry.Name
                    && instrument.Name == "pdp.decisions.total")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
            {
                var copy = tags.ToArray();
                if (copy.Any(t => t.Key == "action" && (string?)t.Value == probeAction))
                {
                    lock (gate)
                    {
                        captured.Add(copy);
                        total += measurement;
                    }
                }
            });
            listener.Start();

            var service = CreateService(new CapturingAuditSink());
            var request = PdpRequests.For(
                PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
                probeAction,
                new Resource("account", Tenant: PdpRequests.Contoso),
                ScopeNames.Read);

            var decision = service.Evaluate(request);
            Assert.Equal(Decision.Deny, decision.Decision); // unknown action fails closed
        }

        Assert.Equal(1L, total);
        var tags = Assert.Single(captured);
        Assert.Contains(tags, t => t.Key == "provider" && (string?)t.Value == "reference");
        Assert.Contains(tags, t => t.Key == "action" && (string?)t.Value == probeAction);
        Assert.Contains(tags, t => t.Key == "decision" && (string?)t.Value == "Deny");
        Assert.Contains(tags, t => t.Key == "reason" && (string?)t.Value == ReasonCodes.UnknownAction);
    }

    [Fact]
    public void StartDecisionActivity_WhenListenerSamples_CarriesProviderAndActionTags()
    {
        using var listener = SamplingListener(_ => { });
        ActivitySource.AddActivityListener(listener);

        using var activity = PdpTelemetry.StartDecisionActivity("reference", ActionNames.AccountRead);

        Assert.NotNull(activity);
        Assert.Equal("pdp.evaluate", activity!.OperationName);
        Assert.Equal("reference", activity.GetTagItem("pdp.provider"));
        Assert.Equal(ActionNames.AccountRead, activity.GetTagItem("pdp.action"));
    }

    [Fact]
    public void Evaluate_WhenTraced_SetsDecisionAndReasonTagsOnSpan()
    {
        // Unique probe action so only THIS test's span is matched under parallel execution.
        var probeAction = $"bank.metric.probe.{Guid.NewGuid():N}";
        var gate = new object();
        var stopped = new List<Activity>();

        using (var listener = SamplingListener(activity =>
        {
            lock (gate)
            {
                stopped.Add(activity);
            }
        }))
        {
            ActivitySource.AddActivityListener(listener);

            var service = CreateService(new CapturingAuditSink());
            var request = PdpRequests.For(
                PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
                probeAction,
                new Resource("account", Tenant: PdpRequests.Contoso),
                ScopeNames.Read);

            service.Evaluate(request);
        }

        Activity span;
        lock (gate)
        {
            span = Assert.Single(
                stopped, a => (string?)a.GetTagItem("pdp.action") == probeAction);
        }

        Assert.Equal("reference", span.GetTagItem("pdp.provider"));
        Assert.Equal("Deny", span.GetTagItem("pdp.decision"));
        Assert.Equal(ReasonCodes.UnknownAction, span.GetTagItem("pdp.reason"));
    }

    [Fact]
    public void Telemetry_MeterCounterAndActivitySource_HaveExpectedNames()
    {
        Assert.Equal("AuthzEntitlements.Authz.Pdp", PdpTelemetry.Name);
        Assert.Equal(PdpTelemetry.Name, PdpTelemetry.Meter.Name);
        Assert.Equal(PdpTelemetry.Name, PdpTelemetry.ActivitySource.Name);
        Assert.Equal("pdp.decisions.total", PdpTelemetry.DecisionsTotal.Name);
    }

    private static ActivityListener SamplingListener(Action<Activity> onStopped) =>
        new()
        {
            ShouldListenTo = source => source.Name == PdpTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = onStopped,
        };
}
