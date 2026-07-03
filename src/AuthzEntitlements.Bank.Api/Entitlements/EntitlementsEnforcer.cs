using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.AspNetCore.Http;

namespace AuthzEntitlements.Bank.Api.Entitlements;

// The outcome of evaluating commercial entitlements for a transaction create. A denied
// decision carries the caller-facing Reason and the HTTP StatusCode the endpoint returns.
public sealed record EnforcementDecision(bool Allowed, string Reason, int StatusCode)
{
    public static readonly EnforcementDecision Allow =
        new(true, "entitled", StatusCodes.Status200OK);
}

// Pure, host-agnostic decision service for commercial-entitlement enforcement on the
// transaction-create path. It orchestrates the module → feature → quota gates in order,
// short-circuiting on the first deny, and depends only on IEntitlementsClient (passed in)
// plus a logger — no DbContext, HTTP host, or ambient state — so it is directly testable.
//
// Fail-closed: whenever a gate's client call returns an Unavailable sentinel, the decision
// is a 503 deny. Known limitation (CS10): a quota already consumed by ConsumeQuotaAsync is
// not compensated if a later create step fails; audit ingestion + compensation land in CS13.
public sealed class EntitlementsEnforcer(ILogger<EntitlementsEnforcer> logger)
{
    private static readonly EventId EnforcementDeniedEvent = new(1001, "EntitlementEnforcementDenied");

    public async Task<EnforcementDecision> EvaluateCreateAsync(
        IEntitlementsClient client, string tenantCode, TransactionType type, decimal amount, CancellationToken ct)
    {
        // 1. Module gate — wire transfers require the licensed "wire" module.
        if (type == TransactionType.Transfer)
        {
            var module = await client.CheckModuleAsync(tenantCode, EntitlementsCatalog.WireModuleKey, ct);
            if (module.IsUnavailable)
            {
                return Unavailable(tenantCode, "module", EntitlementsCatalog.WireModuleKey, module.Reason);
            }

            if (!module.Entitled)
            {
                return Deny(tenantCode, "module", EntitlementsCatalog.WireModuleKey,
                    $"wire module is not licensed for plan {module.PlanTier}",
                    StatusCodes.Status402PaymentRequired);
            }
        }

        // 2. Feature gate — high-value amounts require the high-value-transfers feature.
        if (amount >= EntitlementsCatalog.HighValueTransferThreshold)
        {
            var feature = await client.CheckFeatureAsync(
                tenantCode, EntitlementsCatalog.HighValueTransfersFeatureKey, ct);
            if (feature.IsUnavailable)
            {
                return Unavailable(tenantCode, "feature", EntitlementsCatalog.HighValueTransfersFeatureKey, feature.Reason);
            }

            if (!feature.Enabled)
            {
                return Deny(tenantCode, "feature", EntitlementsCatalog.HighValueTransfersFeatureKey,
                    $"high-value transfers require the high-value-transfers feature (plan {feature.PlanTier})",
                    StatusCodes.Status403Forbidden);
            }
        }

        // 3. Quota gate — every create consumes one unit of the monthly-transactions quota.
        var quota = await client.ConsumeQuotaAsync(
            tenantCode, EntitlementsCatalog.MonthlyTransactionsQuotaKey, 1, ct);
        if (quota.IsUnavailable)
        {
            return Unavailable(tenantCode, "quota", EntitlementsCatalog.MonthlyTransactionsQuotaKey, quota.Reason);
        }

        if (!quota.Allowed)
        {
            return Deny(tenantCode, "quota", EntitlementsCatalog.MonthlyTransactionsQuotaKey,
                $"monthly transaction quota exceeded ({quota.Used}/{quota.Limit})",
                StatusCodes.Status429TooManyRequests);
        }

        return EnforcementDecision.Allow;
    }

    private EnforcementDecision Deny(string tenantCode, string gate, string key, string reason, int statusCode)
    {
        logger.LogWarning(EnforcementDeniedEvent,
            "Entitlement enforcement denied for tenant {TenantCode} at {Gate} gate (key {Key}): {Reason}",
            tenantCode, gate, key, reason);
        return new EnforcementDecision(false, reason, statusCode);
    }

    private EnforcementDecision Unavailable(string tenantCode, string gate, string key, string underlyingReason)
    {
        // Log the diagnostic detail but return a stable, non-leaking caller-facing message.
        Deny(tenantCode, gate, key, $"entitlements service unavailable: {underlyingReason}",
            StatusCodes.Status503ServiceUnavailable);
        return new EnforcementDecision(false, "entitlements service unavailable",
            StatusCodes.Status503ServiceUnavailable);
    }
}
