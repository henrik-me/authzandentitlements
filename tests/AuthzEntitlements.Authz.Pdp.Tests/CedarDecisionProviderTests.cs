using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cedar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The Cedar adapter must answer the shared scenario catalog identically to the reference provider
// (same Decision + primary reason code) using a GENUINE in-process Cedar engine that owns the full
// fintech decision natively — the head-to-head with OPA. (a) full-catalog parity; (b) each scenario
// individually; (c) the config-selectable Name; (d) the maker-checker threshold obligation on
// transaction.create; (e) combined-failure reason ORDERING (LRN-021: first-failing reason wins);
// (f) fail-closed on a degenerate request; (g) runtime selection via AddPdp.
public sealed class CedarDecisionProviderTests
{
    public static IEnumerable<object[]> ScenarioIds() =>
        FintechScenarioCatalog.Scenarios.Select(scenario => new object[] { scenario.Id });

    [Fact]
    public void CedarProvider_AnswersFullCatalog()
    {
        var report = ScenarioCatalogRunner.Run(
            FintechScenarioCatalog.Scenarios, new CedarDecisionProvider());

        var failing = report.Results
            .Where(result => !result.Passed)
            .Select(result => $"{result.Scenario.Id} (expected {result.Scenario.Expected}/" +
                $"{result.Scenario.ExpectedReasonCode}, got {result.Actual.Decision}/" +
                $"{result.Actual.Reasons[0].Code})")
            .ToList();

        Assert.True(report.AllPassed, $"Failing scenarios: {string.Join(" | ", failing)}");
        Assert.Equal(report.Total, report.Passed);
        Assert.Equal(FintechScenarioCatalog.Scenarios.Count, report.Total);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void CedarProvider_MatchesScenarioExpectation(string scenarioId)
    {
        var scenario = FintechScenarioCatalog.Scenarios.Single(s => s.Id == scenarioId);
        var provider = new CedarDecisionProvider();

        var decision = provider.Evaluate(scenario.Request);

        Assert.Equal(scenario.Expected, decision.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Name_IsCedar()
    {
        Assert.Equal("cedar", new CedarDecisionProvider().Name);
    }

    // The catalog runner only checks Decision + primary reason code, not obligations; assert Cedar
    // carries the exact maker-checker threshold obligation (boundary at exactly 10,000).
    [Theory]
    [InlineData(250, ObligationIds.PostImmediately)]
    [InlineData(9_999, ObligationIds.PostImmediately)]
    [InlineData(10_000, ObligationIds.RequireApproval)]
    [InlineData(15_000, ObligationIds.RequireApproval)]
    public void TransactionCreate_CarriesThresholdObligation(int amount, string expectedObligation)
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: amount, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(expectedObligation, Assert.Single(decision.Obligations).Id);
    }

    // LRN-021: when several rules fail at once, the primary reason must be the reference's
    // FIRST-failing one, not an arbitrary member of the determining forbid set.
    [Fact]
    public void Read_MissingScopeAndTenantMismatch_DeniesMissingScopeFirst()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Fabrikam));
        // No scopes AND cross-tenant: MissingScope precedes TenantMismatch.

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Fact]
    public void Approve_AlreadyDecidedSelfTransaction_DeniesNotPendingBeforeSoD()
    {
        var provider = new CedarDecisionProvider();
        // Manager approves an ALREADY-APPROVED transaction they themselves made: both NotPending and
        // MakerEqualsChecker are determining, but Pending is checked first -> NotPending wins.
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            new Resource("transaction", Tenant: PdpRequests.Contoso, MakerId: "user-manager1", Status: "Approved"),
            ScopeNames.ApprovalsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.NotPending, decision.Reasons[0].Code);
    }

    [Fact]
    public void TransactionCreate_MissingScopeAndNotMakerAndTenantMismatch_DeniesMissingScopeFirst()
    {
        var provider = new CedarDecisionProvider();
        // No scope, wrong maker, cross-tenant all at once: MissingScope is first-failing.
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Fabrikam, Amount: 250m, MakerId: "user-manager1"));

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Fact]
    public void AccountCreate_NotManagerAndCrossTenant_DeniesRoleNotAuthorizedFirst()
    {
        var provider = new CedarDecisionProvider();
        // Teller (not BranchManager) in the wrong tenant: role is checked before tenant.
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Fabrikam),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
    }

    // Fail-closed: a degenerate request (an out-of-range amount that overflows the Cedar Long
    // attribute) must Deny with the provider-local ProviderUnavailable reason, never throw, and
    // never fall through to a permit.
    [Fact]
    public void DegenerateRequest_FailsClosed_NeverThrowsNeverPermits()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: decimal.MaxValue, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void UnknownAction_FailsClosed_DeniesUnknownAction()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            "bank.account.delete",
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
    }

    // Fail-closed tenant isolation / reference parity (R1 blocker): the reference denies
    // TenantMismatch unless BOTH tenants are non-whitespace AND exactly equal, so a missing OR
    // whitespace-only tenant on EITHER side is a mismatch. These lock Cedar to that fail-closed
    // semantics (a blank-vs-blank pair must DENY, never fall through to a permit) and assert
    // equivalence to the reference oracle (Decision + primary reason code). None of the 22 catalog
    // scenarios use blank tenants, so this behaviour is only covered here.

    // read: has bank.read scope, both tenants null -> the historical fail-open case ("" == "" no
    // longer permits). Deny/TenantMismatch, matching the reference.
    [Fact]
    public void Read_BothTenantsNull_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", tenant: null, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: null),
            ScopeNames.Read);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    [Fact]
    public void Read_SubjectTenantNull_ResourceTenantSet_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", tenant: null, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    [Fact]
    public void Read_SubjectTenantSet_ResourceTenantNull_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: null),
            ScopeNames.Read);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    // A whitespace-only tenant on one side must be treated as absent (IsNullOrWhiteSpace parity),
    // not as a literal value that could coincidentally match the other side.
    [Fact]
    public void Read_WhitespaceTenant_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", tenant: "   ", RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    // account.create: BranchManager role satisfied, both tenants blank -> role passes, tenant forbid
    // fires. Locks the account.create.TenantMismatch forbid.
    [Fact]
    public void AccountCreate_BothTenantsBlank_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", tenant: null, RoleNames.BranchManager),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: "   "),
            ScopeNames.Read);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    // transaction.create: scope, role, and subject-is-maker all satisfied, both tenants blank ->
    // tenant forbid fires (before the permit). Locks the transaction.create.TenantMismatch forbid.
    [Fact]
    public void TransactionCreate_BothTenantsBlank_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", tenant: null, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: null, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    // approve/reject: scope and role satisfied, both tenants blank -> tenant forbid fires (tenant is
    // checked before pending/SoD). Locks the approval.TenantMismatch forbid.
    [Fact]
    public void Approval_BothTenantsBlank_DeniesTenantMismatchLikeReference()
    {
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", tenant: null, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            new Resource("transaction", Tenant: null, MakerId: "user-teller1", Status: "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertDeniesTenantMismatchLikeReference(request);
    }

    // The reference provider is the oracle: assert Cedar denies TenantMismatch AND matches the
    // reference's Decision + primary reason code exactly for the given blank-tenant request.
    private static void AssertDeniesTenantMismatchLikeReference(AccessRequest request)
    {
        var cedar = new CedarDecisionProvider().Evaluate(request);
        var reference = new ReferenceDecisionProvider().Evaluate(request);

        Assert.Equal(Decision.Deny, cedar.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, cedar.Reasons[0].Code);
        Assert.Equal(reference.Decision, cedar.Decision);
        Assert.Equal(reference.Reasons[0].Code, cedar.Reasons[0].Code);
    }

    // Cedar is in-process, so (unlike OPA) it participates in the real AddPdp DI container and is
    // selectable at runtime by "Pdp:Provider" among all registered engines.
    [Fact]
    public void AddPdp_ResolvesCedarAdapter_AtRuntime()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Pdp:Provider"] = "cedar" })
            .Build();

        var services = new ServiceCollection();
        services.AddPdp(configuration);
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();
        var active = factory.GetActiveProvider();

        // CS45: the cedar engine does not declare ISupportsExtendedAuthorizationContext, so the factory
        // wraps it in the fail-closed ExtendedContextGuardProvider. Selection is unchanged (Name still
        // "cedar"); the resolved instance is the guard whose Inner is the concrete cedar adapter.
        Assert.Equal("cedar", active.Name);
        var guard = Assert.IsType<ExtendedContextGuardProvider>(active);
        Assert.IsType<CedarDecisionProvider>(guard.Inner);
    }

    // CS16 explainability: beyond Decision + reason code, the adapter surfaces a normalized
    // DecisionExplanation carrying the DETERMINING Cedar policy id(s) as a cedar-policy trace. These
    // are ADDITIVE — the decision/reason/obligation parity above is unchanged.

    // A representative single-forbid deny carries the cedar engine label, the normalized rule for its
    // reason, and exactly the determining forbid id as a cedar-policy reference (detail = reason code).
    [Fact]
    public void Deny_SurfacesDeterminingCedarPolicyIdExplanation()
    {
        var provider = new CedarDecisionProvider();
        // scope, role and subject-is-maker all satisfied; only the tenant forbid fires.
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Fabrikam, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, decision.Reasons[0].Code);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("cedar", explanation.Engine);
        Assert.Equal(DeterminingRules.Tenant, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.CedarPolicy, reference.Kind);
        Assert.Equal("transaction.create.TenantMismatch", reference.Reference);
        Assert.Equal(ReasonCodes.TenantMismatch, reference.Detail);
    }

    // A combined-failure deny lists EVERY determining forbid id, first-failing (lowest precedence)
    // first, so the primary determining forbid heads the trace and mirrors the primary reason code.
    [Fact]
    public void Deny_CombinedFailure_ListsDeterminingForbidsFirstFailingFirst()
    {
        var provider = new CedarDecisionProvider();
        // No scope, wrong maker, cross-tenant: MissingScope -> SubjectNotMaker -> TenantMismatch.
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Fabrikam, Amount: 250m, MakerId: "user-manager1"));

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("cedar", explanation.Engine);
        Assert.Equal(DeterminingRules.Scope, explanation.DeterminingRule);
        Assert.Equal(
            new[]
            {
                "transaction.create.MissingScope",
                "transaction.create.SubjectNotMaker",
                "transaction.create.TenantMismatch",
            },
            explanation.PolicyReferences.Select(r => r.Reference).ToArray());
        Assert.All(explanation.PolicyReferences, r => Assert.Equal(PolicyReferenceKinds.CedarPolicy, r.Kind));
    }

    // A permit carries the matched permit policy id as a cedar-policy reference and the
    // all-rules-satisfied rule.
    [Fact]
    public void Permit_SurfacesMatchedPermitPolicyIdExplanation()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("cedar", explanation.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        Assert.All(explanation.PolicyReferences, r => Assert.Equal(PolicyReferenceKinds.CedarPolicy, r.Kind));
        Assert.Contains(explanation.PolicyReferences, r => r.Reference == "transaction.create.Permit");
    }

    // An unknown action carries the unknown-action rule and a reason-code reference (pre-Cedar early
    // return, so there is no cedar-policy id to name).
    [Fact]
    public void UnknownAction_SurfacesUnknownActionExplanation()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            "bank.account.delete",
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("cedar", explanation.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(ReasonCodes.UnknownAction, reference.Reference);
    }

    // A fail-closed decision (degenerate overflow request) carries the engine-unavailable rule and a
    // reason-code reference naming the provider-local ProviderUnavailable code — never a leaked detail.
    [Fact]
    public void FailClosed_SurfacesEngineUnavailableExplanation()
    {
        var provider = new CedarDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: decimal.MaxValue, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("cedar", explanation.Engine);
        Assert.Equal(DeterminingRules.EngineUnavailable, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal("ProviderUnavailable", reference.Reference);
    }
}
