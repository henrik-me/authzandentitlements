using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Maker-checker, JIT-duration, and expiry arithmetic are the security-critical decisions of
// the approval workflow (item e + duration/expiry).
public sealed class GovernanceRulesTests
{
    [Fact]
    public void CheckerDiffersFromRequester_SamePrincipal_IsFalse()
    {
        // The requester cannot approve their own elevation.
        Assert.False(GovernanceRules.CheckerDiffersFromRequester("user-teller1", "user-teller1"));
    }

    [Fact]
    public void CheckerDiffersFromRequester_DifferentPrincipal_IsTrue()
    {
        Assert.True(GovernanceRules.CheckerDiffersFromRequester("user-teller1", "user-manager1"));
    }

    [Fact]
    public void CheckerDiffersFromRequester_IsOrdinalCaseSensitive()
    {
        // A different casing is a different identity; do not treat it as the same requester.
        Assert.True(GovernanceRules.CheckerDiffersFromRequester("user-teller1", "USER-TELLER1"));
    }

    [Theory]
    [InlineData("BranchManager")]
    [InlineData("ComplianceOfficer")]
    public void IsCheckerEligible_OversightRole_IsTrue(string role)
    {
        // Only an oversight role may act as the checker on an elevation.
        Assert.True(GovernanceRules.IsCheckerEligible([role]));
    }

    [Theory]
    [InlineData("Teller")]
    [InlineData("Auditor")]
    public void IsCheckerEligible_NonOversightRole_IsFalse(string role)
    {
        // A maker (Teller) or the independent Auditor may not sign off on an elevation.
        Assert.False(GovernanceRules.IsCheckerEligible([role]));
    }

    [Fact]
    public void IsCheckerEligible_EmptyRoleSet_IsFalse()
    {
        // An unknown principal (no baseline roles) is never checker-eligible.
        Assert.False(GovernanceRules.IsCheckerEligible([]));
    }

    [Fact]
    public void IsCheckerEligible_MixedRoles_IsTrueWhenAnyEligible()
    {
        // Holding at least one oversight role is sufficient.
        Assert.True(GovernanceRules.IsCheckerEligible(["Teller", "ComplianceOfficer"]));
    }

    [Fact]
    public void EffectiveDurationMinutes_NullRequested_UsesPackageDefault()
    {
        Assert.Equal(480, GovernanceRules.EffectiveDurationMinutes(requestedMinutes: null, packageDefaultMinutes: 480));
    }

    [Fact]
    public void EffectiveDurationMinutes_PositiveRequested_UsesRequested()
    {
        Assert.Equal(60, GovernanceRules.EffectiveDurationMinutes(requestedMinutes: 60, packageDefaultMinutes: 480));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void EffectiveDurationMinutes_NonPositiveRequested_FallsBackToDefault(int requested)
    {
        // A zero/negative override would issue an already-expired grant; fall back instead.
        Assert.Equal(240, GovernanceRules.EffectiveDurationMinutes(requested, packageDefaultMinutes: 240));
    }

    [Fact]
    public void ComputeExpiry_AddsMinutesToGrantInstant()
    {
        var grantedAt = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(grantedAt.AddMinutes(480), GovernanceRules.ComputeExpiry(grantedAt, 480));
    }
}
