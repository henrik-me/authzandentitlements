using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Playground;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests.Playground;

// The AuthZ Playground fan-out service (CS15): run ONE request across every engine (or a named
// subset) and return per-engine comparable results. Covers full-family fan-out, per-engine result
// shape (one per engine, latency, reasons, explanation), the AllAgree cross-engine verdict, the
// named-subset path, and the offline availability classification (a fail-closed Deny and a throwing
// engine are both reported unavailable and excluded from AllAgree) — all without a live server.
public sealed class PlaygroundFanoutServiceTests
{
    private static PlaygroundFanoutService Service(AuthorizationDecisionProviderFactory factory) =>
        new(factory);

    private static PlaygroundFanoutService RbacService() =>
        Service(LifecycleTestSupport.RbacFactory());

    [Fact]
    public void Fanout_NullEngines_FansOutAcrossEveryRegisteredProvider()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.Equal(LifecycleTestSupport.RbacEngineNames.Length, response.Results.Count);
        Assert.Equal(
            LifecycleTestSupport.RbacEngineNames,
            response.Results.Select(r => r.Engine).ToArray());
    }

    [Fact]
    public void Fanout_EmptyEngines_FansOutAcrossEveryRegisteredProvider()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), []);

        Assert.Equal(LifecycleTestSupport.RbacEngineNames.Length, response.Results.Count);
    }

    [Fact]
    public void Fanout_ReturnsExactlyOneResultPerEngine()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.Equal(
            response.Results.Count,
            response.Results.Select(r => r.Engine).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Fanout_MeasuresNonNegativeLatencyPerEngine()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.All(response.Results, r => Assert.True(r.LatencyMs >= 0));
    }

    [Fact]
    public void Fanout_Permit_EveryResultCarriesAnExplanation()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.All(response.Results, r => Assert.NotNull(r.Explanation));
    }

    [Fact]
    public void Fanout_Deny_EveryResultCarriesAnExplanation()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.DenyRequest(), null);

        Assert.All(response.Results, r => Assert.NotNull(r.Explanation));
    }

    [Fact]
    public void Fanout_EveryReasonCarriesCodeAndMessage()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.All(response.Results, r =>
        {
            Assert.NotEmpty(r.Reasons);
            Assert.All(r.Reasons, reason =>
            {
                Assert.False(string.IsNullOrWhiteSpace(reason.Code));
                Assert.False(string.IsNullOrWhiteSpace(reason.Message));
            });
        });
    }

    [Fact]
    public void Fanout_AllEnginesPermit_AllAgreeIsTrue()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.True(response.AllAgree);
        Assert.All(response.Results, r => Assert.Equal(Decision.Permit, r.Decision));
        Assert.All(response.Results, r => Assert.True(r.Available));
    }

    [Fact]
    public void Fanout_AllEnginesDeny_AllAgreeIsTrue()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.DenyRequest(), null);

        Assert.True(response.AllAgree);
        Assert.All(response.Results, r => Assert.Equal(Decision.Deny, r.Decision));
    }

    [Fact]
    public void Fanout_ExplicitSubset_ReturnsOnlyThoseEngines()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), ["cedar", "casbin"]);

        Assert.Equal(new[] { "cedar", "casbin" }, response.Results.Select(r => r.Engine).ToArray());
    }

    [Fact]
    public void Fanout_SubsetNames_AreTrimmedAndDeduped()
    {
        var response = RbacService().Fanout(
            LifecycleTestSupport.PermitLargeTxn(), ["  cedar ", "cedar", " CEDAR "]);

        var only = Assert.Single(response.Results);
        Assert.Equal("cedar", only.Engine);
    }

    [Fact]
    public void Fanout_SubsetIgnoresBlankNames()
    {
        var response = RbacService().Fanout(
            LifecycleTestSupport.PermitLargeTxn(), ["reference", "   ", ""]);

        var only = Assert.Single(response.Results);
        Assert.Equal("reference", only.Engine);
    }

    [Fact]
    public void Fanout_GenuineDisagreement_AllAgreeIsFalse()
    {
        // reference denies the cross-tenant read; a forced-permit engine disagrees. Both available.
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new FixedProvider("yes", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "forced"))),
        };
        var response = Service(LifecycleTestSupport.Factory("reference", providers))
            .Fanout(LifecycleTestSupport.DenyRequest(), null);

        Assert.False(response.AllAgree);
        Assert.All(response.Results, r => Assert.True(r.Available));
    }

    [Fact]
    public void Fanout_UnavailableDeny_IsClassifiedUnavailable()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new FixedProvider("broken",
                AccessDecision.Deny(new Reason("ProviderUnavailable", "OPA server unreachable"))),
        };
        var response = Service(LifecycleTestSupport.Factory("broken", providers))
            .Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        var only = Assert.Single(response.Results);
        Assert.False(only.Available);
        Assert.Equal("OPA server unreachable", only.UnavailableReason);
        Assert.Equal(Decision.Deny, only.Decision);
    }

    [Fact]
    public void Fanout_EngineUnavailableCode_IsAlsoClassifiedUnavailable()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new FixedProvider("broken",
                AccessDecision.Deny(new Reason("EngineUnavailable", "engine down"))),
        };
        var response = Service(LifecycleTestSupport.Factory("broken", providers))
            .Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.False(Assert.Single(response.Results).Available);
    }

    [Fact]
    public void Fanout_AllAgree_IgnoresUnavailableEngines()
    {
        // reference + cedar permit (available, agree); a broken engine fails closed and is ignored.
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            LifecycleTestSupport.ProviderByName("cedar"),
            new FixedProvider("broken",
                AccessDecision.Deny(new Reason("ProviderUnavailable", "unreachable"))),
        };
        var response = Service(LifecycleTestSupport.Factory("reference", providers))
            .Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.True(response.AllAgree);
        Assert.False(response.Results.Single(r => r.Engine == "broken").Available);
        Assert.All(
            response.Results.Where(r => r.Engine != "broken"),
            r => Assert.True(r.Available));
    }

    [Fact]
    public void Fanout_ThrowingEngine_IsUnavailable_AndDoesNotAbortFanout()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new ThrowingProvider("kaboom", "boom!"),
            LifecycleTestSupport.ProviderByName("cedar"),
        };
        var response = Service(LifecycleTestSupport.Factory("reference", providers))
            .Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        // The fan-out still produced a result for every engine, despite one throwing.
        Assert.Equal(3, response.Results.Count);

        var thrown = response.Results.Single(r => r.Engine == "kaboom");
        Assert.False(thrown.Available);
        Assert.Equal(Decision.Deny, thrown.Decision);
        Assert.Equal("ProviderUnavailable", thrown.Reasons[0].Code);
        Assert.Equal("boom!", thrown.UnavailableReason);
        Assert.NotNull(thrown.Explanation);

        // The other engines answered normally and still agree among themselves.
        Assert.True(response.AllAgree);
        Assert.All(
            response.Results.Where(r => r.Engine != "kaboom"),
            r => Assert.True(r.Available));
    }

    [Fact]
    public void Fanout_ThrowingEngine_LatencyIsNonNegative()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ThrowingProvider("kaboom", "boom!"),
        };
        var response = Service(LifecycleTestSupport.Factory("kaboom", providers))
            .Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.True(Assert.Single(response.Results).LatencyMs >= 0);
    }

    [Fact]
    public void Fanout_AvailableEngine_HasNullUnavailableReason()
    {
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.All(response.Results, r => Assert.Null(r.UnavailableReason));
    }

    [Fact]
    public void Fanout_TraceId_IsBestEffort_NullWithoutListener()
    {
        // No ambient Activity is started in these unit tests, so the best-effort trace id is null.
        var response = RbacService().Fanout(LifecycleTestSupport.PermitLargeTxn(), null);

        Assert.Null(response.TraceId);
        Assert.All(response.Results, r => Assert.Null(r.TraceId));
    }
}

// A provider that always throws — proves the fan-out survives one engine's failure and classifies it
// as unavailable with a synthesized fail-closed Deny, without depending on a live OPA/OpenFGA server.
internal sealed class ThrowingProvider(string name, string message) : IAuthorizationDecisionProvider
{
    public string Name => name;

    public AccessDecision Evaluate(AccessRequest request) => throw new InvalidOperationException(message);
}
