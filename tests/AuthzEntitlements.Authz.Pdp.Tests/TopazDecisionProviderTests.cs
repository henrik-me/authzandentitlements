using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Deterministic coverage of the Topaz adapter with NO live Topaz: a fake ITopazCheckClient forces any
// raw Rego decision object behind the provider. Because Topaz IS OPA under the hood (the SAME Rego the
// OPA adapter uses), the mapping/explanation assertions mirror the OPA adapter's — Permit + each
// obligation, every Deny reason, the CS16 Rego-native explanation, decision/reason consistency +
// unknown-code fail-closed paths, and registration + selection through the CS05 seam (AddPdp). The one
// deliberate divergence from OPA: an UNKNOWN obligation on a permit FAILS CLOSED (Cerbos-style) rather
// than being dropped. Live policy parity against the SHARED FintechScenarioCatalog is asserted separately
// (env-gated) by TopazIntegrationTests, since the bundle is CI-invisible offline.
public sealed class TopazDecisionProviderTests
{
    private const string Contoso = "CONTOSO";

    private static TopazDecisionProvider Provider(ITopazCheckClient client) =>
        new(client, NullLogger<TopazDecisionProvider>.Instance);

    private static AccessRequest TransactionCreate() =>
        new(
            new Subject("user", "maker", ["Teller"], Contoso),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("transaction", Tenant: Contoso, Amount: 15_000m, MakerId: "maker"),
            new EvaluationContext([ScopeNames.TransactionsWrite]));

    private static AccessRequest AccountRead() =>
        new(
            new Subject("user", "teller1", ["Teller"], Contoso),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext([ScopeNames.Read]));

    // --- Name ---------------------------------------------------------------

    [Fact]
    public void Name_IsTopaz()
    {
        Assert.Equal("topaz", Provider(FakeTopazCheckClient.Returning("Deny", "MissingScope")).Name);
    }

    [Fact]
    public void Evaluate_ForwardsTheRequest_ToTheSeam()
    {
        var fake = FakeTopazCheckClient.Returning("Deny", "MissingScope");
        var request = TransactionCreate();

        Provider(fake).Evaluate(request);

        Assert.Equal(1, fake.Calls);
        Assert.Same(request, fake.LastRequest);
    }

    // --- Permit + obligations -----------------------------------------------

    [Fact]
    public void Permit_WithRequireApproval_MapsObligation()
    {
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Permit", "Permit", "transaction.create.Permit", ["require_approval"]))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.RequireApproval, obligation.Id);
    }

    [Fact]
    public void Permit_WithPostImmediately_MapsObligation()
    {
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Permit", "Permit", "transaction.create.Permit", ["post_immediately"]))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.PostImmediately, obligation.Id);
    }

