using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// CS21 break-glass + manager->delegate delegation scenarios, expressed in the SAME
// AuthorizationScenario shape as AgentAccessScenarioCatalog but exercising the CS21 EvaluationContext
// members (BreakGlass, Delegation, Now). Reference-provider only. It demonstrates the core fintech
// control that break-glass grants a MISSING CAPABILITY yet never bypasses an integrity invariant, and
// that a manager->delegate delegation requires an active, matching grant on top of the CS19 OBO
// intersection. It is intentionally SEPARATE from FintechScenarioCatalog, which is the Actor-free,
// grant-free cross-engine parity catalog and must stay so.
public static class BreakGlassDelegationScenarioCatalog
{
    private const string Contoso = "CONTOSO";
    private const string Fabrikam = "FABRIKAM";

    private const string Teller1 = "user-teller1";
    private const string Manager1 = "user-manager1";
    private const string Delegate1 = "user-delegate1";
    private const string Stranger1 = "user-stranger1";

    // The injected decision clock. Every grant expiry is expressed relative to it so the whole catalog
    // is deterministic — the provider never reads the wall clock.
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Active = Now.AddHours(1);   // future => not yet expired
    private static readonly DateTimeOffset Expired = Now.AddHours(-1); // past   => already expired

    public static IReadOnlyList<AuthorizationScenario> Scenarios { get; } = Build();

