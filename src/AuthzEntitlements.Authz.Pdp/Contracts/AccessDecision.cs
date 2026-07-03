namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The answer to an AccessRequest: the Permit/Deny decision plus the reasons that explain
// it and any obligations the caller must honour on a Permit. Reasons[0] is the primary
// reason (the code parity checks and audit records key on). The factories keep every
// decision self-explaining — a decision always carries at least one reason.
public sealed record AccessDecision(
    Decision Decision,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<Obligation> Obligations)
{
    public static AccessDecision Permit(Reason reason, params Obligation[] obligations) =>
        new(Contracts.Decision.Permit, [reason], obligations);

    public static AccessDecision Deny(Reason reason) =>
        new(Contracts.Decision.Deny, [reason], []);
}
