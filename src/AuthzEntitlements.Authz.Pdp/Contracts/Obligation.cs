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
}
