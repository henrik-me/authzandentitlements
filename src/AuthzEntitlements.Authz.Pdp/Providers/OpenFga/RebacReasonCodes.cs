namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// CS07-specific reason codes for the OpenFGA (ReBAC) adapter. The shared CS05 ReasonCodes
// live in the contract (Reason.cs) and are OWNED by CS05, so the ReBAC-only outcomes are
// declared here rather than by editing that file. The adapter reuses ReasonCodes.Permit and
// ReasonCodes.UnknownAction where they already fit; these add the relationship-specific denials.
public static class RebacReasonCodes
{
    // No relationship path grants the requested relation between subject and resource — the
    // ordinary ReBAC deny (the Check returned allowed=false).
    public const string NoRelationship = "NoRelationship";

    // A ReBAC Check needs a concrete resource id ("account:acme-checking"); the request carried a
    // resource with no id, so it is denied at the boundary rather than checked against a blank object.
    public const string MissingResourceId = "MissingResourceId";

    // The requested resource type has no queryable relation in the ReBAC model — the adapter answers
    // account-relationship questions, so a non-account resource (e.g. the CS05 "transaction" shape) is
    // denied at the boundary rather than issuing a Check against a type the model does not define.
    public const string UnsupportedResourceType = "UnsupportedResourceType";
}
