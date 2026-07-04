namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;

// The deserialization shape of an OPA data-API decision response. OPA wraps a rule's value
// under "result"; when the policy is undefined for the input, OPA returns "{}" with no
// "result" at all — a null Result, which the provider treats as fail-closed. Property matching
// is case-insensitive (JsonSerializerDefaults.Web), so the exact JSON casing is not load-bearing.
internal sealed record OpaDecisionResponse
{
    // Absent (null) when the policy produced no decision for the input (OPA returns "{}").
    public OpaDecisionResult? Result { get; init; }
}

// The decision object the Rego policy returns under "result": a Permit/Deny string, the stable
// reason code, and zero or more obligation strings. All members are nullable so a malformed or
// partial body is detected (and fails closed) rather than throwing.
internal sealed record OpaDecisionResult
{
    // "Permit" or "Deny"; any other value is unrecognized and fails closed.
    public string? Decision { get; init; }

    // The stable reason code (e.g. "Permit", "MissingScope", "MakerEqualsChecker").
    public string? Reason { get; init; }

    // The determining check id (CS16), "<action-short>.<Reason>" (e.g. "read.Permit",
    // "transaction.create.MissingScope", "unknown.UnknownAction"). Additive and nullable: an older
    // policy that predates the field returns null, in which case the adapter degrades to a
    // package-path-only explanation rather than failing the decision.
    public string? Rule { get; init; }

    // Obligation ids for a permitted transaction.create ("require_approval"/"post_immediately");
    // absent or empty otherwise.
    public IReadOnlyList<string>? Obligations { get; init; }
}
