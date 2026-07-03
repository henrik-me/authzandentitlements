namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "resource" — what is being acted on. Optional attributes carry the values
// the fintech rules evaluate (owning tenant/branch, transaction amount, the maker who
// created it, and its status). Type is one of "account", "transaction", "tenant",
// "branch". Attributes a rule does not need stay null.
public sealed record Resource(
    string Type,
    string? Id = null,
    string? Tenant = null,
    string? Branch = null,
    decimal? Amount = null,
    string? MakerId = null,
    string? Status = null);
