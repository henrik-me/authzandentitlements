namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A first-class, engine-agnostic explanation of WHY a decision was reached, attached to every
// AccessDecision (CS16). Normalizes the "why" across engines via DeterminingRule while surfacing
// each engine's native determining artifact(s) in PolicyReferences, so a playground / audit
// explorer (CS15) can render and compare explanations across engines.
public sealed record DecisionExplanation(
    string Engine,
    string DeterminingRule,
    IReadOnlyList<PolicyReference> PolicyReferences,
    string Narrative);

// One engine-native artifact that contributed to the decision: Kind (what sort of artifact),
// a stable Reference (its identifier), and optional human-readable Detail.
public sealed record PolicyReference(string Kind, string Reference, string? Detail = null);

// The normalized, engine-agnostic determining-rule vocabulary. Every engine maps its decision
// onto exactly one of these so explanations compare across engines (derived from the reason code).
public static class DeterminingRules
{
    public const string AllRulesSatisfied = "all-rules-satisfied";     // Permit
    public const string Scope = "scope";                               // MissingScope
    public const string Role = "role";                                 // RoleNotAuthorized
    public const string Tenant = "tenant";                             // TenantMismatch / BranchNotInTenant
    public const string SubjectIsMaker = "subject-is-maker";           // SubjectNotMaker
    public const string PendingStatus = "pending-status";              // NotPending
    public const string SegregationOfDuties = "segregation-of-duties"; // MakerEqualsChecker
    public const string Relationship = "relationship";                 // ReBAC NoRelationship
    public const string UnknownAction = "unknown-action";              // UnknownAction
    public const string EngineUnavailable = "engine-unavailable";      // provider-local fail-closed
}

// The kinds of engine-native policy artifacts surfaced in PolicyReference.Kind.
public static class PolicyReferenceKinds
{
    public const string ReasonCode = "reason-code";                 // baseline (no engine enrichment)
    public const string Rule = "rule";                              // reference-engine pipeline rule
    public const string RegoRule = "rego-rule";                     // OPA
    public const string CedarPolicy = "cedar-policy";               // Cedar policy id
    public const string CasbinRule = "casbin-rule";                 // Casbin matched policy line
    public const string AspNetRequirement = "aspnet-requirement";   // ASP.NET requirement
    public const string RelationshipTuple = "relationship-tuple";   // OpenFGA tuple
}
