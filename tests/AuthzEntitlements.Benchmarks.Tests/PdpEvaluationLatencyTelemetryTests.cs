using System.Diagnostics.Metrics;
using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using AuthzEntitlements.Authz.Pdp.Telemetry;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Proves the CS24 telemetry edit: invoking PdpDecisionService.Evaluate records at least one
// measurement on the new pdp.evaluate.duration histogram, carrying the same low-cardinality
// provider/action/decision/reason tag vocabulary as the decision counter. The service is built with
// the same in-process factory + audit-sink pattern the existing Pdp tests use. Telemetry primitives
// are process-wide statics, so the test isolates its own measurement by a unique provider name.
public sealed class PdpEvaluationLatencyTelemetryTests
{
    private sealed class CapturingAuditSink : IPdpDecisionAuditSink
    {
        public List<PdpDecisionAuditEvent> Events { get; } = [];

        public void Record(PdpDecisionAuditEvent decisionEvent) => Events.Add(decisionEvent);
    }

    // A provider with a unique, caller-chosen name returning a fixed decision, so the histogram test
    // isolates its measurement by the (never-normalized) provider tag under the shared meter.
    private sealed class FixedDenyProvider(string name) : IAuthorizationDecisionProvider
    {
        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request) =>
            AccessDecision.Deny(new Reason(ReasonCodes.TenantMismatch, "fixed deny for latency test"));
    }

    [Fact]
    public void Evaluate_RecordsEvaluationDurationMeasurement_WithExpectedTags()
    {
        var providerName = $"latency-probe-{Guid.NewGuid():N}";
        var gate = new object();
        var captured = new List<KeyValuePair<string, object?>[]>();
        var count = 0;

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == PdpTelemetry.Name
                    && instrument.Name == "pdp.evaluate.duration")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
            {
                var copy = tags.ToArray();
                if (copy.Any(t => t.Key == "provider" && (string?)t.Value == providerName))
                {
                    lock (gate)
                    {
                        captured.Add(copy);
                        count++;
                        Assert.True(measurement >= 0, "evaluation duration must be non-negative");
                    }
                }
            });
            listener.Start();

            var service = new PdpDecisionService(
                new AuthorizationDecisionProviderFactory(
                    [new FixedDenyProvider(providerName)],
                    Options.Create(new PdpOptions { Provider = providerName })),
                new CapturingAuditSink());

            var request = new AccessRequest(
                new Subject("user", "user-teller1", [RoleNames.Teller], "CONTOSO"),
                new ActionRequest(ActionNames.AccountRead),
                new Resource("account", Tenant: "FABRIKAM"),
                new EvaluationContext([ScopeNames.Read]));

            var decision = service.Evaluate(request);
            Assert.Equal(Decision.Deny, decision.Decision);
        }

        Assert.True(count >= 1, "expected at least one pdp.evaluate.duration measurement");
        var recordedTags = captured[0];
        Assert.Contains(recordedTags, t => t.Key == "provider" && (string?)t.Value == providerName);
        Assert.Contains(recordedTags, t => t.Key == "action" && (string?)t.Value == ActionNames.AccountRead);
        Assert.Contains(recordedTags, t => t.Key == "decision" && (string?)t.Value == "Deny");
        Assert.Contains(recordedTags, t => t.Key == "reason" && (string?)t.Value == ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void EvaluationDurationInstrument_HasExpectedNameAndUnit()
    {
        Assert.Equal("pdp.evaluate.duration", PdpTelemetry.EvaluationDuration.Name);
        Assert.Equal("ms", PdpTelemetry.EvaluationDuration.Unit);
        Assert.Equal(PdpTelemetry.Name, PdpTelemetry.EvaluationDuration.Meter.Name);
    }
}
