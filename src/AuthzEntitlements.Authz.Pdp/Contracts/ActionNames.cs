namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The well-known action verbs the PDP understands, mirroring the Bank.Api enforcement
// surface. Shared by the reference provider (rule dispatch) and the scenario catalog
// (request authoring) so the vocabulary has a single source of truth.
public static class ActionNames
{
    public const string AccountRead = "bank.account.read";
    public const string AccountCreate = "bank.account.create";
    public const string TransactionCreate = "bank.transaction.create";
    public const string TransactionApprove = "bank.transaction.approve";
    public const string TransactionReject = "bank.transaction.reject";

    // The bounded tag value for any action outside the known vocabulary. Callers can send an
    // arbitrary action.name (evaluate is anonymous and unknown actions fail closed), so metric
    // tags collapse unknowns to this constant to keep metric label cardinality bounded.
    public const string Unknown = "unknown";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        AccountRead,
        AccountCreate,
        TransactionCreate,
        TransactionApprove,
        TransactionReject,
    };

    // Normalizes an action name to the bounded vocabulary for low-cardinality metric tags: a
    // known verb passes through; anything else collapses to "unknown". The raw action stays on
    // spans and audit events for debugging.
    public static string ForMetric(string name) => Known.Contains(name) ? name : Unknown;
}
