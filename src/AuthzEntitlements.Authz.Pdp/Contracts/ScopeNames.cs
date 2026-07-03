namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The coarse-grained OAuth scopes carried on EvaluationContext.Scopes, mirroring
// Bank.Api's AuthorizationSetup scope policies. The PDP re-checks the relevant scope as
// defence in depth even though the edge already enforced it.
public static class ScopeNames
{
    public const string Read = "bank.read";
    public const string TransactionsWrite = "bank.transactions.write";
    public const string ApprovalsWrite = "bank.approvals.write";
}
