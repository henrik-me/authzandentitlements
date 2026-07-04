using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Sod;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS11 — the governance segregation-of-duties (SoD) action. The reference provider answers
// governance.access.request over a PROPOSED role set carried on subject.roles: a toxic role
// combination denies SodConflict, an independent set permits with no obligation. These assert the
// SAME rule the OPA/Rego policy (infra/opa/policy/authz.rego + governance_test.rego) encodes, so
// the reference and OPA engines return the same verdict. GovernanceSodPolicy — the pure helper the
// provider delegates to — is unit-tested directly as well.
public sealed class GovernanceSodTests
{
    private static readonly ReferenceDecisionProvider Provider = new();

    // The five incompatible (unordered) role pairs. A proposed set holding BOTH members conflicts.
    public static TheoryData<string, string> IncompatiblePairs() => new()
    {
        { RoleNames.Teller, RoleNames.BranchManager },
        { RoleNames.Teller, RoleNames.ComplianceOfficer },
        { RoleNames.Auditor, RoleNames.Teller },
        { RoleNames.Auditor, RoleNames.BranchManager },
        { RoleNames.Auditor, RoleNames.ComplianceOfficer },
    };

    // Independent role sets that must permit: two oversight roles together are allowed, single
    // roles never conflict, an empty set never conflicts, and roles outside the pairs are ignored.
    public static TheoryData<string[]> IndependentRoleSets() => new()
    {
        new[] { RoleNames.BranchManager, RoleNames.ComplianceOfficer },
        new[] { RoleNames.Teller },
        new[] { RoleNames.BranchManager },
        new[] { RoleNames.ComplianceOfficer },
        new[] { RoleNames.Auditor },
        Array.Empty<string>(),
        new[] { RoleNames.Teller, "SomeOtherRole" },
    };

    private static AccessRequest GovernanceRequest(params string[] roles) =>
        PdpRequests.For(
            PdpRequests.User("user-1", PdpRequests.Contoso, roles),
            ActionNames.GovernanceAccessRequest,
            new Resource("access-grant", Id: "quarter-end-close", Tenant: PdpRequests.Contoso));

    // --- Reference provider: conflicts deny SodConflict ---

    [Theory]
    [MemberData(nameof(IncompatiblePairs))]
    public void Governance_IncompatiblePair_DeniesSodConflict(string roleA, string roleB)
    {
        var decision = Provider.Evaluate(GovernanceRequest(roleA, roleB));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(GovernanceSodPolicy.SodConflictReasonCode, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Theory]
    [MemberData(nameof(IncompatiblePairs))]
    public void Governance_IncompatiblePair_IsOrderIndependent(string roleA, string roleB)
    {
        // The reversed set is the same unordered pair, so it must also deny.
        var decision = Provider.Evaluate(GovernanceRequest(roleB, roleA));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(GovernanceSodPolicy.SodConflictReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Governance_ConflictAmongExtraRoles_DeniesSodConflict()
    {
        // A non-conflicting extra role does not mask an incompatible pair.
        var decision = Provider.Evaluate(
            GovernanceRequest(RoleNames.ComplianceOfficer, RoleNames.Auditor, "SomeOtherRole"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(GovernanceSodPolicy.SodConflictReasonCode, decision.Reasons[0].Code);
    }

    // --- Reference provider: independent sets permit ---

    [Theory]
    [MemberData(nameof(IndependentRoleSets))]
    public void Governance_IndependentRoleSet_Permits(string[] roles)
    {
        var decision = Provider.Evaluate(GovernanceRequest(roles));

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // --- GovernanceSodPolicy: direct unit tests of the pure helper ---

    [Fact]
    public void Policy_FindConflict_IncompatiblePair_ReturnsPairInDeclaredOrder()
    {
        // Input order is reversed; the reported pair is the policy's declared (Auditor,
        // BranchManager) order, so the Deny message reads deterministically.
        var conflict = GovernanceSodPolicy.FindConflict([RoleNames.BranchManager, RoleNames.Auditor]);

        Assert.True(conflict.HasValue);
        Assert.Equal(RoleNames.Auditor, conflict.Value.First);
        Assert.Equal(RoleNames.BranchManager, conflict.Value.Second);
    }

    [Fact]
    public void Policy_HasConflict_TrueForIncompatiblePair() =>
        Assert.True(GovernanceSodPolicy.HasConflict([RoleNames.Teller, RoleNames.ComplianceOfficer]));

    [Theory]
    [MemberData(nameof(IndependentRoleSets))]
    public void Policy_FindConflict_IndependentSet_ReturnsNull(string[] roles)
    {
        // The pure helper agrees with the provider: no conflict for any independent set.
        Assert.False(GovernanceSodPolicy.FindConflict(roles).HasValue);
        Assert.False(GovernanceSodPolicy.HasConflict(roles));
    }

    // --- Stable wire vocabulary + metric normalization ---

    [Fact]
    public void GovernanceAction_HasStableWireValue() =>
        Assert.Equal("governance.access.request", ActionNames.GovernanceAccessRequest);

    [Fact]
    public void SodConflictReasonCode_HasStableWireValue() =>
        Assert.Equal("SodConflict", GovernanceSodPolicy.SodConflictReasonCode);

    [Fact]
    public void GovernanceAction_IsKnownForMetrics() =>
        // A real, handled action is a bounded known metric tag, not collapsed to "unknown".
        Assert.Equal(
            ActionNames.GovernanceAccessRequest,
            ActionNames.ForMetric(ActionNames.GovernanceAccessRequest));
}
