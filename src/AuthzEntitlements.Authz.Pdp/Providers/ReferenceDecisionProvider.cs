using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers;

// The in-process baseline engine: a pure, deterministic function of the AccessRequest
// that mirrors the Bank.Api enforcement rules exactly — coarse scopes, role eligibility,
// the 10,000 maker-checker threshold, tenant isolation, and segregation of duties. It is
// the reference the CS06-CS09 adapters are compared against, so it encodes the rules
// locally (constants below) rather than depending on Bank.Api.
public sealed class ReferenceDecisionProvider : IAuthorizationDecisionProvider
{
    // Mirrors BankPolicy.ApprovalThreshold: at/above this, a created transaction obliges a
    // second-person approval; below it, it may post immediately.
    private const decimal ApprovalThreshold = 10_000m;

    // "Pending" is the only transaction status a maker-checker decision may act on;
    // mirrors TransactionStatus.Pending.
    private const string PendingStatus = "Pending";

    // Mirrors AuthorizationSetup.MakerEligibleRoles — who may originate a transaction.
    private static readonly HashSet<string> MakerEligibleRoles =
        new(StringComparer.Ordinal)
        {
            RoleNames.Teller,
            RoleNames.BranchManager,
            RoleNames.ComplianceOfficer,
        };

    // Mirrors RoleNames.CheckerEligibleRoles — who may decide (check) an approval.
    private static readonly HashSet<string> CheckerEligibleRoles =
        new(StringComparer.Ordinal)
        {
            RoleNames.BranchManager,
            RoleNames.ComplianceOfficer,
        };

    public string Name => "reference";

    public AccessDecision Evaluate(AccessRequest request) => request.Action.Name switch
    {
        ActionNames.AccountRead => EvaluateRead(request),
        ActionNames.AccountCreate => EvaluateAccountCreate(request),
        ActionNames.TransactionCreate => EvaluateTransactionCreate(request),
        ActionNames.TransactionApprove or ActionNames.TransactionReject =>
            EvaluateApprovalDecision(request),
        // Fail closed: an action outside the known vocabulary is denied, never permitted.
        _ => AccessDecision.Deny(new Reason(
            ReasonCodes.UnknownAction,
            $"Action '{request.Action.Name}' is not a recognized bank action.")),
    };

    // Reads require the read scope and same-tenant access; no role gate (mirrors the
    // ScopeReadPolicy endpoints, which any authenticated same-tenant caller may hit).
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

    // Creating an account is gated to BranchManager within the caller's own tenant.
    private static AccessDecision EvaluateAccountCreate(AccessRequest request)
    {
        if (!HasRole(request, RoleNames.BranchManager))
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

    // Creating a transaction requires the write scope, a maker-eligible role, the caller
    // acting as themselves (subject == maker), and same-tenant access. On permit it
    // carries the threshold obligation the domain would otherwise apply.
    private static AccessDecision EvaluateTransactionCreate(AccessRequest request)
    {
        if (!HasScope(request, ScopeNames.TransactionsWrite))
        {
            return MissingScope(ScopeNames.TransactionsWrite);
        }

        if (!HasAnyRole(request, MakerEligibleRoles))
        {
            return RoleNotAuthorized(
                $"Creating a transaction requires one of: {Join(MakerEligibleRoles)}.");
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

    // Approving/rejecting requires the approvals scope, a checker-eligible role, same-tenant
    // access, a pending target, and segregation of duties (checker != maker). Pending is
    // checked BEFORE SoD to mirror Bank.Api's Approval.Decide, which rejects an already-decided
    // approval before the maker==checker check — so a self-approval of an already-decided
    // transaction denies NotPending, exactly as the enforced domain rule does.
    private static AccessDecision EvaluateApprovalDecision(AccessRequest request)
    {
        if (!HasScope(request, ScopeNames.ApprovalsWrite))
        {
            return MissingScope(ScopeNames.ApprovalsWrite);
        }

        if (!HasAnyRole(request, CheckerEligibleRoles))
        {
            return RoleNotAuthorized(
                $"Deciding an approval requires one of: {Join(CheckerEligibleRoles)}.");
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

    private static bool HasRole(AccessRequest request, string role) =>
        request.Subject.Roles.Any(r => string.Equals(r, role, StringComparison.Ordinal));

    private static bool HasAnyRole(AccessRequest request, HashSet<string> eligible) =>
        request.Subject.Roles.Any(eligible.Contains);

    // Fail closed on tenant: a missing subject or resource tenant is treated as a
    // mismatch, mirroring Bank.Api's fail-closed token-tenant check.
    private static bool TenantMatches(AccessRequest request) =>
        request.Subject.Tenant is { Length: > 0 } subjectTenant
        && request.Resource.Tenant is { Length: > 0 } resourceTenant
        && string.Equals(subjectTenant, resourceTenant, StringComparison.Ordinal);

    private static bool SubjectIsMaker(AccessRequest request) =>
        request.Resource.MakerId is { Length: > 0 } makerId
        && string.Equals(request.Subject.Id, makerId, StringComparison.Ordinal);

    private static bool IsPending(AccessRequest request) =>
        string.Equals(request.Resource.Status, PendingStatus, StringComparison.Ordinal);

    private static string Join(IEnumerable<string> roles) =>
        string.Join(", ", roles.OrderBy(r => r, StringComparer.Ordinal));

    private static AccessDecision Permitted() => AccessDecision.Permit(PermitReason());

    private static Reason PermitReason() =>
        new(ReasonCodes.Permit, "Request satisfies all applicable rules.");

    private static AccessDecision MissingScope(string scope) =>
        AccessDecision.Deny(new Reason(
            ReasonCodes.MissingScope, $"Requires the '{scope}' scope."));

    private static AccessDecision TenantMismatch() =>
        AccessDecision.Deny(new Reason(
            ReasonCodes.TenantMismatch,
            "The subject's tenant does not match the resource's tenant."));

    private static AccessDecision RoleNotAuthorized(string message) =>
        AccessDecision.Deny(new Reason(ReasonCodes.RoleNotAuthorized, message));
}
