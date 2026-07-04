namespace AuthzEntitlements.Authz.Pdp.Contracts;

// Shared explanation normalization + baseline so both the central PdpDecisionService AND the
// per-engine adapters reuse ONE mapping instead of duplicating it (CS16). Contracts must NOT
// depend on provider namespaces, so provider-local/rebac reason codes are matched by string
// literal (with a comment naming their source) rather than by importing their constants.
public static class DecisionExplanations
{
    // Maps a decision's primary reason CODE to its normalized determining rule. Known shared
    // ReasonCodes map by constant; provider-local/rebac codes map by string literal (Contracts must
    // not depend on provider namespaces); anything else falls back to EngineUnavailable (fail-closed
    // engine outcomes) — engines override this with a richer, engine-native explanation.
    public static string RuleForReason(string reasonCode) => reasonCode switch
    {
        ReasonCodes.Permit => DeterminingRules.AllRulesSatisfied,
        ReasonCodes.MissingScope => DeterminingRules.Scope,
        ReasonCodes.RoleNotAuthorized => DeterminingRules.Role,
        ReasonCodes.TenantMismatch or ReasonCodes.BranchNotInTenant => DeterminingRules.Tenant,
        ReasonCodes.SubjectNotMaker => DeterminingRules.SubjectIsMaker,
        ReasonCodes.NotPending => DeterminingRules.PendingStatus,
        ReasonCodes.MakerEqualsChecker => DeterminingRules.SegregationOfDuties,
        ReasonCodes.SodConflict => DeterminingRules.SegregationOfDuties, // CS11 governance SoD
        ReasonCodes.UnknownAction => DeterminingRules.UnknownAction,
        "NoRelationship" => DeterminingRules.Relationship,     // RebacReasonCodes.NoRelationship
        _ => DeterminingRules.EngineUnavailable,               // ProviderUnavailable/EngineUnavailable/boundary
    };

    // A baseline explanation derived purely from a decision's primary reason, used when a provider
    // attaches no richer explanation — guarantees every decision the service returns is explainable.
    public static DecisionExplanation Baseline(string engine, AccessDecision decision)
    {
        var reason = decision.Reasons.Count > 0
            ? decision.Reasons[0]
            : new Reason(decision.Decision.ToString(), decision.Decision.ToString());
        return new DecisionExplanation(
            Engine: engine,
            DeterminingRule: RuleForReason(reason.Code),
            PolicyReferences: [new PolicyReference(PolicyReferenceKinds.ReasonCode, reason.Code)],
            Narrative: reason.Message);
    }
}
