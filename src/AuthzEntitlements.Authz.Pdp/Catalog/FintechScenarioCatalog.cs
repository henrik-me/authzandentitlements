using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// A fixed set of concrete fintech authorization scenarios, expressed once and
// engine-agnostic, so any provider is dispatched the same questions and compared against
// the same expected outcomes. Covers permit and deny across tenant isolation, role
// eligibility, the maker-checker threshold (including the boundary), segregation of
// duties, subject-is-maker, missing scopes, and the fail-closed unknown-action path.
// Synthetic string ids (no real GUIDs) keep the catalog portable across engines.
public static class FintechScenarioCatalog
{
    private const string Contoso = "CONTOSO";
    private const string Fabrikam = "FABRIKAM";

    private const string Teller1 = "user-teller1";
    private const string Manager1 = "user-manager1";
    private const string Compliance1 = "user-compliance1";
    private const string Auditor1 = "user-auditor1";
    private const string FabrikamTeller = "user-fabrikam-teller";

    public static IReadOnlyList<AuthorizationScenario> Scenarios { get; } = Build();

    private static IReadOnlyList<AuthorizationScenario> Build()
    {
        var readScope = new EvaluationContext([ScopeNames.Read]);
        var txnWriteScope = new EvaluationContext([ScopeNames.TransactionsWrite]);
        var approvalsScope = new EvaluationContext([ScopeNames.ApprovalsWrite]);
        var noScopes = new EvaluationContext([]);

        return
        [
            Scenario("read-own-tenant-account",
                "Teller reads an account in their own tenant.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso), readScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("read-other-tenant-account",
                "Teller reads an account in another tenant.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Fabrikam), readScope,
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("auditor-reads-own-tenant",
                "Auditor (read-only role) reads tenant-scoped data in their own tenant.",
                Auditor(Auditor1, Contoso), ActionNames.AccountRead, TenantResource(Contoso), readScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("teller-create-account",
                "Teller tries to create an account (create requires BranchManager).",
                Teller(Teller1, Contoso), ActionNames.AccountCreate, Account(Contoso), readScope,
                Decision.Deny, ReasonCodes.RoleNotAuthorized),

            Scenario("manager-create-account-own-tenant",
                "BranchManager creates an account in their own tenant.",
                Manager(Manager1, Contoso), ActionNames.AccountCreate, Account(Contoso), readScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("manager-create-account-other-tenant",
                "BranchManager tries to create an account in another tenant.",
                Manager(Manager1, Contoso), ActionNames.AccountCreate, Account(Fabrikam), readScope,
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("teller-create-small-txn",
                "Teller creates a $250 debit as themselves (below threshold: post_immediately).",
                Teller(Teller1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 250m, Teller1), txnWriteScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("teller-create-large-txn",
                "Teller creates a $15,000 transfer as themselves (at/above threshold: require_approval).",
                Teller(Teller1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 15_000m, Teller1), txnWriteScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("manager-approve-pending",
                "BranchManager approves a pending transaction made by a teller in-tenant.",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Teller1, "Pending"), approvalsScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("teller-approve-not-eligible",
                "Teller tries to approve a pending transaction (teller is not checker-eligible).",
                Teller(Teller1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Manager1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.RoleNotAuthorized),

            Scenario("manager-approve-own-txn-sod",
                "BranchManager approves a transaction they themselves made (segregation of duties).",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Manager1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.MakerEqualsChecker),

            Scenario("compliance-reject-pending",
                "ComplianceOfficer rejects a pending transaction made by a teller in-tenant.",
                Compliance(Compliance1, Contoso), ActionNames.TransactionReject,
                Transaction(Contoso, 15_000m, Teller1, "Pending"), approvalsScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("manager-approve-already-approved",
                "BranchManager tries to approve a transaction that is already Approved.",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Teller1, "Approved"), approvalsScope,
                Decision.Deny, ReasonCodes.NotPending),

            Scenario("manager-approve-other-tenant-txn",
                "BranchManager tries to approve a pending transaction in another tenant.",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Fabrikam, 15_000m, Teller1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("fabrikam-teller-reads-contoso",
                "Fabrikam teller reads a Contoso account (cross-tenant read).",
                Teller(FabrikamTeller, Fabrikam), ActionNames.AccountRead, Account(Contoso), readScope,
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("auditor-create-txn",
                "Auditor tries to create a transaction (auditor is not maker-eligible).",
                Auditor(Auditor1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 100m, Auditor1), txnWriteScope,
                Decision.Deny, ReasonCodes.RoleNotAuthorized),

            Scenario("teller-create-txn-no-scope",
                "Teller tries to create a transaction without the transactions.write scope.",
                Teller(Teller1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 250m, Teller1), noScopes,
                Decision.Deny, ReasonCodes.MissingScope),

            Scenario("teller-read-no-scope",
                "Teller tries to read an account without the read scope.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso), noScopes,
                Decision.Deny, ReasonCodes.MissingScope),

            Scenario("compliance-approve-own-txn-sod",
                "ComplianceOfficer approves a transaction they themselves made (SoD, second role).",
                Compliance(Compliance1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 20_000m, Compliance1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.MakerEqualsChecker),

            Scenario("teller-create-txn-for-other-maker",
                "Teller tries to create a transaction attributed to a different maker.",
                Teller(Teller1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 250m, Manager1), txnWriteScope,
                Decision.Deny, ReasonCodes.SubjectNotMaker),

            Scenario("teller-create-threshold-boundary",
                "Teller creates an exactly $10,000 transaction (boundary: require_approval).",
                Teller(Teller1, Contoso), ActionNames.TransactionCreate,
                Transaction(Contoso, 10_000m, Teller1), txnWriteScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("unknown-action-fails-closed",
                "An action outside the known vocabulary is denied (fail closed).",
                Teller(Teller1, Contoso), "bank.account.delete", Account(Contoso), readScope,
                Decision.Deny, ReasonCodes.UnknownAction),
        ];
    }

    private static Subject Teller(string id, string tenant) =>
        new("user", id, [RoleNames.Teller], tenant);

    private static Subject Manager(string id, string tenant) =>
        new("user", id, [RoleNames.BranchManager], tenant);

    private static Subject Compliance(string id, string tenant) =>
        new("user", id, [RoleNames.ComplianceOfficer], tenant);

    private static Subject Auditor(string id, string tenant) =>
        new("user", id, [RoleNames.Auditor], tenant);

    private static Resource Account(string tenant) => new("account", Tenant: tenant);

    private static Resource TenantResource(string tenant) => new("tenant", Tenant: tenant);

    private static Resource Transaction(string tenant, decimal amount, string makerId, string? status = null) =>
        new("transaction", Tenant: tenant, Amount: amount, MakerId: makerId, Status: status);

    private static AuthorizationScenario Scenario(
        string id,
        string description,
        Subject subject,
        string action,
        Resource resource,
        EvaluationContext context,
        Decision expected,
        string expectedReasonCode) =>
        new(id, description,
            new AccessRequest(subject, new ActionRequest(action), resource, context),
            expected, expectedReasonCode);
}
