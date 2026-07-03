using System.Diagnostics.Metrics;
using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using AuthzEntitlements.Authz.Pdp.Telemetry;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Coverage for the R2 Copilot-hardening round: a blank provider config falls back to the
// default engine, caller-supplied action names are normalized to a bounded metric tag, and a
// provider that violates the "at least one reason" contract is never mislabelled as a permit.
public sealed class CopilotHardeningTests
{
    // ---- Factory: blank config falls back to the default provider ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Factory_BlankProviderConfig_DefaultsToReference(string? configured)
    {
        var factory = new AuthorizationDecisionProviderFactory(
            [new ReferenceDecisionProvider()],
            Options.Create(new PdpOptions { Provider = configured! }));

        Assert.Equal("reference", factory.GetActiveProvider().Name);
    }

    // ---- ActionNames.ForMetric normalization ----

    [Theory]
    [InlineData(ActionNames.AccountRead)]
    [InlineData(ActionNames.AccountCreate)]
    [InlineData(ActionNames.TransactionCreate)]
    [InlineData(ActionNames.TransactionApprove)]
    [InlineData(ActionNames.TransactionReject)]
    public void ForMetric_KnownAction_PassesThrough(string action) =>
        Assert.Equal(action, ActionNames.ForMetric(action));

    [Theory]
    [InlineData("bank.account.delete")]
    [InlineData("totally.made.up")]
    [InlineData("")]
    public void ForMetric_UnknownAction_CollapsesToUnknown(string action) =>
        Assert.Equal(ActionNames.Unknown, ActionNames.ForMetric(action));

    // ---- Reason fallback: a reasonless Deny is never mislabelled as Permit ----

    [Fact]
    public void DecisionService_ProviderReturnsReasonlessDeny_AuditRecordsDenyNotPermit()
    {
        var captured = new List<PdpDecisionAuditEvent>();
        var service = new PdpDecisionService(
            new AuthorizationDecisionProviderFactory(
                [new ReasonlessDenyProvider()],
                Options.Create(new PdpOptions { Provider = "reasonless" })),
            new CapturingSink(captured));

        service.Evaluate(PdpRequests.For(
            PdpRequests.User("u", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read));

        var evt = Assert.Single(captured);
        Assert.Equal(Decision.Deny.ToString(), evt.Decision);
        Assert.Equal(Decision.Deny.ToString(), evt.Reason);
        Assert.NotEqual(ReasonCodes.Permit, evt.Reason);
    }

    // ---- Metric action tag is normalized for a caller-supplied unknown action ----

    [Fact]
    public void DecisionService_UnknownAction_MetricActionTagIsNormalized()
    {
        var actionTags = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PdpTelemetry.Name && instrument.Name == "pdp.decisions.total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "action")
                {
                    lock (actionTags)
                    {
                        actionTags.Add(tag.Value?.ToString());
                    }
                }
            }
        });
        listener.Start();

        var service = new PdpDecisionService(
            new AuthorizationDecisionProviderFactory(
                [new ReferenceDecisionProvider()],
                Options.Create(new PdpOptions { Provider = "reference" })),
            new CapturingSink([]));

        // A caller-supplied action outside the vocabulary: the counter tag must collapse to
        // "unknown"; the raw verb must never reach the metric backend.
        service.Evaluate(PdpRequests.For(
            PdpRequests.User("u", PdpRequests.Contoso, RoleNames.Teller),
            "bank.account.delete",
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read));

        listener.Dispose();

        Assert.Contains(ActionNames.Unknown, actionTags);
        Assert.DoesNotContain("bank.account.delete", actionTags);
    }

    // ---- Factory fails fast on blank or duplicate provider names ----

    [Fact]
    public void Factory_DuplicateProviderNames_ThrowsAtConstruction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AuthorizationDecisionProviderFactory(
                [new NamedProvider("dup"), new NamedProvider("DUP")],
                Options.Create(new PdpOptions { Provider = "dup" })));

        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Factory_BlankProviderName_ThrowsAtConstruction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new AuthorizationDecisionProviderFactory(
                [new NamedProvider("   ")],
                Options.Create(new PdpOptions { Provider = "reference" })));

        Assert.Contains("blank Name", ex.Message);
    }

    [Fact]
    public void Factory_DistinctProviderNames_Constructs()
    {
        var factory = new AuthorizationDecisionProviderFactory(
            [new ReferenceDecisionProvider(), new NamedProvider("casbin")],
            Options.Create(new PdpOptions { Provider = "casbin" }));

        Assert.Equal("casbin", factory.GetActiveProvider().Name);
    }

    private sealed class NamedProvider(string name) : IAuthorizationDecisionProvider
    {
        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request) =>
            AccessDecision.Deny(new Reason(ReasonCodes.UnknownAction, "named stub"));
    }

    private sealed class ReasonlessDenyProvider : IAuthorizationDecisionProvider
    {
        public string Name => "reasonless";

        // Deliberately violates the "at least one reason" contract to exercise the fallback.
        public AccessDecision Evaluate(AccessRequest request) => new(Decision.Deny, [], []);
    }

    private sealed class CapturingSink(List<PdpDecisionAuditEvent> sink) : IPdpDecisionAuditSink
    {
        public void Record(PdpDecisionAuditEvent decisionEvent) => sink.Add(decisionEvent);
    }
}
