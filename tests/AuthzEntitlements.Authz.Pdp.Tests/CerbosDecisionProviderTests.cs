using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Deterministic coverage of the Cerbos adapter with NO live Cerbos: a fake ICerbosCheckClient forces
// any (allowed, outputToken) outcome behind the provider. Covers the full-decision reason/obligation
// mapping (Permit + each obligation, every Deny reason), the CS16 explanation, decision/reason
// consistency + unknown-code fail-closed paths, the output-less UnknownAction / fail-closed paths, and
// registration + selection through the CS05 seam (AddPdp). The Cerbos YAML policy's scenario parity is
// asserted separately (live) by CerbosIntegrationTests, since the policy is CI-invisible offline.
public sealed class CerbosDecisionProviderTests
{
    private const string Contoso = "CONTOSO";

    private static CerbosDecisionProvider Provider(ICerbosCheckClient client) =>
        new(client, NullLogger<CerbosDecisionProvider>.Instance);

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
    public void Name_IsCerbos()
    {
        Assert.Equal("cerbos", Provider(FakeCerbosCheckClient.Returning(false, "MissingScope")).Name);
    }

    [Fact]
    public void Evaluate_ForwardsTheRequest_ToTheSeam()
    {
        var fake = FakeCerbosCheckClient.Returning(false, "MissingScope");
        var request = TransactionCreate();

        Provider(fake).Evaluate(request);

        Assert.Equal(1, fake.Calls);
        Assert.Same(request, fake.LastRequest);
    }

    // --- Permit + obligations -----------------------------------------------