    private static IReadOnlyList<AuthorizationScenario> Build()
    {
        return
        [
            // ---- Break-glass elevates a MISSING-CAPABILITY deny ----
            Scenario("break-glass-elevates-missing-scope",
                "Break-glass elevates a missing-scope read deny to a permit with mandatory review.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso),
                BreakGlassContext([], BreakGlass(Teller1, ActionNames.AccountRead, Active)),
                Decision.Permit, ReasonCodes.BreakGlassInvoked),

            Scenario("break-glass-elevates-role-not-authorized",
                "Break-glass elevates a role-ineligible account-create deny to a permit.",
                Teller(Teller1, Contoso), ActionNames.AccountCreate, Account(Contoso),
                BreakGlassContext([ScopeNames.Read], BreakGlass(Teller1, ActionNames.AccountCreate, Active)),
                Decision.Permit, ReasonCodes.BreakGlassInvoked),

            // ---- Fail-closed break-glass: the base deny stands ----
            Scenario("break-glass-expired-grant-deny-stands",
                "An expired break-glass grant does not elevate: the missing-scope deny stands.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso),
                BreakGlassContext([], BreakGlass(Teller1, ActionNames.AccountRead, Expired)),
                Decision.Deny, ReasonCodes.MissingScope),

            Scenario("break-glass-mismatched-subject-deny-stands",
                "A break-glass grant issued to a different subject does not elevate.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso),
                BreakGlassContext([], BreakGlass(Manager1, ActionNames.AccountRead, Active)),
                Decision.Deny, ReasonCodes.MissingScope),

            // ---- Break-glass NEVER overrides an integrity invariant (the key control) ----
            Scenario("break-glass-does-not-override-tenant-mismatch",
                "Break-glass never bypasses tenant isolation: a cross-tenant read stays denied.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Fabrikam),
                BreakGlassContext([ScopeNames.Read], BreakGlass(Teller1, ActionNames.AccountRead, Active)),
                Decision.Deny, ReasonCodes.TenantMismatch),

            Scenario("break-glass-does-not-override-sod",
                "Break-glass never bypasses segregation of duties: a maker approving their own txn stays denied.",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Manager1, "Pending"),
                BreakGlassContext(
                    [ScopeNames.ApprovalsWrite],
                    BreakGlass(Manager1, ActionNames.TransactionApprove, Active)),
                Decision.Deny, ReasonCodes.MakerEqualsChecker),

            // ---- Break-glass must NOT elevate when a missing-capability deny MASKS an integrity
            // violation (regression for the ordering bug: EvaluateCore surfaces the FIRST failure, so a
            // missing scope can hide a co-occurring tenant/SoD violation). The deny must stand. ----
            Scenario("break-glass-missing-scope-masking-tenant-mismatch",
                "No read scope AND cross-tenant: the elevatable MissingScope masks the tenant violation, " +
                "but break-glass must not elevate — the deny stands.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Fabrikam),
                BreakGlassContext([], BreakGlass(Teller1, ActionNames.AccountRead, Active)),
                Decision.Deny, ReasonCodes.MissingScope),

            Scenario("break-glass-missing-scope-masking-sod",
                "No approvals scope AND self-approval: the elevatable MissingScope masks the SoD violation, " +
                "but break-glass must not elevate — the deny stands.",
                Manager(Manager1, Contoso), ActionNames.TransactionApprove,
                Transaction(Contoso, 15_000m, Manager1, "Pending"),
                BreakGlassContext([], BreakGlass(Manager1, ActionNames.TransactionApprove, Active)),
                Decision.Deny, ReasonCodes.MissingScope),

            // ---- Manager->delegate delegation ----
            Scenario("delegation-active-grant-permits",
                "A delegate holding the delegated scope acts for a manager under an active grant: permit.",
                DelegatedManager(Manager1, Contoso, Delegate1, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Contoso),
                DelegationContext([ScopeNames.Read], Delegation(Manager1, Delegate1, Active, AgentScopeNames.Read)),
                Decision.Permit, ReasonCodes.Permit),

            Scenario("delegation-expired-grant-denies",
                "An expired delegation grant denies even when the delegate holds the delegated scope.",
                DelegatedManager(Manager1, Contoso, Delegate1, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Contoso),
                DelegationContext([ScopeNames.Read], Delegation(Manager1, Delegate1, Expired, AgentScopeNames.Read)),
                Decision.Deny, ReasonCodes.DelegationNotActive),

            Scenario("delegation-mismatched-delegate-denies",
                "A delegation grant naming a different delegate denies (the grant must match the Actor).",
                DelegatedManager(Manager1, Contoso, Delegate1, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Contoso),
                DelegationContext([ScopeNames.Read], Delegation(Manager1, Stranger1, Active, AgentScopeNames.Read)),
                Decision.Deny, ReasonCodes.DelegationNotActive),

            // ---- The manager's grant Scopes bound the delegate even when its own token would allow ----
            Scenario("delegation-grant-scope-omits-required-denies",
                "An active grant whose Scopes omit the action's required scope denies even though the " +
                "delegate's own token holds it: the manager's grant bounds the delegate.",
                DelegatedManager(Manager1, Contoso, Delegate1, AgentScopeNames.Read),
                ActionNames.AccountRead, Account(Contoso),
                DelegationContext(
                    [ScopeNames.Read],
                    Delegation(Manager1, Delegate1, Active, AgentScopeNames.ApprovalsWrite)),
                Decision.Deny, ReasonCodes.DelegationScopeMissing),

            // ---- Control: the human / no-context path is unchanged ----
            Scenario("human-no-context-path-unchanged",
                "A plain human read with no grants and no injected clock behaves exactly as before.",
                Teller(Teller1, Contoso), ActionNames.AccountRead, Account(Contoso),
                new EvaluationContext([ScopeNames.Read]),
                Decision.Permit, ReasonCodes.Permit),
        ];
    }

    private static Subject Teller(string id, string tenant) =>
        new("user", id, [RoleNames.Teller], tenant);

    private static Subject Manager(string id, string tenant) =>
        new("user", id, [RoleNames.BranchManager], tenant);

    // A manager (human Subject) with a delegate Actor: the delegate carries the delegated capability
    // scopes it was granted, reusing the CS19 OBO Actor seam.
    private static Subject DelegatedManager(
        string managerId, string tenant, string delegateId, params string[] delegateScopes) =>
        new("user", managerId, [RoleNames.BranchManager], tenant,
            Actor: new Actor("user", delegateId, delegateScopes));

    private static BreakGlassGrant BreakGlass(string subjectId, string action, DateTimeOffset expiresAt) =>
        new($"bg-{subjectId}-{action}", subjectId, action, expiresAt, "Emergency access for incident.");

    private static DelegationGrant Delegation(
        string managerId, string delegateId, DateTimeOffset expiresAt, params string[] scopes) =>
        new($"del-{managerId}-{delegateId}", managerId, delegateId, expiresAt, scopes);

    private static EvaluationContext BreakGlassContext(string[] scopes, BreakGlassGrant grant) =>
        new(scopes, BreakGlass: grant, Now: Now);

    private static EvaluationContext DelegationContext(string[] scopes, DelegationGrant grant) =>
        new(scopes, Delegation: grant, Now: Now);

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
