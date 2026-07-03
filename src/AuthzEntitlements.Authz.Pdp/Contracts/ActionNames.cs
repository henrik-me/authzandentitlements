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
}
