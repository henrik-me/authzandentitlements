using AuthzEntitlements.Governance.Service.Domain;
using AuthzEntitlements.Governance.Service.Sod;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// The approval service runs the two SoD gates in order — maker-checker on the approval
// action, then the PDP SoD check on the proposed role set — and issues the grant only on a
// permit (items e + f). It must fail closed on Unavailable and never grant on a deny.
public sealed class AccessApprovalServiceTests
{
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;

    [Fact]
    public async Task Evaluate_ApproverEqualsRequester_IsMakerCheckerDenied_WithoutCallingPdp()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Permit };
        var outcome = await Evaluate(fake, requesterId: "user-compliance1", approverId: "user-compliance1");

        Assert.Equal(ApprovalDisposition.MakerCheckerDenied, outcome.Disposition);
        Assert.Null(outcome.Grant);
        // The PDP is never consulted once maker-checker fails.
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Evaluate_SodDeny_IsRejected_WithNoGrant()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Deny("SodConflict", "conflict") };
        var outcome = await Evaluate(fake, requesterId: "user-teller1", approverId: "user-manager1");

        Assert.Equal(ApprovalDisposition.SodDenied, outcome.Disposition);
        Assert.Equal("SodConflict", outcome.ReasonCode);
        Assert.Null(outcome.Grant);
    }

    [Fact]
    public async Task Evaluate_SodUnavailable_IsUnavailable_WithNoGrant()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Unavailable("pdp down") };
        var outcome = await Evaluate(fake, requesterId: "user-teller1", approverId: "user-manager1");

        // Fail closed: an unreachable PDP must never yield an approval.
        Assert.Equal(ApprovalDisposition.SodUnavailable, outcome.Disposition);
        Assert.Null(outcome.Grant);
    }

    [Fact]
    public async Task Evaluate_Permit_IsApproved_WithGrantAndPackageDefaultExpiry()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Permit };
        var outcome = await Evaluate(fake, requesterId: "user-compliance1", approverId: "user-manager1");

        Assert.Equal(ApprovalDisposition.Approved, outcome.Disposition);
        var grant = Assert.IsType<AccessGrant>(outcome.Grant);
        Assert.Equal(Now.AddMinutes(480), grant.ExpiresAt);
        Assert.Equal(["BranchManager", "ComplianceOfficer"], grant.Roles.Select(r => r.RoleName));
    }

    [Fact]
    public async Task Evaluate_Permit_WithRequestedOverride_ShortensExpiry()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Permit };
        var request = GovernanceTestData.Request(
            "user-compliance1", GovernanceTestData.Contoso, "quarter-end-close", requestedMinutes: 60);

        var outcome = await Evaluate(fake, request, approverId: "user-manager1");

        var grant = Assert.IsType<AccessGrant>(outcome.Grant);
        Assert.Equal(Now.AddMinutes(60), grant.ExpiresAt);
    }

    [Fact]
    public async Task Evaluate_ProposedRoles_AreBaselineUnionPackageRoles()
    {
        var fake = new FakePdpSodClient { Result = SodCheckResult.Permit };
        await Evaluate(fake, requesterId: "user-compliance1", approverId: "user-manager1");

        // Baseline {ComplianceOfficer} UNION package {BranchManager, ComplianceOfficer}.
        Assert.NotNull(fake.LastProposedRoles);
        Assert.Equal(["BranchManager", "ComplianceOfficer"], fake.LastProposedRoles!);
        Assert.Equal("quarter-end-close", fake.LastPackageCode);
        Assert.Equal(GovernanceTestData.Contoso, fake.LastTenantCode);
    }

    private static Task<ApprovalOutcome> Evaluate(
        FakePdpSodClient fake, string requesterId, string approverId)
    {
        var request = GovernanceTestData.Request(requesterId, GovernanceTestData.Contoso, "quarter-end-close");
        return Evaluate(fake, request, approverId);
    }

    private static Task<ApprovalOutcome> Evaluate(
        FakePdpSodClient fake, AccessGrantRequest request, string approverId)
    {
        var principal = GovernanceTestData.Principal(request.PrincipalId, GovernanceTestData.Contoso, "ComplianceOfficer");
        var package = GovernanceTestData.Package("quarter-end-close", 480, "BranchManager", "ComplianceOfficer");
        var service = new AccessApprovalService(fake);
        return service.EvaluateAsync(request, principal, package, approverId, Now, CancellationToken.None);
    }

    private sealed class FakePdpSodClient : IPdpSodClient
    {
        public SodCheckResult Result { get; init; } = SodCheckResult.Permit;
        public int Calls { get; private set; }
        public string[]? LastProposedRoles { get; private set; }
        public string? LastPrincipalId { get; private set; }
        public string? LastTenantCode { get; private set; }
        public string? LastPackageCode { get; private set; }

        public Task<SodCheckResult> EvaluateAsync(
            string principalId,
            string tenantCode,
            IReadOnlyCollection<string> proposedRoles,
            string accessPackageCode,
            CancellationToken ct)
        {
            Calls++;
            LastPrincipalId = principalId;
            LastTenantCode = tenantCode;
            LastProposedRoles = proposedRoles.ToArray();
            LastPackageCode = accessPackageCode;
            return Task.FromResult(Result);
        }
    }
}
