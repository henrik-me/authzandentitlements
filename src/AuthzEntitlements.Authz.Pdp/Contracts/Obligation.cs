namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A post-decision requirement the caller must honour when acting on a Permit — e.g. a
// large transfer permits creation but obliges a second-person approval. Properties
// carry optional structured detail for the obligation.
public sealed record Obligation(
    string Id,
    IReadOnlyDictionary<string, string>? Properties = null);

// Well-known obligation ids the reference provider attaches to a permitted
// transaction.create, keyed off the maker-checker threshold.
public static class ObligationIds
{
    public const string RequireApproval = "require_approval";
    public const string PostImmediately = "post_immediately";

    // CS21 break-glass: attached to a BreakGlassInvoked permit to enforce the mandatory post-review
    // of every emergency elevation. A break-glass grant is bounded and audited, and a human must
    // review the elevated access after the fact — this obligation carries that requirement.
    public const string RequireBreakGlassReview = "require_break_glass_review";
}
