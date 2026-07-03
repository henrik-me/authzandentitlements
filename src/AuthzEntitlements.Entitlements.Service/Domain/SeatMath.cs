namespace AuthzEntitlements.Entitlements.Service.Domain;

// Pure seat arithmetic. Remaining seats is the plan limit minus seats used, unless
// the plan is unlimited (SeatLimit < 0), in which case remaining is Unlimited (-1)
// and a seat request is never denied.
public static class SeatMath
{
    public static int Remaining(int seatLimit, int seatsUsed) =>
        seatLimit < 0 ? (int)EntitlementCatalog.Unlimited : seatLimit - seatsUsed;

    public static bool HasCapacity(int seatLimit, int seatsUsed) =>
        seatLimit < 0 || seatsUsed < seatLimit;
}
