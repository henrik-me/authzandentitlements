using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Sod;

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

    // Constrained delegation (CS19). The public entry point computes the base (human) decision
    // unchanged, then applies the on-behalf-of (OBO) constraint when the Subject carries an Actor:
    //
    //   * Actor == null            -> return the base decision byte-identical (direct human/service
    //                                 path; the "human path unaffected" guarantee).
    //   * Actor present, base Deny -> return the base Deny unchanged (the human is not permitted, so
    //                                 the agent — which can never exceed the human — is not either;
    //                                 the human reason is preserved so the denial is explained the
    //                                 same way for the agent as for the user).
    //   * Actor present, base Permit -> require the delegated scope for the action class. The agent
    //                                 is authorized only at the INTERSECTION of the human's rights
    //                                 and its own delegated scopes; a missing/unmapped delegated
    //                                 scope denies DelegationScopeMissing (fail-closed).
    public AccessDecision Evaluate(AccessRequest request)
    {
        var baseDecision = EvaluateCore(request);

        if (request.Subject.Actor is null)
        {
            return baseDecision;
        }

        if (baseDecision.Decision == Decision.Deny)
        {
            return baseDecision;
        }

        var actor = request.Subject.Actor;
        var required = AgentScopeNames.RequiredFor(request.Action.Name);

        // Fail-closed: an action with no defined delegated scope, or an agent whose granted scopes
        // do not include the required one, denies. A null/empty Scopes list satisfies nothing.
        var satisfied = required is not null
            && actor.Scopes is not null
            && actor.Scopes.Any(s => string.Equals(s, required, StringComparison.Ordinal));

        if (satisfied)
        {
            return baseDecision;
        }

        return Explain(AccessDecision.Deny(new Reason(
            ReasonCodes.DelegationScopeMissing,
            $"Agent '{actor.Id}' is not authorized to '{request.Action.Name}' on behalf of subject " +
            $"'{request.Subject.Id}': missing delegated scope '{required ?? "(none defined)"}'.")));
    }

    private AccessDecision EvaluateCore(AccessRequest request) => request.Action.Name switch
    {
        ActionNames.AccountRead => EvaluateRead(request),
        ActionNames.AccountCreate => EvaluateAccountCreate(request),
        ActionNames.TransactionCreate => EvaluateTransactionCreate(request),
        ActionNames.TransactionApprove or ActionNames.TransactionReject =>
            EvaluateApprovalDecision(request),
        ActionNames.GovernanceAccessRequest => EvaluateGovernanceAccessRequest(request),
        // Fail closed: an action outside the known vocabulary is denied, never permitted.
        _ => Explain(AccessDecision.Deny(new Reason(
            ReasonCodes.UnknownAction,
            $"Action '{request.Action.Name}' is not a recognized bank action."))),
    };

    // Reads require the read scope and same-tenant access; no role gate (mirrors the
    // ScopeReadPolicy endpoints, which any authenticated same-tenant caller may hit).
    private static AccessDecision EvaluateRead(AccessRequest request)
    {
        if (!HasScope(request, ScopeNames.Read))
        {
            return Explain(MissingScope(ScopeNames.Read));
        }

        if (!TenantMatches(request))
        {
            return Explain(TenantMismatch());
        }

        return Explain(Permitted());
    }

    // Creating an account is gated to BranchManager within the caller's own tenant.
    private static AccessDecision EvaluateAccountCreate(AccessRequest request)
    {
        if (!HasRole(request, RoleNames.BranchManager))
        {
            return Explain(RoleNotAuthorized(
                $"Creating an account requires the {RoleNames.BranchManager} role."));
        }

        if (!TenantMatches(request))
        {
            return Explain(TenantMismatch());
        }

        return Explain(Permitted());
    }

    // Creating a transaction requires the write scope, a maker-eligible role, the caller
    // acting as themselves (subject == maker), and same-tenant access. On permit it
    // carries the threshold obligation the domain would otherwise apply.
    private static AccessDecision EvaluateTransactionCreate(AccessRequest request)
    {
        if (!HasScope(request, ScopeNames.TransactionsWrite))
        {
            return Explain(MissingScope(ScopeNames.TransactionsWrite));
        }

        if (!HasAnyRole(request, MakerEligibleRoles))
        {
            return Explain(RoleNotAuthorized(
                $"Creating a transaction requires one of: {Join(MakerEligibleRoles)}."));
        }

        if (!SubjectIsMaker(request))
        {
            return Explain(AccessDecision.Deny(new Reason(
                ReasonCodes.SubjectNotMaker,
                "A caller may only create a transaction as themselves (subject must be the maker).")));
        }

        if (!TenantMatches(request))
        {
            return Explain(TenantMismatch());
        }

        var amount = request.Resource.Amount ?? 0m;
        var obligation = amount >= ApprovalThreshold
            ? new Obligation(ObligationIds.RequireApproval)
            : new Obligation(ObligationIds.PostImmediately);

        return Explain(AccessDecision.Permit(PermitReason(), obligation));
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
            return Explain(MissingScope(ScopeNames.ApprovalsWrite));
        }

        if (!HasAnyRole(request, CheckerEligibleRoles))
        {
            return Explain(RoleNotAuthorized(
                $"Deciding an approval requires one of: {Join(CheckerEligibleRoles)}."));
        }

        if (!TenantMatches(request))
        {
            return Explain(TenantMismatch());
        }

        if (!IsPending(request))
        {
            return Explain(AccessDecision.Deny(new Reason(
                ReasonCodes.NotPending,
                "Only a pending transaction can be approved or rejected.")));
        }

        if (SubjectIsMaker(request))
        {
            return Explain(AccessDecision.Deny(new Reason(
                ReasonCodes.MakerEqualsChecker,
                "Segregation of duties: the checker may not be the maker of the transaction.")));
        }

        return Explain(Permitted());
    }

    // Attaches a "reference"-engine DecisionExplanation to every decision (CS16). The reference
    // engine owns its own role set (no IEngineRoleAuthorizer), so it surfaces a single normalized
    // pipeline-rule PolicyReference — including for role denials — derived from the decision's
    // primary reason, mirroring the rule ids FintechRuleEvaluator uses so reference and adapter
    // explanations compare cleanly. Additive only: decision, reasons, and obligations are untouched.
    private static AccessDecision Explain(AccessDecision decision)
    {
        var reason = decision.Reasons[0];
        var rule = DecisionExplanations.RuleForReason(reason.Code);
        return decision.WithExplanation(new DecisionExplanation(
            Engine: "reference",
            DeterminingRule: rule,
            PolicyReferences: [new PolicyReference(PolicyReferenceKinds.Rule, rule)],
            Narrative: reason.Message));
    }

    // governance.access.request: a pure segregation-of-duties check over the PROPOSED resulting
    // role set carried on subject.roles. Unlike the bank actions it has NO tenant/scope/maker gate
    // — it asks only whether the role set is internally incompatible. A toxic combination denies
    // SodConflict; an independent set permits with no obligation. Encodes the SAME rule as the
    // OPA/Rego policy (GovernanceSodPolicy mirrors authz.rego), so the reference and OPA engines
    // return the same verdict.
    private static AccessDecision EvaluateGovernanceAccessRequest(AccessRequest request)
    {
        if (GovernanceSodPolicy.FindConflict(request.Subject.Roles) is { } pair)
        {
            return Explain(AccessDecision.Deny(new Reason(
                GovernanceSodPolicy.SodConflictReasonCode,
                $"Segregation of duties: the roles '{pair.First}' and '{pair.Second}' " +
                "may not be held together.")));
        }

        return Explain(Permitted());
    }

    private static bool HasScope(AccessRequest request, string scope) =>
        request.Context.Scopes.Any(s => string.Equals(s, scope, StringComparison.Ordinal));

    private static bool HasRole(AccessRequest request, string role) =>
        request.Subject.Roles.Any(r => string.Equals(r, role, StringComparison.Ordinal));

    private static bool HasAnyRole(AccessRequest request, HashSet<string> eligible) =>
        request.Subject.Roles.Any(eligible.Contains);

    // Fail closed on tenant: a missing OR whitespace-only tenant on either side is treated as a
    // mismatch (a blank string is not a real tenant), mirroring Bank.Api's fail-closed
    // token-tenant check.
    private static bool TenantMatches(AccessRequest request) =>
        !string.IsNullOrWhiteSpace(request.Subject.Tenant)
        && !string.IsNullOrWhiteSpace(request.Resource.Tenant)
        && string.Equals(request.Subject.Tenant, request.Resource.Tenant, StringComparison.Ordinal);

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
