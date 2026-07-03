namespace AuthzEntitlements.Entitlements.Service.Domain;

// Persisted usage for a metered quota, one row per (tenant, quota, period). PeriodKey
// is a coarse bucket such as "2026-07" so quotas reset naturally each month. Used is
// incremented atomically under optimistic concurrency when a consume call is allowed.
public sealed class UsageCounter
{
    public Guid Id { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string QuotaKey { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public long Used { get; set; }

    // The current calendar-month bucket in UTC, e.g. "2026-07".
    public static string CurrentPeriod(DateTimeOffset nowUtc) =>
        nowUtc.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}
