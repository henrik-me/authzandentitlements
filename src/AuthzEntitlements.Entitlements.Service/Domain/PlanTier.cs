namespace AuthzEntitlements.Entitlements.Service.Domain;

// Commercial plan tiers. Persisted as strings via HasConversion<string>() so the
// database stores stable, human-readable values rather than brittle ordinals, and
// the same names flow onto the wire and into OpenFeature evaluation contexts.
public enum PlanTier
{
    Standard,
    Professional,
    Enterprise,
}
