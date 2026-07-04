using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Sod;
using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// The SoD evidence proves the SYSTEM (GovernanceSodPolicy + ReferenceDecisionProvider) detects and
// denies every toxic role combination and permits every independent one.
public sealed class SodEvidenceReporterTests
{
    public static TheoryData<string, string> IncompatiblePairs() => new()
    {
        { RoleNames.Teller, RoleNames.BranchManager },
        { RoleNames.Teller, RoleNames.ComplianceOfficer },
        { RoleNames.Auditor, RoleNames.Teller },
        { RoleNames.Auditor, RoleNames.BranchManager },
        { RoleNames.Auditor, RoleNames.ComplianceOfficer },
    };

    [Theory]
    [MemberData(nameof(IncompatiblePairs))]
    public void EveryIncompatiblePair_IsDetected(string first, string second)
    {
        Assert.True(GovernanceSodPolicy.HasConflict([first, second]));
    }

    [Theory]
    [MemberData(nameof(IncompatiblePairs))]
    public void EveryIncompatiblePair_DeniesWithSodConflict(string first, string second)
    {
        var decision = Evaluate([first, second]);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.SodConflict, decision.Reasons[0].Code);
    }

    [Fact]
    public void SupersetContainingAPair_IsDetected()
    {
        Assert.True(GovernanceSodPolicy.HasConflict(
            [RoleNames.Teller, RoleNames.BranchManager, RoleNames.ComplianceOfficer]));
    }

    [Fact]
    public void CleanOversightPair_IsConflictFreeAndPermitted()
    {
        var roles = new[] { RoleNames.BranchManager, RoleNames.ComplianceOfficer };

        Assert.False(GovernanceSodPolicy.HasConflict(roles));
        Assert.Equal(Decision.Permit, Evaluate(roles).Decision);
    }

    [Fact]
    public void SingleRole_IsConflictFree()
    {
        Assert.False(GovernanceSodPolicy.HasConflict([RoleNames.Teller]));
        Assert.Equal(Decision.Permit, Evaluate([RoleNames.Teller]).Decision);
    }

    [Fact]
    public void EmptySet_IsConflictFree()
    {
        Assert.False(GovernanceSodPolicy.HasConflict([]));
        Assert.Equal(Decision.Permit, Evaluate([]).Decision);
    }

    [Fact]
    public void Build_AllToxicDenied_AllCleanPermitted_AndEveryCasePasses()
    {
        var section = SodEvidenceReporter.Build();

        Assert.Equal(5, section.IncompatiblePairCount);
        Assert.True(section.AllToxicCombinationsDenied);
        Assert.True(section.AllCleanSetsPermitted);
        Assert.All(section.Cases, c => Assert.True(c.Passed, c.Scenario));
    }

    [Fact]
    public void Build_MapsMakerCheckerControl()
    {
        var section = SodEvidenceReporter.Build();

        Assert.Contains(section.MappedControls, c => c.ControlId == "SOD-MAKER-CHECKER");
        Assert.Contains(section.MappedControls, c => c.ControlId == "SOD-ROLE-INCOMPATIBILITY");
    }

    private static AccessDecision Evaluate(IReadOnlyList<string> roles) =>
        new ReferenceDecisionProvider().Evaluate(new AccessRequest(
            new Subject("user", "probe", roles),
            new ActionRequest(ActionNames.GovernanceAccessRequest),
            new Resource("governance"),
            new EvaluationContext([])));
}