    [Fact]
    public void Permit_WithEmptyObligations_HasNone()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Permit", "Permit", obligations: []))
            .Evaluate(AccountRead());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithNoObligationsField_HasNone()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Permit", "Permit"))
            .Evaluate(AccountRead());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithUnknownObligationToken_FailsClosed()
    {
        // Deliberate divergence from the standalone OPA adapter (which DROPS an unknown obligation): a
        // malformed obligation token (e.g. a typo) must NOT permit while silently dropping the
        // maker-checker approval requirement — that would be a fail-OPEN on the 10,000 threshold. Topaz
        // fails closed, mirroring the Cerbos adapter.
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Permit", "Permit", "transaction.create.Permit", ["mystery"]))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithKnownAndUnknownObligation_FailsClosed()
    {
        // Even alongside a valid obligation, one unknown token fails the whole permit closed — the
        // adapter never partially maps and silently drops the rest.
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Permit", "Permit", "transaction.create.Permit", ["require_approval", "mystery"]))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithMalformedObligationsField_FailsClosed()
    {
        // The authorizer returned a Permit whose `obligations` field was PRESENT but not a JSON array
        // (the service surfaces this as ObligationsMalformed). Treating it as a no-obligation permit would
        // silently drop require_approval — a fail-OPEN on the maker-checker 10,000 threshold — so the
        // provider fails closed BEFORE mapping obligations, even though the (null) obligation list alone
        // would otherwise be a legitimate no-obligation permit.
        var decision = Provider(FakeTopazCheckClient.ReturningOutcome(
                new TopazCheckOutcome(
                    "Permit", "Permit", "transaction.create.Permit", null, ObligationsMalformed: true)))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // --- Delegation / OBO / break-glass boundary ----------------------------
    //
    // CS45 keeps the OBO / delegation / break-glass fail-closed guard in the shared
    // AuthorizationDecisionProviderFactory seam (the single authoritative guard). Because the Topaz
    // provider does not declare ISupportsExtendedAuthorizationContext, the factory wraps it in the
    // fail-closed ExtendedContextGuardProvider, which denies any request carrying Subject.Actor /
    // Context.Delegation / Context.BreakGlass BEFORE it reaches this adapter. That behaviour is proven
    // for every non-capable engine in ExtendedContextGuardTests, so it is not re-asserted here.

    // --- Deny reasons -------------------------------------------------------

    [Theory]
    [InlineData("MissingScope")]
    [InlineData("TenantMismatch")]
    [InlineData("RoleNotAuthorized")]
    [InlineData("SubjectNotMaker")]
    [InlineData("MakerEqualsChecker")]
    [InlineData("NotPending")]
    [InlineData("BranchNotInTenant")]
    [InlineData("SodConflict")]
    [InlineData("UnknownAction")]
    public void Deny_SurfacesReasonCode(string reasonCode)
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Deny", reasonCode))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(reasonCode, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Deny_UnknownAction_MapsDeterminingRule()
    {
        // The shared Rego emits reason "UnknownAction" for an action no rule matches; the adapter
        // surfaces it with the UnknownAction determining rule, mirroring the reference engine.
        var request = new AccessRequest(
            new Subject("user", "teller1", ["Teller"], Contoso),
            new ActionRequest("bank.account.delete"),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext([ScopeNames.Read]));

        var decision = Provider(FakeTopazCheckClient.Returning("Deny", "UnknownAction")).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
        Assert.Equal("topaz", decision.Explanation!.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, decision.Explanation.DeterminingRule);
    }

    // --- CS16 explanation ---------------------------------------------------

    [Fact]
    public void Permit_AttachesTopazExplanation_WithRegoRuleAndPackagePath()
    {
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Permit", "Permit", "transaction.create.Permit", ["require_approval"]))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("topaz", explanation!.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        Assert.Equal("Topaz policy decision: Permit.", explanation.Narrative);
        var regoRule = Assert.Single(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.RegoRule && r.Reference == "transaction.create.Permit");
        Assert.Equal("package authz.bank", regoRule.Detail);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.RegoRule && r.Reference == "data.authz.bank.decision");
    }

    [Fact]
    public void Deny_AttachesTopazExplanation_WithMappedDeterminingRuleAndRegoRule()
    {
        var decision = Provider(FakeTopazCheckClient.Returning(
                "Deny", "MissingScope", "transaction.create.MissingScope"))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("topaz", explanation!.Engine);
        Assert.Equal(DeterminingRules.Scope, explanation.DeterminingRule);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.RegoRule && r.Reference == "transaction.create.MissingScope");
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.RegoRule && r.Reference == "data.authz.bank.decision");
    }

