using AuthzEntitlements.Entitlements.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// Pure seat arithmetic: remaining seats and capacity, including the unlimited case.
public sealed class SeatMathTests
{
    [Fact]
    public void Remaining_Bounded_IsLimitMinusUsed() =>
        Assert.Equal(20, SeatMath.Remaining(seatLimit: 25, seatsUsed: 5));

    [Fact]
    public void Remaining_Full_IsZero() =>
        Assert.Equal(0, SeatMath.Remaining(seatLimit: 5, seatsUsed: 5));

    [Fact]
    public void Remaining_Unlimited_IsNegativeOne() =>
        Assert.Equal(
            (int)EntitlementCatalog.Unlimited,
            SeatMath.Remaining(seatLimit: (int)EntitlementCatalog.Unlimited, seatsUsed: 9999));

    [Theory]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(-1, 100000, true)]
    public void HasCapacity_RespectsLimitAndUnlimited(int seatLimit, int seatsUsed, bool expected) =>
        Assert.Equal(expected, SeatMath.HasCapacity(seatLimit, seatsUsed));
}
