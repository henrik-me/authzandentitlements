using AuthzEntitlements.Entitlements.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// Pure-domain tests for the seat-assignment arithmetic. No database is needed:
// SeatDecision is a pure value type, so the assign/deny/idempotent rules are exercised
// directly (mirrors QuotaDecisionTests).
public sealed class SeatDecisionTests
{
    [Fact]
    public void Evaluate_WithCapacity_Assigns_AndIncrementsUsed()
    {
        var decision = SeatDecision.Evaluate(seatLimit: 25, seatsUsed: 5, alreadyAssigned: false);

        Assert.True(decision.Assigned);
        Assert.Equal(6, decision.SeatsUsed);
        Assert.Equal(19, decision.Remaining);
        Assert.Equal(SeatDecision.ReasonAssigned, decision.Reason);
    }

    [Fact]
    public void Evaluate_AtCapacity_Denies_DoesNotIncrement()
    {
        var decision = SeatDecision.Evaluate(seatLimit: 5, seatsUsed: 5, alreadyAssigned: false);

        Assert.False(decision.Assigned);
        Assert.Equal(5, decision.SeatsUsed);
        Assert.Equal(0, decision.Remaining);
        Assert.Equal(SeatDecision.ReasonLimitReached, decision.Reason);
    }

    [Fact]
    public void Evaluate_AlreadyAssigned_IsIdempotent_NoIncrement()
    {
        var decision = SeatDecision.Evaluate(seatLimit: 5, seatsUsed: 3, alreadyAssigned: true);

        Assert.True(decision.Assigned);
        Assert.Equal(3, decision.SeatsUsed);
        Assert.Equal(2, decision.Remaining);
        Assert.Equal(SeatDecision.ReasonAlreadyAssigned, decision.Reason);
    }

    [Fact]
    public void Evaluate_AlreadyAssigned_AtCapacity_StillIdempotentAllow()
    {
        // A user who already holds a seat is never denied, even when the plan is full.
        var decision = SeatDecision.Evaluate(seatLimit: 5, seatsUsed: 5, alreadyAssigned: true);

        Assert.True(decision.Assigned);
        Assert.Equal(5, decision.SeatsUsed);
        Assert.Equal(0, decision.Remaining);
        Assert.Equal(SeatDecision.ReasonAlreadyAssigned, decision.Reason);
    }

    [Fact]
    public void Evaluate_Unlimited_AlwaysAssigns_RemainingUnlimited()
    {
        var decision = SeatDecision.Evaluate(
            seatLimit: (int)EntitlementCatalog.Unlimited, seatsUsed: 100_000, alreadyAssigned: false);

        Assert.True(decision.Assigned);
        Assert.Equal(100_001, decision.SeatsUsed);
        Assert.Equal((int)EntitlementCatalog.Unlimited, decision.Remaining);
        Assert.Equal(SeatDecision.ReasonAssigned, decision.Reason);
    }

    [Fact]
    public void Evaluate_LastSeat_Assigns_ThenAtLimit_Denies()
    {
        // seatsUsed == limit - 1 → the last seat is granted.
        var granted = SeatDecision.Evaluate(seatLimit: 5, seatsUsed: 4, alreadyAssigned: false);
        Assert.True(granted.Assigned);
        Assert.Equal(5, granted.SeatsUsed);
        Assert.Equal(0, granted.Remaining);
        Assert.Equal(SeatDecision.ReasonAssigned, granted.Reason);

        // seatsUsed == limit → the next new user is denied.
        var denied = SeatDecision.Evaluate(seatLimit: 5, seatsUsed: 5, alreadyAssigned: false);
        Assert.False(denied.Assigned);
        Assert.Equal(SeatDecision.ReasonLimitReached, denied.Reason);
    }
}