    [Fact]
    public void Permit_WithRequireApproval_MapsObligation()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "Permit:require_approval"))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.RequireApproval, obligation.Id);
    }

    [Fact]
    public void Permit_WithPostImmediately_MapsObligation()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "Permit:post_immediately"))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.PostImmediately, obligation.Id);
    }

    [Fact]
    public void Permit_WithNoObligationToken_HasNone()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "Permit"))
            .Evaluate(AccountRead());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithUnknownObligationToken_FailsClosed()
    {
        // A malformed obligation suffix (e.g. a typo) must NOT permit while silently dropping the
        // maker-checker approval requirement — that would be a fail-OPEN on the 10,000 threshold. It
        // fails closed instead.
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "Permit:mystery"))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // --- Delegation / OBO / break-glass boundary (fail closed, never forwarded) ---

    [Fact]
    public void Evaluate_WithActor_FailsClosed_WithoutForwarding()
    {
        // On-behalf-of (Subject.Actor) is a CS19/CS21 constraint the Cerbos policy does not encode.
        // Forwarding could let Cerbos permit where the reference denies a missing delegated scope, so
        // the adapter fails closed BEFORE forwarding — the seam is never called.
        var fake = FakeCerbosCheckClient.Returning(true, "Permit");
        var request = new AccessRequest(
            new Subject("user", "manager1", ["Manager"], Contoso, Actor: new Actor("agent", "bot-1", ["bank.read"])),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext([ScopeNames.Read]));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void Evaluate_WithDelegationContext_FailsClosed_WithoutForwarding()
    {
        var fake = FakeCerbosCheckClient.Returning(true, "Permit");
        var request = new AccessRequest(
            new Subject("user", "delegate1", ["Teller"], Contoso),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext(
                [ScopeNames.Read],
                Delegation: new DelegationGrant("g1", "mgr1", "delegate1", DateTimeOffset.MaxValue, ["bank.read"])));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void Evaluate_WithBreakGlassContext_FailsClosed_WithoutForwarding()
    {
        var fake = FakeCerbosCheckClient.Returning(true, "Permit");
        var request = new AccessRequest(
            new Subject("user", "teller1", ["Teller"], Contoso),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext(
                [ScopeNames.Read],
                BreakGlass: new BreakGlassGrant("bg1", "teller1", "bank.account.read", DateTimeOffset.MaxValue, "audit")));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void Deny_KnownActionWithNoOutput_FailsClosed_NotUnknownAction()
    {
        // A KNOWN action that Cerbos denied with NO output token means a malformed policy/server
        // response — it must fail closed (ProviderUnavailable), NOT be misreported as UnknownAction.
        var decision = Provider(FakeCerbosCheckClient.Returning(false, null))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
    }

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
    public void Deny_SurfacesReasonCode(string reasonCode)
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(false, reasonCode))
            .Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(reasonCode, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // --- Unknown action (Cerbos default deny with no output) ----------------

    [Fact]
    public void Deny_WithNoOutputToken_MapsToUnknownAction()
    {
        // Cerbos denies-by-default with no output for an action no rule matches; the adapter maps that
        // to the fail-closed UnknownAction reason, mirroring the reference engine's unknown-action path.
        var request = new AccessRequest(
            new Subject("user", "teller1", ["Teller"], Contoso),
            new ActionRequest("bank.account.delete"),
            new Resource("account", Tenant: Contoso),
            new EvaluationContext([ScopeNames.Read]));

        var decision = Provider(FakeCerbosCheckClient.Returning(false, null)).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
        Assert.Equal("cerbos", decision.Explanation!.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, decision.Explanation.DeterminingRule);
    }

    // --- CS16 explanation ---------------------------------------------------

    [Fact]
    public void Permit_AttachesCerbosExplanation_WithRuleAndPolicyId()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "Permit:require_approval"))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("cerbos", explanation!.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        Assert.Equal("Cerbos policy decision: Permit.", explanation.Narrative);
        var rule = Assert.Single(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.Rule && r.Reference == "transaction.create.Permit");
        Assert.Equal("Cerbos resource policy 'bank' (version default)", rule.Detail);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.Rule && r.Reference == "resource.bank.vdefault");
    }

    [Fact]
    public void Deny_AttachesCerbosExplanation_WithMappedDeterminingRule()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(false, "MissingScope"))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("cerbos", explanation!.Engine);
        Assert.Equal(DeterminingRules.Scope, explanation.DeterminingRule);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.Rule && r.Reference == "transaction.create.MissingScope");
    }

    [Fact]
    public void FailClosed_AttachesEngineUnavailableExplanation()
    {
        var decision = Provider(FakeCerbosCheckClient.Throwing(new InvalidOperationException("engine down")))
            .Evaluate(TransactionCreate());

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("cerbos", explanation!.Engine);
        Assert.Equal(DeterminingRules.EngineUnavailable, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal("ProviderUnavailable", reference.Reference);
    }

    // --- Fail closed --------------------------------------------------------

    [Fact]
    public void FailClosed_WhenSeamThrows()
    {
        var decision = Provider(FakeCerbosCheckClient.Throwing(new InvalidOperationException("boom")))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_OnUnknownReasonCode()
    {
        // Cerbos is out-of-process; a token outside the bounded ReasonCodes vocabulary must not reach
        // the caller (or inflate audit/metric cardinality) — fail closed.
        var decision = Provider(FakeCerbosCheckClient.Returning(false, "TotallyMadeUp"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_WhenPermitCarriesNonPermitReason()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(true, "TenantMismatch"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_WhenDenyCarriesPermitReason()
    {
        var decision = Provider(FakeCerbosCheckClient.Returning(false, "Permit"))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_WhenPermitHasNoOutputToken()
    {
        // A permit with no policy output means a misbehaving policy: fail closed rather than surface an
        // unexplained permit.
        var decision = Provider(FakeCerbosCheckClient.Returning(true, null))
            .Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void FailClosed_MessageIsStable_AndDoesNotLeakExceptionDetail()
    {
        // A transport error whose message carries internal detail. The caller-facing Reason.Message
        // must be the stable text, never the raw exception string — /api/authz/evaluate returns
        // AccessDecision straight to anonymous callers.
        const string secret = "http://internal-cerbos.corp.local:3593 connection refused";
        var decision = Provider(FakeCerbosCheckClient.Throwing(new InvalidOperationException(secret)))
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
                ["Pdp:Cerbos:Endpoint"] = endpoint,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddPdp_RegistersCerbosProvider_AmongProviders()
    {
        using var provider = Build("reference");

        var names = provider.GetServices<IAuthorizationDecisionProvider>().Select(p => p.Name).ToList();

        Assert.Contains("cerbos", names);
        Assert.Contains("reference", names);
    }

    [Fact]
    public void AddPdp_SelectsCerbos_WhenConfigured_CaseInsensitively()
    {
        using var provider = Build("Cerbos");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        Assert.Equal("cerbos", factory.GetActiveProvider().Name);
        Assert.IsType<CerbosDecisionProvider>(factory.GetActiveProvider());
    }

    [Fact]
    public void CerbosOptions_BindFromPdpCerbosSection()
    {
        using var provider = Build("cerbos", endpoint: "http://localhost:3593");

        var options = provider.GetRequiredService<IOptions<CerbosOptions>>().Value;

        Assert.Equal("http://localhost:3593", options.Endpoint);
        Assert.Equal("Pdp:Cerbos", CerbosOptions.SectionName);
    }

    [Fact]
    public void Registration_DoesNotRequireServer_BlankEndpointIsFine()
    {
        // Building the container and resolving the provider must succeed with no Cerbos server and an
        // empty endpoint — the live gRPC client is built lazily only on first actual check.
        using var provider = Build("cerbos", endpoint: "");

        var cerbos = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "cerbos");

        Assert.NotNull(cerbos);
        Assert.NotNull(provider.GetRequiredService<CerbosCheckService>());
    }

    [Fact]
    public void Evaluate_FailsClosed_WhenEngineUnavailable()
    {
        // With a blank endpoint the service throws when the provider issues its check; Evaluate must
        // DENY (fail closed) with a stable reason, never throw a 500 through /api/authz/evaluate.
        using var provider = Build("cerbos", endpoint: "");
        var cerbos = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "cerbos");

        var decision = cerbos.Evaluate(TransactionCreate());

        AssertProviderUnavailable(decision);
    }

    [Fact]
    public void UnknownProvider_StillFailsClosed_ListingCerbos()
    {
        using var provider = Build("does-not-exist");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("cerbos", ex.Message);
    }

    private static void AssertProviderUnavailable(AccessDecision decision)
    {
        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }
}
