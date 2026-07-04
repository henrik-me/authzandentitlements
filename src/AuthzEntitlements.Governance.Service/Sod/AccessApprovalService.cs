using AuthzEntitlements.Governance.Service.Domain;

namespace AuthzEntitlements.Governance.Service.Sod;

// What the approval of an access request resolves to. Only Approved carries a Grant.
public enum ApprovalDisposition
{
    MakerCheckerDenied,
    SodDenied,
    SodUnavailable,
    Approved,
}

// The result of evaluating an approval: the disposition, a stable reason code + message,
// and (only when Approved) the AccessGrant to persist.
public sealed record ApprovalOutcome(
    ApprovalDisposition Disposition,
    string ReasonCode,
    string Message,
    AccessGrant? Grant);

// Pure decision service for approving an access request. It orchestrates the two
// segregation-of-duties gates in order — maker-checker on the approval action, then the
// PDP SoD check on the proposed role set — and constructs the grant on a permit. Its only
// dependency is the injected IPdpSodClient, so (like Bank.Api's EntitlementsEnforcer) it
// is directly testable with a fake client and never touches a DbContext or HTTP host.
//
// Fail-closed: an Unavailable SoD result yields SodUnavailable (the endpoint maps it to a
// 503 and leaves the request Pending), never an approval.
public sealed class AccessApprovalService(IPdpSodClient sod)
{
    public async Task<ApprovalOutcome> EvaluateAsync(
        AccessGrantRequest request,
        Principal principal,
        AccessPackage package,
        string approverId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // 1. Maker-checker: the approver must differ from the requester, so a principal
        // cannot approve their own elevation. Denies without ever consulting the PDP.
        if (!GovernanceRules.CheckerDiffersFromRequester(request.PrincipalId, approverId))
        {
            return new ApprovalOutcome(
                ApprovalDisposition.MakerCheckerDenied,
                "MakerEqualsChecker",
                "the checker (approver) must differ from the requester",
                null);
        }

        // 2. SoD via the PDP over the proposed role set (baseline UNION package roles).
        var proposedRoles = ProposedRoleSet.Compute(
            principal.BaselineRoles.Select(r => r.RoleName),
            package.Roles.Select(r => r.RoleName));

        var sodResult = await sod.EvaluateAsync(
            request.PrincipalId, request.TenantCode, proposedRoles, package.Code, ct);

        return sodResult.Status switch
        {
            SodStatus.Deny => new ApprovalOutcome(
                ApprovalDisposition.SodDenied,
                sodResult.ReasonCode ?? "SodConflict",
                sodResult.ReasonMessage ?? "the proposed roles violate a segregation-of-duties policy",
                null),

            SodStatus.Unavailable => new ApprovalOutcome(
                ApprovalDisposition.SodUnavailable,
                sodResult.ReasonCode ?? SodCheckResult.UnavailableCode,
                sodResult.ReasonMessage ?? "the PDP is unavailable",
                null),

            _ => new ApprovalOutcome(
                ApprovalDisposition.Approved,
                "Permit",
                "approved",
                AccessGrantFactory.Create(request, package, now)),
        };
    }
}
