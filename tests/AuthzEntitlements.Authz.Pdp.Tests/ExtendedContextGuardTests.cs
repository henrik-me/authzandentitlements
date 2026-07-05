using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS45 shared fail-closed guard: the AuthorizationDecisionProviderFactory wraps every provider that
// does NOT declare ISupportsExtendedAuthorizationContext in an ExtendedContextGuardProvider, so a
// request carrying CS19/CS21 extended-authorization context — on-behalf-of (Subject.Actor),
// manager->delegate delegation (Context.Delegation), or break-glass (Context.BreakGlass) — DENIES with
// ReasonCodes.ExtendedContextUnsupported rather than being forwarded to an engine that would evaluate
// it by the human subject alone (a fail-OPEN on an engine swap). This suite proves:
//   * EVERY registered non-reference engine fails closed on each extended-context field independently,
//     via BOTH the enforced GetActiveProvider seam AND the factory-resolved GetProvider path;
//   * the capable reference engine is NOT wrapped and still applies its own CS19/CS21 permit/deny;
//   * a non-delegated request is transparent (forwarded to the inner engine unchanged);
//   * the guard never throws and never permits on the trigger, and never even consults the inner;
//   * the reason code is distinct from the "unavailable" outage codes PlaygroundFanoutService filters.
public sealed class ExtendedContextGuardTests
{
    private const string Contoso = "CONTOSO";

    // ---- Requests -----------------------------------------------------------

    private static Actor Agent(params string[] scopes) => new("agent", "agent-1", scopes);

    // A base account.read that the reference engine PERMITS for the human alone — so a fail-open would
    // be visible (an engine ignoring the extended context would permit it too).
    private static AccessRequest AccountRead(
        Actor? actor = null, DelegationGrant? delegation = null, BreakGlassGrant? breakGlass = null) =>
        new(
            new Subject("user", "user-teller1", [RoleNames.Teller], Contoso, Actor: actor),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext([ScopeNames.Read], BreakGlass: breakGlass, Delegation: delegation));

    private static AccessRequest WithActor() => AccountRead(actor: Agent(AgentScopeNames.Read));

    private static AccessRequest WithDelegation() =>
        AccountRead(delegation: new DelegationGrant(
            "grant-1", "user-teller1", "agent-1", DateTimeOffset.MaxValue, [AgentScopeNames.Read]));

    private static AccessRequest WithBreakGlass() =>
        AccountRead(breakGlass: new BreakGlassGrant(
            "bg-1", "user-teller1", "bank.account.read", DateTimeOffset.MaxValue, "audit"));

    private static AccessRequest NonDelegated() => AccountRead();

    // A transaction.create the reference PERMITS for the human but DENIES on-behalf-of when the agent
    // lacks the delegated transactions.write scope — a genuine reference OBO deny for the pass-through.
    private static AccessRequest TxnCreate(Actor? actor = null) =>
        new(
            new Subject("user", "user-teller1", [RoleNames.Teller], Contoso, Actor: actor),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("transaction", Tenant: Contoso, Amount: 250m, MakerId: "user-teller1"),
            new EvaluationContext([ScopeNames.TransactionsWrite]));

    // ---- Factory builders ---------------------------------------------------

    private static ServiceProvider BuildProvider(string active) =>
        new ServiceCollection()
            .AddLogging()
            .AddPdp(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdp:Provider"] = active })
                .Build())
            .BuildServiceProvider();

    private static AuthorizationDecisionProviderFactory HandBuilt(
        string active, params IAuthorizationDecisionProvider[] providers) =>
        new(providers, Options.Create(new PdpOptions { Provider = active }));

    // Every registered engine except the reference — enumerated from the real AddPdp graph so a new
    // non-capable adapter is covered automatically (the exit-criteria "all registered providers" bar).
    public static IEnumerable<object[]> AllRegisteredNonReferenceEngines()
    {
        using var provider = BuildProvider("reference");
        return provider.GetRequiredService<AuthorizationDecisionProviderFactory>()
            .ProviderNames
            .Where(name => !string.Equals(name, "reference", StringComparison.OrdinalIgnoreCase))
            .Select(name => new object[] { name })
            .ToList();
    }

    // ---- Every non-capable engine fails closed, both resolution paths -------

    [Theory]
    [MemberData(nameof(AllRegisteredNonReferenceEngines))]
    public void EveryRegisteredNonCapableEngine_ExtendedContext_FailsClosed_ViaGetProvider(string engine)
    {
        // Factory-resolved path (ShadowRunner / WhatIfEvaluator / PlaygroundFanoutService use GetProvider).
        using var provider = BuildProvider("reference");
        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        AssertExtendedContextUnsupported(factory.GetProvider(engine).Evaluate(WithActor()));
        AssertExtendedContextUnsupported(factory.GetProvider(engine).Evaluate(WithDelegation()));
        AssertExtendedContextUnsupported(factory.GetProvider(engine).Evaluate(WithBreakGlass()));
    }

