namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "context" — request-time facts outside the subject/resource. For the
// fintech rules the coarse-grained granted scopes (e.g. "bank.read",
// "bank.transactions.write") are what the PDP still re-checks as defence in depth.
// Kept intentionally small; new contextual facts are added as members over time.
public sealed record EvaluationContext(IReadOnlyList<string> Scopes);
