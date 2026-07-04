using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Casbin;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The Casbin.NET adapter must answer the shared scenario catalog identically to the reference
// provider (same Decision + primary reason code) using a genuine embedded Casbin RBAC model +
// policy for the role gate. (a) full-catalog parity; (b) each scenario individually (decision +
// reason code); (c) the config-selectable Name; (d) role-eligibility unit tests that exercise
// the Casbin enforcer directly.
public sealed class CasbinDecisionProviderTests
{
    public static IEnumerable<object[]> ScenarioIds() =>
        FintechScenarioCatalog.Scenarios.Select(scenario => new object[] { scenario.Id });

    [Fact]
    public void CasbinProvider_AnswersFullCatalog()
    {
        var report = ScenarioCatalogRunner.Run(
            FintechScenarioCatalog.Scenarios, new CasbinDecisionProvider());

        var failing = report.Results
            .Where(result => !result.Passed)
            .Select(result => result.Scenario.Id)
            .ToList();

        Assert.True(report.AllPassed, $"Failing scenarios: {string.Join(", ", failing)}");
        Assert.Equal(report.Total, report.Passed);
        Assert.Equal(FintechScenarioCatalog.Scenarios.Count, report.Total);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void CasbinProvider_MatchesScenarioExpectation(string scenarioId)
    {
        var scenario = FintechScenarioCatalog.Scenarios.Single(s => s.Id == scenarioId);
        var provider = new CasbinDecisionProvider();

        var decision = provider.Evaluate(scenario.Request);

        Assert.Equal(scenario.Expected, decision.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Name_IsCasbin()
    {
        Assert.Equal("casbin", new CasbinDecisionProvider().Name);
    }

    [Theory]
    [InlineData(RoleNames.BranchManager, ActionNames.AccountCreate, true)]
    [InlineData(RoleNames.Teller, ActionNames.AccountCreate, false)]
    [InlineData(RoleNames.ComplianceOfficer, ActionNames.AccountCreate, false)]
    [InlineData(RoleNames.Auditor, ActionNames.AccountCreate, false)]
    [InlineData(RoleNames.Teller, ActionNames.TransactionCreate, true)]
    [InlineData(RoleNames.BranchManager, ActionNames.TransactionCreate, true)]
    [InlineData(RoleNames.ComplianceOfficer, ActionNames.TransactionCreate, true)]
    [InlineData(RoleNames.Auditor, ActionNames.TransactionCreate, false)]
    [InlineData(RoleNames.BranchManager, ActionNames.TransactionApprove, true)]
    [InlineData(RoleNames.ComplianceOfficer, ActionNames.TransactionApprove, true)]
    [InlineData(RoleNames.Teller, ActionNames.TransactionApprove, false)]
    [InlineData(RoleNames.Auditor, ActionNames.TransactionApprove, false)]
    [InlineData(RoleNames.BranchManager, ActionNames.TransactionReject, true)]
    [InlineData(RoleNames.Teller, ActionNames.TransactionReject, false)]
    public void RoleEligibility_GatesByRole(string role, string action, bool eligible)
    {
        IEngineRoleAuthorizer authorizer = new CasbinDecisionProvider();

        Assert.Equal(eligible, authorizer.IsRoleAuthorized(action, [role]));
    }

    [Fact]
    public void ManagerCreatesAccount_Permits()
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    [Fact]
    public void TellerCreatesAccount_DeniesRoleNotAuthorized()
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
    }

    [Fact]
    public void TellerApproves_DeniesRoleNotAuthorized()
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionApprove,
            new Resource("transaction", Tenant: PdpRequests.Contoso, MakerId: "user-manager1", Status: "Pending"),
            ScopeNames.ApprovalsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
    }

    // The catalog runner only checks Decision + primary reason code, not obligations; assert the
    // adapter carries the exact maker-checker threshold obligation FintechRuleEvaluator attaches.
    [Theory]
    [InlineData(250, ObligationIds.PostImmediately)]
    [InlineData(9_999, ObligationIds.PostImmediately)]
    [InlineData(10_000, ObligationIds.RequireApproval)]
    [InlineData(15_000, ObligationIds.RequireApproval)]
    public void TransactionCreate_CarriesThresholdObligation(int amount, string expectedObligation)
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: amount, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(expectedObligation, Assert.Single(decision.Obligations).Id);
    }

    // --- CS16 explainability: every decision carries a "casbin"-engine explanation ---

    [Fact]
    public void Permit_CarriesCasbinExplanation_WithMatchedPolicyLine()
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("casbin", explanation.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        // The engine-native matched Casbin policy line is surfaced alongside the normalized rule.
        var casbinRule = Assert.Single(
            explanation.PolicyReferences, r => r.Kind == PolicyReferenceKinds.CasbinRule);
        Assert.Equal($"p, {RoleNames.BranchManager}, {ActionNames.AccountCreate}", casbinRule.Reference);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.Rule && r.Reference == DeterminingRules.AllRulesSatisfied);
    }

    [Fact]
    public void RoleDeny_CarriesCasbinExplanation_WithRequiredPolicyLines()
    {
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("casbin", explanation.Engine);
        Assert.Equal(DeterminingRules.Role, explanation.DeterminingRule);
        // No subject role matched, so the reference names the required policy line(s).
        var casbinRule = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.CasbinRule, casbinRule.Kind);
        Assert.Equal($"p, {RoleNames.BranchManager}, {ActionNames.AccountCreate}", casbinRule.Reference);
    }

    [Fact]
    public void TenantDeny_CarriesCasbinExplanation_WithRoleAndTenantRules()
    {
        // Role passes (BranchManager) but tenants differ: the explanation carries both the matched
        // engine-native Casbin rule and the normalized tenant rule that determined the deny.
        var provider = new CasbinDecisionProvider();
        var request = PdpRequests.For(
            PdpRequests.User("user-manager1", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate,
            new Resource("account", Tenant: PdpRequests.Fabrikam),
            ScopeNames.Read);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, decision.Reasons[0].Code);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("casbin", explanation.Engine);
        Assert.Equal(DeterminingRules.Tenant, explanation.DeterminingRule);
        Assert.Contains(explanation.PolicyReferences, r => r.Kind == PolicyReferenceKinds.CasbinRule);
        Assert.Contains(
            explanation.PolicyReferences,
            r => r.Kind == PolicyReferenceKinds.Rule && r.Reference == DeterminingRules.Tenant);
    }

    [Fact]
    public void DescribeRoleRule_NamesMatchedRole()
    {
        var provider = new CasbinDecisionProvider();

        var reference = ((IEngineRoleAuthorizer)provider).DescribeRoleRule(
            ActionNames.TransactionCreate, [RoleNames.Teller]);

        Assert.Equal(PolicyReferenceKinds.CasbinRule, reference.Kind);
        Assert.Equal($"p, {RoleNames.Teller}, {ActionNames.TransactionCreate}", reference.Reference);
    }

    [Fact]
    public void EngineName_IsCasbin()
    {
        Assert.Equal("casbin", ((IEngineRoleAuthorizer)new CasbinDecisionProvider()).EngineName);
    }
}