    [Theory]
    [MemberData(nameof(AllRegisteredNonReferenceEngines))]
    public void EveryRegisteredNonCapableEngine_ExtendedContext_FailsClosed_ViaGetActiveProvider(string engine)
    {
        // Enforced path (PdpDecisionService resolves the active provider once via GetActiveProvider).
        using var provider = BuildProvider(engine);
        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        AssertExtendedContextUnsupported(factory.GetActiveProvider().Evaluate(WithActor()));
        AssertExtendedContextUnsupported(factory.GetActiveProvider().Evaluate(WithDelegation()));
        AssertExtendedContextUnsupported(factory.GetActiveProvider().Evaluate(WithBreakGlass()));
    }

    [Fact]
    public void NonReferenceEngines_AreDiscovered_SoTheGuardCoverageIsNotVacuous()
    {
        // Guards against a regression where the enumeration silently returns nothing (which would make
        // the parameterized fail-closed theories pass vacuously).
        Assert.NotEmpty(AllRegisteredNonReferenceEngines());
    }

    // ---- The reference engine is capable: NOT wrapped, its own semantics ----

    [Fact]
    public void ReferenceProvider_IsNotWrapped_ByTheFactory()
    {
        using var provider = BuildProvider("reference");
        var active = provider.GetRequiredService<AuthorizationDecisionProviderFactory>().GetActiveProvider();

        Assert.Equal("reference", active.Name);
        Assert.IsType<ReferenceDecisionProvider>(active);
    }

    [Fact]
    public void ReferenceProvider_ObopermitPassesThrough_Unchanged()
    {
        // Teller reads an in-tenant account on behalf of an agent holding the delegated read scope: the
        // reference's CS19 OBO intersection PERMITS. The guard must not intercept a capable engine.
        using var provider = BuildProvider("reference");
        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var decision = factory.GetActiveProvider().Evaluate(WithActor());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    [Fact]
    public void ReferenceProvider_OboDenyPassesThrough_Unchanged()
    {
        // Agent holds only the read scope, so a transaction.create on behalf of the teller denies
        // DelegationScopeMissing — the reference's own CS19 verdict, NOT the guard's.
        using var provider = BuildProvider("reference");
        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var decision = factory.GetActiveProvider().Evaluate(TxnCreate(actor: Agent(AgentScopeNames.Read)));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    [Fact]
    public void ReferenceProvider_BreakGlassCarryingRequest_IsEvaluatedByReference_NotGuardDenied()
    {
        // A break-glass grant carried on a request the base already permits: the reference evaluates it
        // itself (grant unused, base Permit) — proving the capable engine, not the guard, owns the
        // decision. The tell is the reason is Permit, never ExtendedContextUnsupported.
        using var provider = BuildProvider("reference");
        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var decision = factory.GetActiveProvider().Evaluate(WithBreakGlass());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        Assert.NotEqual(ReasonCodes.ExtendedContextUnsupported, decision.Reasons[0].Code);
    }

    // ---- A declared-capable provider opts OUT of the guard ------------------

    [Fact]
    public void CapableProvider_IsNotWrapped_AndHonoursExtendedContextItself()
    {
        var capable = new CapableRecordingProvider(
            "capable", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "capable permit")));
        var factory = HandBuilt("capable", new ReferenceDecisionProvider(), capable);

        var resolved = factory.GetProvider("capable");
        var decision = resolved.Evaluate(WithActor());

        Assert.IsType<CapableRecordingProvider>(resolved);
        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(1, capable.Calls);
    }

    // ---- Transparency: non-delegated requests pass through unchanged --------

    [Fact]
    public void NonCapableProvider_NonDelegatedRequest_PassesThroughToInner_Unchanged()
    {
        var inner = new RecordingProvider(
            "fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "inner permit")));
        var factory = HandBuilt("fake", new ReferenceDecisionProvider(), inner);

        var decision = factory.GetProvider("fake").Evaluate(NonDelegated());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal("inner permit", decision.Reasons[0].Message);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void Guard_PassThrough_PreservesInnerExplanation()
    {
        var explained = AccessDecision
            .Deny(new Reason(ReasonCodes.TenantMismatch, "inner deny"))
            .WithExplanation(new DecisionExplanation("fake", "tenant", [], "inner narrative"));
        var guard = new ExtendedContextGuardProvider(new RecordingProvider("fake", explained));

        var decision = guard.Evaluate(NonDelegated());

        Assert.NotNull(decision.Explanation);
        Assert.Equal("fake", decision.Explanation!.Engine);
        Assert.Equal("inner narrative", decision.Explanation.Narrative);
    }

    // ---- Never throws, never permits, never even consults the inner ---------

    [Fact]
    public void Guard_OnExtendedContext_NeverCallsInner_NeverThrows_NeverPermits()
    {
        // The inner would THROW if consulted; the guard must fail closed BEFORE forwarding, so a
        // throwing (or permitting) engine can never leak through on an extended-context request.
        var inner = new ThrowingInnerProvider("boom-engine");
        var factory = HandBuilt("reference", new ReferenceDecisionProvider(), inner);
        var guarded = factory.GetProvider("boom-engine");

        foreach (var request in new[] { WithActor(), WithDelegation(), WithBreakGlass() })
        {
            var decision = guarded.Evaluate(request); // must not throw
            AssertExtendedContextUnsupported(decision);
            Assert.NotEqual(Decision.Permit, decision.Decision);
        }

        Assert.Equal(0, inner.Calls);
    }

    // ---- Direct unit coverage of the decorator ------------------------------

    [Fact]
    public void Guard_NullInner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ExtendedContextGuardProvider(null!));
    }

