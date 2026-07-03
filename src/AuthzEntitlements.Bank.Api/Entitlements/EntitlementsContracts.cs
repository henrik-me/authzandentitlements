namespace AuthzEntitlements.Bank.Api.Entitlements;

// Client-side result records for the Entitlements.Service HTTP contract. Each carries
// an IsUnavailable flag so the enforcer can distinguish a genuine business "deny"
// (not entitled / not enabled / quota exceeded) from a fail-closed "service could not
// be reached" outcome, which maps to a different HTTP status (503) at the edge.

public sealed record ModuleCheckResult(bool Entitled, string PlanTier, string Reason)
{
    // True only for the fail-closed sentinel produced when the service is unreachable
    // or returns a non-success response. Never set from a deserialized wire payload.
    public bool IsUnavailable { get; init; }

    public static ModuleCheckResult Unavailable(string reason) =>
        new(false, EntitlementsCatalog.UnknownPlanTier, reason) { IsUnavailable = true };
}

public sealed record FeatureCheckResult(bool Enabled, string PlanTier, string Reason)
{
    public bool IsUnavailable { get; init; }

    public static FeatureCheckResult Unavailable(string reason) =>
        new(false, EntitlementsCatalog.UnknownPlanTier, reason) { IsUnavailable = true };
}

public sealed record QuotaDecisionResult(bool Allowed, long Limit, long Used, long Remaining, string Reason)
{
    public bool IsUnavailable { get; init; }

    public static QuotaDecisionResult Unavailable(string reason) =>
        new(false, 0, 0, 0, reason) { IsUnavailable = true };
}

public sealed record SeatUsageResult(string PlanTier, int SeatLimit, int SeatsUsed, int Remaining)
{
    public bool IsUnavailable { get; init; }

    // Only populated on the fail-closed sentinel; the wire contract carries no reason
    // field for seats, so this stays null on a successful lookup.
    public string? Reason { get; init; }

    public static SeatUsageResult Unavailable(string reason) =>
        new(EntitlementsCatalog.UnknownPlanTier, 0, 0, 0) { IsUnavailable = true, Reason = reason };
}

// Catalog keys and thresholds used when enforcing commercial entitlements. The keys
// are the canonical identifiers the Entitlements.Service exposes for this domain.
public static class EntitlementsCatalog
{
    public const string WireModuleKey = "wire";
    public const string HighValueTransactionsFeatureKey = "high-value-transactions";
    public const string MonthlyTransactionsQuotaKey = "monthly-transactions";

    // Distinct from BankPolicy.ApprovalThreshold (10_000m, maker-checker): high-value
    // transactions of ANY type at or above this amount additionally require the
    // high-value-transactions feature flag.
    public const decimal HighValueTransactionThreshold = 50_000m;

    public const string UnknownPlanTier = "unknown";
}
