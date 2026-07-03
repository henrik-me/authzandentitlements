namespace AuthzEntitlements.Entitlements.Service.Domain;

// Pure, side-effect-free quota arithmetic. Kept separate from persistence so the
// allow/deny/remaining rules are trivially unit-testable and have a single
// definition that both the endpoint and the tests exercise.
public readonly record struct QuotaDecision(
    bool Allowed,
    long Limit,
    long Used,
    long Remaining,
    string Reason)
{
    public const string ReasonUnlimited = "unlimited";
    public const string ReasonWithinQuota = "within-quota";
    public const string ReasonExceeded = "quota-exceeded";

    // Evaluates a consume of `amount` against a quota with the given `limit` and the
    // already-persisted `used`. A non-positive amount is normalised to 1 (matching the
    // consume contract). On allow, Used is the post-increment value the caller should
    // persist; on deny, Used is unchanged and nothing is persisted.
    //
    // Unlimited (limit < 0): always allowed, usage is still tracked so metering is
    // real, and Remaining is Unlimited (-1). Bounded: allowed iff used + amount does
    // not exceed the limit.
    public static QuotaDecision Evaluate(long limit, long used, long amount)
    {
        var delta = amount <= 0 ? 1 : amount;

        if (limit < 0)
        {
            var trackedUsed = used + delta;
            return new QuotaDecision(true, limit, trackedUsed, EntitlementCatalog.Unlimited, ReasonUnlimited);
        }

        if (used + delta <= limit)
        {
            var newUsed = used + delta;
            return new QuotaDecision(true, limit, newUsed, limit - newUsed, ReasonWithinQuota);
        }

        return new QuotaDecision(false, limit, used, limit - used, ReasonExceeded);
    }
}