    [Fact]
    public void Guard_Name_IsInnerName()
    {
        var guard = new ExtendedContextGuardProvider(
            new RecordingProvider("inner-name", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p"))));

        Assert.Equal("inner-name", guard.Name);
    }

    [Fact]
    public void Guard_Inner_ExposesTheWrappedProvider()
    {
        var inner = new RecordingProvider("fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p")));
        var guard = new ExtendedContextGuardProvider(inner);

        Assert.Same(inner, guard.Inner);
    }

    [Fact]
    public void Guard_DoesNotDeclareExtendedContextCapability()
    {
        // The guard refuses the extended context; it must not itself look capable, or the factory would
        // leave engines unguarded — the exact fail-open it prevents.
        var guard = new ExtendedContextGuardProvider(
            new RecordingProvider("fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p"))));

        Assert.IsNotAssignableFrom<ISupportsExtendedAuthorizationContext>(guard);
    }

    [Fact]
    public void Guard_DeniesActor_WithoutForwarding()
    {
        var inner = new RecordingProvider("fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p")));

        var decision = new ExtendedContextGuardProvider(inner).Evaluate(WithActor());

        AssertExtendedContextUnsupported(decision);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public void Guard_DeniesDelegation_WithoutForwarding()
    {
        var inner = new RecordingProvider("fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p")));

        var decision = new ExtendedContextGuardProvider(inner).Evaluate(WithDelegation());

        AssertExtendedContextUnsupported(decision);
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public void Guard_DeniesBreakGlass_WithoutForwarding()
    {
        var inner = new RecordingProvider("fake", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "p")));

        var decision = new ExtendedContextGuardProvider(inner).Evaluate(WithBreakGlass());

        AssertExtendedContextUnsupported(decision);
        Assert.Equal(0, inner.Calls);
    }

    // ---- The reason code is a deliberate boundary, not an outage ------------

    [Fact]
    public void ExtendedContextUnsupported_DoesNotContainUnavailable_SoItIsNotMisclassifiedAsOutage()
    {
        // PlaygroundFanoutService treats deny reasons whose Code contains "unavailable" as an engine
        // OUTAGE and excludes them from its all-agree verdict. This is a deliberate semantic boundary,
        // so the code MUST NOT contain that substring — a direct guard on Decision #5.
        Assert.DoesNotContain(
            "unavailable", ReasonCodes.ExtendedContextUnsupported, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtendedContextUnsupported_IsDistinctFromProviderUnavailable()
    {
        Assert.NotEqual("ProviderUnavailable", ReasonCodes.ExtendedContextUnsupported);
    }

    private static void AssertExtendedContextUnsupported(AccessDecision decision)
    {
        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.ExtendedContextUnsupported, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // A non-capable engine double that records whether it was consulted and returns a fixed decision.
    private sealed class RecordingProvider(string name, AccessDecision decision)
        : IAuthorizationDecisionProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request)
        {
            Calls++;
            return decision;
        }
    }

    // A non-capable engine double that THROWS if consulted — proves the guard never forwards an
    // extended-context request (a throwing engine can never become the only signal).
    private sealed class ThrowingInnerProvider(string name) : IAuthorizationDecisionProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request)
        {
            Calls++;
            throw new InvalidOperationException("inner must not be consulted on an extended-context request");
        }
    }

    // A CAPABLE engine double (declares the marker): the factory must leave it unwrapped so it applies
    // its own extended-context semantics.
    private sealed class CapableRecordingProvider(string name, AccessDecision decision)
        : IAuthorizationDecisionProvider, ISupportsExtendedAuthorizationContext
    {
        public int Calls { get; private set; }

        public string Name => name;

        public AccessDecision Evaluate(AccessRequest request)
        {
            Calls++;
            return decision;
        }
    }
}
