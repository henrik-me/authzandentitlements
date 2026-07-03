namespace AuthzEntitlements.Entitlements.Service.Metering;

// The kind of entitlement decision being recorded. Serialised lower-case in the audit
// event so downstream ingestion (CS13) sees stable, matchable values.
public enum EntitlementDecisionType
{
    Module,
    Feature,
    Quota,
    Seat,
}

public enum EntitlementOutcome
{
    Allow,
    Deny,
}

// An audit-ready record of a single entitlement decision. Amount/Used/Limit are
// populated for quota (and, where meaningful, seat) decisions and left null otherwise.
public sealed record EntitlementDecision(
    string TenantCode,
    EntitlementDecisionType DecisionType,
    string Key,
    EntitlementOutcome Outcome,
    string PlanTier,
    long? Amount,
    long? Used,
    long? Limit,
    DateTimeOffset TimestampUtc);