    [Fact]
    public void WellFormedDecision_WithoutRuleField_DegradesToPackagePathReferenceOnly()
    {
        // An older bundle that predates the additive `rule` field: the decision still succeeds and the
        // explanation degrades to the stable package-path reference rather than failing closed.
        var decision = Provider(FakeTopazCheckClient.Returning("Permit", "Permit", obligations: []))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("topaz", explanation!.Engine);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.RegoRule, reference.Kind);
        Assert.Equal("data.authz.bank.decision", reference.Reference);
    }

    [Fact]
    public void FailClosed_AttachesEngineUnavailableExplanation()
    {
        var decision = Provider(FakeTopazCheckClient.Throwing(new InvalidOperationException("engine down")))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("topaz", explanation!.Engine);
        Assert.Equal(DeterminingRules.EngineUnavailable, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal("ProviderUnavailable", reference.Reference);
    }

    // --- Fail closed --------------------------------------------------------

    [Fact]
    public void FailClosed_WhenSeamThrows()
    {
        var decision = Provider(FakeTopazCheckClient.Throwing(new InvalidOperationException("boom")))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_OnNoDecisionBinding()
    {
        // An empty/malformed query result (the OPA bundle was undefined for the input) surfaces as the
        // None sentinel; the provider fails closed rather than fabricating a decision.
        var decision = Provider(FakeTopazCheckClient.ReturningOutcome(TopazCheckOutcome.None))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_OnMissingReason()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Permit", null))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_OnUnknownDecisionString()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Maybe", "Permit"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_OnUnknownReasonCode()
    {
        // Topaz is out-of-process; a reason outside the bounded ReasonCodes vocabulary must not reach the
        // caller (or inflate audit/metric cardinality) — fail closed.
        var decision = Provider(FakeTopazCheckClient.Returning("Deny", "TotallyMadeUp"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_WhenPermitCarriesNonPermitReason()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Permit", "TenantMismatch"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_WhenDenyCarriesPermitReason()
    {
        var decision = Provider(FakeTopazCheckClient.Returning("Deny", "Permit"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_MessageIsStable_AndDoesNotLeakExceptionDetail()
    {
        // A transport error whose message carries internal detail (URL, network cause). The caller-facing
        // Reason.Message must be the stable text, never the raw exception string — /api/authz/evaluate
        // returns AccessDecision straight to anonymous callers.
        const string secret = "https://internal-topaz.corp.local:8282 connection refused";
        var decision = Provider(FakeTopazCheckClient.Throwing(new InvalidOperationException(secret)))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.DoesNotContain(secret, decision.Reasons[0].Message);
        Assert.DoesNotContain("connection refused", decision.Reasons[0].Message);
    }

    // --- Registration + selection through the CS05 seam ---------------------

    private static ServiceProvider Build(string provider, string endpoint = "")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pdp:Provider"] = provider,
                ["Pdp:Topaz:Endpoint"] = endpoint,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddPdp_RegistersTopazProvider_AmongProviders()
    {
        using var provider = Build("reference");

        var names = provider.GetServices<IAuthorizationDecisionProvider>().Select(p => p.Name).ToList();

        Assert.Contains("topaz", names);
        Assert.Contains("reference", names);
    }

    [Fact]
    public void AddPdp_SelectsTopaz_WhenConfigured_CaseInsensitively()
    {
        using var provider = Build("TOPAZ");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        // Selection is case-insensitive: the active provider's Name is still "topaz". CS45 wraps the
        // topaz engine (which does not declare ISupportsExtendedAuthorizationContext) in the fail-closed
        // ExtendedContextGuardProvider at the factory seam, so the resolved instance is the guard — its
        // Inner is the TopazDecisionProvider.
        var active = factory.GetActiveProvider();
        Assert.Equal("topaz", active.Name);
        var guard = Assert.IsType<ExtendedContextGuardProvider>(active);
        Assert.IsType<TopazDecisionProvider>(guard.Inner);
    }

    [Fact]
    public void TopazOptions_BindFromPdpTopazSection()
    {
        using var provider = Build("topaz", endpoint: "https://localhost:8282");

        var options = provider.GetRequiredService<IOptions<TopazOptions>>().Value;

        Assert.Equal("https://localhost:8282", options.Endpoint);
        Assert.Equal("Pdp:Topaz", TopazOptions.SectionName);
    }

    [Fact]
    public void Registration_DoesNotRequireServer_BlankEndpointIsFine()
    {
        // Building the container and resolving the provider must succeed with no Topaz server and an
        // empty endpoint — the live authorizer client is built lazily only on first actual check.
        using var provider = Build("topaz", endpoint: "");

        var topaz = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "topaz");

        Assert.NotNull(topaz);
        Assert.NotNull(provider.GetRequiredService<TopazCheckService>());
    }

    [Fact]
    public void Evaluate_FailsClosed_WhenEngineUnavailable()
    {
        // With a blank endpoint the service throws when the provider issues its check; Evaluate must DENY
        // (fail closed) with a stable reason, never throw a 500 through /api/authz/evaluate.
        using var provider = Build("topaz", endpoint: "");
        var topaz = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "topaz");

        var decision = topaz.Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void UnknownProvider_StillFailsClosed_ListingTopaz()
    {
        using var provider = Build("does-not-exist");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("topaz", ex.Message);
    }

    private static void AssertProviderUnavailable(AccessDecision decision)
    {
        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }
}
