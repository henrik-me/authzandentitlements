namespace AuthzEntitlements.Entitlements.Service.Domain;

// A module licensed by a plan tier (wire / fx / treasury). The presence of the row
// is the entitlement: a module lookup is "does a PlanModule exist for this tier and
// module key?".
public sealed class PlanModule
{
    public PlanTier PlanTier { get; set; }
    public string ModuleKey { get; set; } = string.Empty;

    public Plan Plan { get; set; } = null!;
}
