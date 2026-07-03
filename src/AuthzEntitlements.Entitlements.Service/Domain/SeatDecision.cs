namespace AuthzEntitlements.Entitlements.Service.Domain;

// Pure, side-effect-free seat-assignment arithmetic. Kept separate from persistence so
// the allow/deny/remaining rules are trivially unit-testable and have a single
// definition that both the endpoint and the tests exercise (mirrors QuotaDecision).
public readonly record struct SeatDecision(
    bool Assigned,
    int SeatsUsed,
    int Remaining,
    string Reason)
{
    public const string ReasonAlreadyAssigned = "already-assigned";
    public const string ReasonAssigned = "seat-assigned";
    public const string ReasonLimitReached = "seat-limit-reached";

    // Evaluates assigning a user to a seat against a plan with the given `seatLimit` and
    // the already-persisted `seatsUsed`. When the user already holds a seat the request is
    // idempotent: assigned, no increment. Otherwise a seat is granted iff there is capacity
    // (SeatMath.HasCapacity); on grant SeatsUsed is the post-increment value the caller
    // should persist. Unlimited plans (seatLimit < 0) always have capacity and report
    // Remaining as Unlimited (-1).
    public static SeatDecision Evaluate(int seatLimit, int seatsUsed, bool alreadyAssigned)
    {
        if (alreadyAssigned)
        {
            return new SeatDecision(
                true, seatsUsed, SeatMath.Remaining(seatLimit, seatsUsed), ReasonAlreadyAssigned);
        }

        if (SeatMath.HasCapacity(seatLimit, seatsUsed))
        {
            var after = seatsUsed + 1;
            return new SeatDecision(true, after, SeatMath.Remaining(seatLimit, after), ReasonAssigned);
        }

        return new SeatDecision(
            false, seatsUsed, SeatMath.Remaining(seatLimit, seatsUsed), ReasonLimitReached);
    }
}
