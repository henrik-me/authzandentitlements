using AuthzEntitlements.Entitlements.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// Pure-domain tests for the quota arithmetic. No database is needed: QuotaDecision is
// a pure value type, so the allow/deny/remaining rules are exercised directly.
public sealed class QuotaDecisionTests
{
    [Fact]
    public void Evaluate_WithinLimit_Allows_AndIncrementsUsed()
    {
        var decision = QuotaDecision.Evaluate(limit: 100, used: 10, amount: 5);

        Assert.True(decision.Allowed);
        Assert.Equal(100, decision.Limit);
        Assert.Equal(15, decision.Used);
        Assert.Equal(85, decision.Remaining);
        Assert.Equal(QuotaDecision.ReasonWithinQuota, decision.Reason);
    }

    [Fact]
    public void Evaluate_ExactlyAtLimit_Allows_RemainingZero()
    {
        var decision = QuotaDecision.Evaluate(limit: 100, used: 99, amount: 1);

        Assert.True(decision.Allowed);
        Assert.Equal(100, decision.Used);
        Assert.Equal(0, decision.Remaining);
    }

    [Fact]
    public void Evaluate_OverLimit_Denies_DoesNotIncrement()
    {
        var decision = QuotaDecision.Evaluate(limit: 100, used: 100, amount: 1);

        Assert.False(decision.Allowed);
        Assert.Equal(100, decision.Used);
        Assert.Equal(0, decision.Remaining);
        Assert.Equal(QuotaDecision.ReasonExceeded, decision.Reason);
    }

    [Fact]
    public void Evaluate_MultiAmountOverLimit_Denies_UsedUnchanged()
    {
        var decision = QuotaDecision.Evaluate(limit: 100, used: 98, amount: 5);

        Assert.False(decision.Allowed);
        Assert.Equal(98, decision.Used);
        Assert.Equal(2, decision.Remaining);
    }

    [Fact]
    public void Evaluate_MultiAmountWithinLimit_Allows()
    {
        var decision = QuotaDecision.Evaluate(limit: 1000, used: 250, amount: 250);

        Assert.True(decision.Allowed);
        Assert.Equal(500, decision.Used);
        Assert.Equal(500, decision.Remaining);
    }

    [Fact]
    public void Evaluate_Unlimited_AlwaysAllows_TracksUsage_RemainingUnlimited()
    {
        var decision = QuotaDecision.Evaluate(limit: EntitlementCatalog.Unlimited, used: 1_000_000, amount: 42);

        Assert.True(decision.Allowed);
        Assert.Equal(1_000_042, decision.Used);
        Assert.Equal(EntitlementCatalog.Unlimited, decision.Remaining);
        Assert.Equal(QuotaDecision.ReasonUnlimited, decision.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Evaluate_NonPositiveAmount_NormalizedToOne(long amount)
    {
        var decision = QuotaDecision.Evaluate(limit: 100, used: 10, amount: amount);

        Assert.True(decision.Allowed);
        Assert.Equal(11, decision.Used);
    }
}
