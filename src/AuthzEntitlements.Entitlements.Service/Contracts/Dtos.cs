namespace AuthzEntitlements.Entitlements.Service.Contracts;

// Response DTOs for the entitlements API. Property names are the authoritative wire
// contract (the sibling Bank.Api client is coded to these exact names); ASP.NET's web
// JSON defaults serialise them camelCase, and the enum-less shape keeps every value a
// primitive or string.

public sealed record PlanSummaryResponse(
    string TenantCode,
    string PlanTier,
    int SeatLimit,
    int SeatsUsed,
    string[] Modules,
    string[] Features);

public sealed record ModuleEntitlementResponse(
    bool Entitled,
    string PlanTier,
    string Reason);

public sealed record FeatureEntitlementResponse(
    bool Enabled,
    string PlanTier,
    string Reason);

public sealed record QuotaConsumeResponse(
    bool Allowed,
    long Limit,
    long Used,
    long Remaining,
    string Reason);

public sealed record SeatSummaryResponse(
    string PlanTier,
    int SeatLimit,
    int SeatsUsed,
    int Remaining);

// Optional body for the consume endpoint. A missing body or a non-positive amount is
// treated as 1 by the handler.
public sealed record ConsumeQuotaRequest(long Amount);
