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
    // Engine-agnostic "why" for this decision (CS16). Null until attached; the PdpDecisionService
    // guarantees a baseline is present on every decision it returns, and each engine adapter may
    // attach a richer, engine-native explanation via WithExplanation.
    public DecisionExplanation? Explanation { get; init; }

    public static AccessDecision Permit(Reason reason, params Obligation[] obligations) =>
        new(Contracts.Decision.Permit, [reason], obligations);

    public static AccessDecision Deny(Reason reason) =>
        new(Contracts.Decision.Deny, [reason], []);

    // Returns a copy carrying the given explanation (engine adapters use this to enrich).
    public AccessDecision WithExplanation(DecisionExplanation explanation) =>
        this with { Explanation = explanation };
}
