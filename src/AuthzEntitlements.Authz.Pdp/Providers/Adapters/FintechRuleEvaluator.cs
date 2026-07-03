using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters;

// The shared fintech decision pipeline the CS06-CS09 engine adapters run. It encodes the
// exact same ordered rules as the CS05 ReferenceDecisionProvider — coarse-scope re-check,
// the 10,000 maker-checker threshold, tenant isolation, subject-is-maker, pending status,
// and segregation of duties — and returns the first failing rule's reason code, so every
// adapter answers the shared scenario catalog identically to the reference. The one rule an
// adapter's *engine* owns, role eligibility, is delegated to IEngineRoleAuthorizer; every
// other (ABAC) rule is engine-agnostic domain logic and lives here so all adapters stay in
// lock-step parity with the reference. Deny is fail-closed; the reason CODE (Reasons[0]) is
// the contract the catalog + audit key on (human messages may vary between engines).
public static class FintechRuleEvaluator
{
    // Mirrors ReferenceDecisionProvider.ApprovalThreshold: at/above this a created transaction
    // obliges a second-person approval; below it, it may post immediately.
    private const decimal ApprovalThreshold = 10_000m;

    // The only transaction status an approve/reject decision may act on (mirrors Pending).
    private const string PendingStatus = "Pending";

    public static AccessDecision Evaluate(AccessRequest request, IEngineRoleAuthorizer roleAuthorizer) =>
        request.Action.Name switch
        {
            ActionNames.AccountRead => EvaluateRead(request),
            ActionNames.AccountCreate => EvaluateAccountCreate(request, roleAuthorizer),
            ActionNames.TransactionCreate => EvaluateTransactionCreate(request, roleAuthorizer),
            ActionNames.TransactionApprove or ActionNames.TransactionReject =>
                EvaluateApprovalDecision(request, roleAuthorizer),
            // Fail closed: an action outside the known vocabulary is denied, never permitted.
            _ => AccessDecision.Deny(new Reason(
                ReasonCodes.UnknownAction,
                $"Action '{request.Action.Name}' is not a recognized bank action.")),
        };

    // read: scope -> tenant. No role gate (any authenticated same-tenant caller may read).
    private static AccessDecision EvaluateRead(AccessRequest request)
    {
        if (!HasScope(request, ScopeNames.Read))
        {
            return MissingScope(ScopeNames.Read);
        }

        if (!TenantMatches(request))
        {
            return TenantMismatch();
        }

        return Permitted();
    }

    // account.create: role (engine) -> tenant. No scope check — creation is role-gated at the
    // service, matching the reference and the coarse-vs-fine boundary doc.
    private static AccessDecision EvaluateAccountCreate(
        AccessRequest request, IEngineRoleAuthorizer roleAuthorizer)
    {
        if (!roleAuthorizer.IsRoleAuthorized(request.Action.Name, request.Subject.Roles))
        {
            return RoleNotAuthorized(
                $"Creating an account requires the {RoleNames.BranchManager} role.");
        }

        if (!TenantMatches(request))
        {
            return TenantMismatch();
        }

        return Permitted();
    }

    // transaction.create: scope -> role (engine) -> subject-is-maker -> tenant -> permit with
    // the threshold obligation.
    private static AccessDecision EvaluateTransactionCreate(
        AccessRequest request, IEngineRoleAuthorizer roleAuthorizer)
    {
        if (!HasScope(request, ScopeNames.TransactionsWrite))
        {
            return MissingScope(ScopeNames.TransactionsWrite);
        }

        if (!roleAuthorizer.IsRoleAuthorized(request.Action.Name, request.Subject.Roles))
        {
            return RoleNotAuthorized("Creating a transaction requires a maker-eligible role.");
        }

        if (!SubjectIsMaker(request))
        {
            return AccessDecision.Deny(new Reason(
                ReasonCodes.SubjectNotMaker,
                "A caller may only create a transaction as themselves (subject must be the maker)."));
        }

        if (!TenantMatches(request))
        {
            return TenantMismatch();
        }

        var amount = request.Resource.Amount ?? 0m;
        var obligation = amount >= ApprovalThreshold
            ? new Obligation(ObligationIds.RequireApproval)
            : new Obligation(ObligationIds.PostImmediately);

        return AccessDecision.Permit(PermitReason(), obligation);
    }

    // approve/reject: scope -> role (engine) -> tenant -> pending -> segregation of duties.
    // Pending is checked BEFORE SoD to mirror the reference / Bank.Api's Approval.Decide, so a
    // self-approval of an already-decided transaction denies NotPending, not MakerEqualsChecker.
    private static AccessDecision EvaluateApprovalDecision(
        AccessRequest request, IEngineRoleAuthorizer roleAuthorizer)
    {
        if (!HasScope(request, ScopeNames.ApprovalsWrite))
        {
            return MissingScope(ScopeNames.ApprovalsWrite);
        }

        if (!roleAuthorizer.IsRoleAuthorized(request.Action.Name, request.Subject.Roles))
        {
            return RoleNotAuthorized("Deciding an approval requires a checker-eligible role.");
        }

        if (!TenantMatches(request))
        {
            return TenantMismatch();
        }

        if (!IsPending(request))
        {
            return AccessDecision.Deny(new Reason(
                ReasonCodes.NotPending,
                "Only a pending transaction can be approved or rejected."));
        }

        if (SubjectIsMaker(request))
        {
            return AccessDecision.Deny(new Reason(
                ReasonCodes.MakerEqualsChecker,
                "Segregation of duties: the checker may not be the maker of the transaction."));
        }

        return Permitted();
    }

    private static bool HasScope(AccessRequest request, string scope) =>
        request.Context.Scopes.Any(s => string.Equals(s, scope, StringComparison.Ordinal));

    // Fail closed on tenant: a missing OR whitespace-only tenant on either side is a mismatch.
    private static bool TenantMatches(AccessRequest request) =>
        !string.IsNullOrWhiteSpace(request.Subject.Tenant)
        && !string.IsNullOrWhiteSpace(request.Resource.Tenant)
        && string.Equals(request.Subject.Tenant, request.Resource.Tenant, StringComparison.Ordinal);

    private static bool SubjectIsMaker(AccessRequest request) =>
        request.Resource.MakerId is { Length: > 0 } makerId
        && string.Equals(request.Subject.Id, makerId, StringComparison.Ordinal);

    private static bool IsPending(AccessRequest request) =>
        string.Equals(request.Resource.Status, PendingStatus, StringComparison.Ordinal);

    private static AccessDecision Permitted() => AccessDecision.Permit(PermitReason());

    private static Reason PermitReason() =>
        new(ReasonCodes.Permit, "Request satisfies all applicable rules.");

    private static AccessDecision MissingScope(string scope) =>
        AccessDecision.Deny(new Reason(ReasonCodes.MissingScope, $"Requires the '{scope}' scope."));

    private static AccessDecision TenantMismatch() =>
        AccessDecision.Deny(new Reason(
            ReasonCodes.TenantMismatch,
            "The subject's tenant does not match the resource's tenant."));

    private static AccessDecision RoleNotAuthorized(string message) =>
        AccessDecision.Deny(new Reason(ReasonCodes.RoleNotAuthorized, message));
}
