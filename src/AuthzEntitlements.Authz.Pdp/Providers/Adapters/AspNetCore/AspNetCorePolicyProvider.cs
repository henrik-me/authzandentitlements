using System.Security.Claims;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.AspNetCore;

// Engine adapter that answers the single role-eligibility question via genuine ASP.NET Core
// policy-based authorization. Each role-gated action maps to a RolesAuthorizationRequirement —
// the exact type behind [Authorize(Roles=...)] / RequireRole — evaluated over an
// AuthorizationHandlerContext built from the subject's role claims. The shared
// FintechRuleEvaluator composes the full fintech ABAC decision (scope, tenant isolation,
// subject-is-maker, pending, segregation of duties, the approval-threshold obligation, and the
// fail-closed unknown-action deny) on top of this engine-owned role gate, so the adapter stays
// in lock-step parity with the reference provider while the *engine* owns role eligibility.
public sealed class AspNetCorePolicyProvider : IAuthorizationDecisionProvider, IEngineRoleAuthorizer
{
    // Role-gated action -> the ASP.NET requirement carrying its eligible roles. Actions absent
    // here (e.g. bank.account.read) are not role-gated and never reach IsRoleAuthorized; a
    // missing action therefore fails closed (false).
    private static readonly IReadOnlyDictionary<string, RolesAuthorizationRequirement> Requirements =
        new Dictionary<string, RolesAuthorizationRequirement>(StringComparer.Ordinal)
        {
            [ActionNames.AccountCreate] =
                new RolesAuthorizationRequirement([RoleNames.BranchManager]),
            [ActionNames.TransactionCreate] =
                new RolesAuthorizationRequirement(
                    [RoleNames.Teller, RoleNames.BranchManager, RoleNames.ComplianceOfficer]),
            [ActionNames.TransactionApprove] =
                new RolesAuthorizationRequirement(
                    [RoleNames.BranchManager, RoleNames.ComplianceOfficer]),
            [ActionNames.TransactionReject] =
                new RolesAuthorizationRequirement(
                    [RoleNames.BranchManager, RoleNames.ComplianceOfficer]),
        };

    public string Name => "aspnet";

    public string EngineName => "aspnet";

    public AccessDecision Evaluate(AccessRequest request) =>
        FintechRuleEvaluator.Evaluate(request, this);

    // Genuine ASP.NET Core policy-based role authorization: builds a principal from the subject's
    // roles and asks the action's RolesAuthorizationRequirement to decide. RolesAuthorizationRequirement
    // is its own synchronous handler (no I/O), so evaluating it here is fully deterministic.
    public bool IsRoleAuthorized(string action, IReadOnlyList<string> subjectRoles)
    {
        if (!Requirements.TryGetValue(action, out var requirement))
        {
            return false;
        }

        var identity = new ClaimsIdentity(authenticationType: "pdp");
        foreach (var role in subjectRoles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(identity), resource: null);
        requirement.HandleAsync(context).GetAwaiter().GetResult();
        return context.HasSucceeded;
    }

    // The engine-native role artifact for CS16 explanations: the action's
    // RolesAuthorizationRequirement rendered with its eligible roles — the exact requirement
    // [Authorize(Roles=...)] evaluates — plus the subject's roles versus the required set. An
    // action with no requirement (never role-gated) yields an empty requirement reference.
    public PolicyReference DescribeRoleRule(string action, IReadOnlyList<string> subjectRoles)
    {
        if (!Requirements.TryGetValue(action, out var requirement))
        {
            return new PolicyReference(
                PolicyReferenceKinds.AspNetRequirement,
                "RolesAuthorizationRequirement[]",
                $"Action '{action}' has no role requirement.");
        }

        var eligible = requirement.AllowedRoles.ToList();
        return new PolicyReference(
            PolicyReferenceKinds.AspNetRequirement,
            $"RolesAuthorizationRequirement[{string.Join(", ", eligible)}]",
            $"Subject roles [{string.Join(", ", subjectRoles)}] vs required [{string.Join(", ", eligible)}].");
    }
}
