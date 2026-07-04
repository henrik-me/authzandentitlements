namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The delegated agent-capability scopes carried on Actor.Scopes for on-behalf-of (OBO) calls,
// plus the action -> required-scope map the reference provider applies as the delegation
// constraint. These are DISTINCT from the coarse OAuth ScopeNames the human already holds: an
// agent may act for a user only when it ALSO holds the delegated scope for the action class, so
// a scoped, time-boxed agent token can be strictly narrower than the user it acts for.
public static class AgentScopeNames
{
    public const string Read = "agent.bank.read";
    public const string TransactionsWrite = "agent.bank.transactions.write";
    public const string ApprovalsWrite = "agent.bank.approvals.write";

    // The delegated scope an agent MUST hold to perform `action` on behalf of a user. Fail-closed:
    // an unmapped/unknown action returns null so the caller denies (a delegate can never act on an
    // action class the delegation model does not explicitly cover). References the ActionNames
    // constants so the vocabulary has a single source of truth.
    public static string? RequiredFor(string action) => action switch
    {
        ActionNames.AccountRead => Read,
        ActionNames.AccountCreate => TransactionsWrite,
        ActionNames.TransactionCreate => TransactionsWrite,
        ActionNames.TransactionApprove or ActionNames.TransactionReject => ApprovalsWrite,
        ActionNames.GovernanceAccessRequest => Read,
        _ => null,
    };
}
