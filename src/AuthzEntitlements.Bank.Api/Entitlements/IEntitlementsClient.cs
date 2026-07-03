namespace AuthzEntitlements.Bank.Api.Entitlements;

// Typed client for the Entitlements.Service. All methods fail closed: a transport
// failure or non-success response yields an "unavailable" sentinel result rather than
// throwing, so the enforcer can translate it into a deny (503) without the caller ever
// creating a transaction against an unknown entitlement state.
public interface IEntitlementsClient
{
    Task<ModuleCheckResult> CheckModuleAsync(string tenantCode, string moduleKey, CancellationToken ct);

    Task<FeatureCheckResult> CheckFeatureAsync(string tenantCode, string featureKey, CancellationToken ct);

    Task<QuotaDecisionResult> ConsumeQuotaAsync(string tenantCode, string quotaKey, long amount, CancellationToken ct);

    Task<SeatUsageResult> GetSeatsAsync(string tenantCode, CancellationToken ct);
}
