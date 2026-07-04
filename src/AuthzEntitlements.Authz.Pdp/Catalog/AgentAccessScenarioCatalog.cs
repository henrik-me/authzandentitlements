using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// CS19 on-behalf-of (OBO) / non-human-access scenarios, expressed in the SAME AuthorizationScenario
// shape as FintechScenarioCatalog but exercising Subject.Actor (delegation) and non-human Subject
// types. Reference-provider only: it demonstrates constrained delegation (an agent is authorized at
// the INTERSECTION of the human's rights and the agent's delegated scopes) and the human-path
// guarantee. It is intentionally SEPARATE from FintechScenarioCatalog, which is the Actor-free
// cross-engine parity catalog and must stay so.
public static class AgentAccessScenarioCatalog
{
    private const string Contoso = "CONTOSO";
    private const string Fabrikam = "FABRIKAM";

    private const string Teller1 = "user-teller1";
    private const string Manager1 = "user-manager1";
    private const string Compliance1 = "user-compliance1";

    private const string AgentId = "agent-copilot-1";
    private const string ServiceId = "svc-batch-reader";

    public static IReadOnlyList<AuthorizationScenario> Scenarios { get; } = Build();

    private static IReadOnlyList<AuthorizationScenario> Build()
    {
        var readScope = new EvaluationContext([ScopeNames.Read]);
        var txnWriteScope = new EvaluationContext([ScopeNames.TransactionsWrite]);
        var approvalsScope = new EvaluationContext([ScopeNames.ApprovalsWrite]);

        return
        [
            Scenario("agent-read-in-tenant-permit",
                "Agent with the delegated read scope reads an in-tenant account for a permitted teller.",
                OboTeller(Teller1, Contoso, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Contoso), readScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("agent-read-missing-delegated-scope",
                "Agent lacks the delegated read scope for an action the teller is otherwise permitted.",
                OboTeller(Teller1, Contoso),
                ActionNames.AccountRead, Account(Contoso), readScope,
                Decision.Deny, ReasonCodes.DelegationScopeMissing),

            Scenario("agent-read-cross-tenant-passthrough",
                "Agent acts for a teller reading another tenant: the human is denied, so the agent is too (same reason).",
                OboTeller(Teller1, Contoso, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Fabrikam), readScope,
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("service-self-read-permit",
                "A non-human subject acting AS ITSELF (Type=service, no Actor) reads an in-tenant account.",
                Service(ServiceId, Contoso),
                ActionNames.AccountRead, Account(Contoso), readScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("agent-create-large-txn-permit",
                "Agent with the delegated write scope creates a $10,000 boundary transaction for a teller maker.",
                OboTeller(Teller1, Contoso, AgentScopeNames.TransactionsWrite),
                ActionNames.TransactionCreate, Transaction(Contoso, 10_000m, Teller1), txnWriteScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("agent-create-txn-missing-delegated-scope",
                "Agent holding only the read delegated scope tries to create a transaction the teller could make.",
                OboTeller(Teller1, Contoso, AgentScopeNames.Read),
                ActionNames.TransactionCreate, Transaction(Contoso, 250m, Teller1), txnWriteScope,
                Decision.Deny, ReasonCodes.DelegationScopeMissing),

            Scenario("agent-approve-permit",
                "Agent with the delegated approvals scope approves a pending in-tenant transaction for a manager.",
                OboManager(Manager1, Contoso, AgentScopeNames.ApprovalsWrite),
                ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Teller1, "Pending"), approvalsScope,
                Decision.Permit, ReasonCodes.Permit),

            Scenario("agent-approve-missing-delegated-scope",
                "Agent lacking the delegated approvals scope tries to approve for an otherwise-permitted manager.",
                OboManager(Manager1, Contoso, AgentScopeNames.Read),
                ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Teller1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.DelegationScopeMissing),

            Scenario("agent-approve-role-ineligible-passthrough",
                "Agent WITH the delegated approvals scope acts for a teller: the human is not checker-eligible, so it denies (agent cannot exceed the user).",
                OboTeller(Teller1, Contoso, AgentScopeNames.ApprovalsWrite),
                ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Manager1, "Pending"), approvalsScope,
                Decision.Deny, ReasonCodes.RoleNotAuthorized),

            Scenario("agent-reject-permit",
                "Agent with the delegated approvals scope rejects a pending in-tenant transaction for a compliance officer.",
                OboCompliance(Compliance1, Contoso, AgentScopeNames.ApprovalsWrite),
                ActionNames.TransactionReject,
                Transaction(Contoso, 20_000m, Teller1, "Pending"), approvalsScope,
                Decision.Permit, ReasonCodes.Permit),
        ];
    }

    private static Subject OboTeller(string id, string tenant, params string[] agentScopes) =>
        new("user", id, [RoleNames.Teller], tenant, Actor: Agent(agentScopes));

    private static Subject OboManager(string id, string tenant, params string[] agentScopes) =>
        new("user", id, [RoleNames.BranchManager], tenant, Actor: Agent(agentScopes));

    private static Subject OboCompliance(string id, string tenant, params string[] agentScopes) =>
        new("user", id, [RoleNames.ComplianceOfficer], tenant, Actor: Agent(agentScopes));

    private static Subject Service(string id, string tenant) =>
        new("service", id, [RoleNames.Teller], tenant);

    private static Actor Agent(string[] scopes) => new("agent", AgentId, scopes);

    private static Resource Account(string tenant) => new("account", Tenant: tenant);

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
