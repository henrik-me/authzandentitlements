namespace AuthzEntitlements.Authz.Pdp.Contracts;

// AuthZEN's boolean decision, modelled as an enum for readability and to leave room for
// a future NotApplicable outcome. Deny is 0 so the zero-value default fails closed.
public enum Decision
{
    Deny = 0,
    Permit = 1,
}
