namespace AuthzEntitlements.Entitlements.Service.Domain;

// The stable catalog of entitlement keys shared across the seed, the endpoints, and
// (via the sibling Bank.Api client) the rest of the system. These strings are the
// wire contract for module/feature/quota lookups, so they never change casually.
public static class EntitlementCatalog
{
    public static class Modules
    {
        public const string Wire = "wire";
        public const string Fx = "fx";
        public const string Treasury = "treasury";

        public static readonly IReadOnlyList<string> All = [Wire, Fx, Treasury];
    }

    public static class Features
    {
        public const string HighValueTransactions = "high-value-transactions";
        public const string BulkPayments = "bulk-payments";

        public static readonly IReadOnlyList<string> All = [HighValueTransactions, BulkPayments];
    }

    public static class Quotas
    {
        public const string MonthlyTransactions = "monthly-transactions";

        public static readonly IReadOnlyList<string> All = [MonthlyTransactions];
    }

    // Sentinel limit meaning "unlimited": never denied, no hard cap tracked. Used by
    // both quota limits and seat limits so callers have a single unambiguous marker.
    public const long Unlimited = -1;
}
