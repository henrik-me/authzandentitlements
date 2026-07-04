using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the JIT ACCESS-REQUEST (governance) page model. No server, Docker,
// or Keycloak required — they exercise the pending filter, the segregation-of-duties
// labelling, the create-request-body builder (which binds the principal from the signed-in
// identity, never a form field), and the outcome labelling that surfaces the fail-closed /
// SoD / decide-once semantics the server enforces.
public class AccessRequestsModelTests
{
    private static AccessRequestResponse Request(string status, string sodOutcome = "NotEvaluated") =>
        new(
            Id: Guid.NewGuid(),
            PrincipalId: "user-teller1",
            TenantCode: "CONTOSO",
            AccessPackageCode: "branch-approver",
            Justification: "cover for the branch manager",
            RequestedDurationMinutes: null,
            Status: status,
            SodOutcome: sodOutcome,
            SodReason: null,
            RequestedAt: DateTimeOffset.UnixEpoch,
            DecidedBy: null,
            DecidedAt: null);

    [Fact]
    public void Pending_keeps_only_requests_with_status_Pending()
    {
        var pending = Request("Pending");
        var reqs = new[]
        {
            pending,
            Request("Approved"),
            Request("Rejected"),
            Request("Expired"),
            Request("Revoked"),
        };

        var result = AccessRequestsModel.Pending(reqs);

        Assert.Single(result);
        Assert.Equal(pending.Id, result[0].Id);
    }

    [Fact]
    public void Pending_is_empty_for_no_input()
    {
        Assert.Empty(AccessRequestsModel.Pending([]));
    }

    [Theory]
    [InlineData("Pending", true)]
    [InlineData("Approved", false)]
    public void IsPending_reflects_status(string status, bool expected)
    {
        Assert.Equal(expected, AccessRequestsModel.IsPending(Request(status)));
    }

    [Theory]
    [InlineData("Permit", "Allowed (segregation-of-duties check passed)")]
    [InlineData("Allowed", "Allowed (segregation-of-duties check passed)")]
    [InlineData("Deny", "Denied (segregation-of-duties conflict)")]
    [InlineData("Denied", "Denied (segregation-of-duties conflict)")]
    [InlineData("Unavailable", "Unavailable (PDP unreachable — fail-closed, request stays Pending)")]
    [InlineData("NotEvaluated", "Not yet evaluated (pending a decision)")]
    public void SodOutcomeLabel_maps_known_outcomes(string outcome, string expected)
    {
        Assert.Equal(expected, AccessRequestsModel.SodOutcomeLabel(outcome));
    }

    [Fact]
    public void SodOutcomeLabel_falls_back_to_the_raw_value_for_unknown_or_empty()
    {
        Assert.Equal("Weird", AccessRequestsModel.SodOutcomeLabel("Weird"));
        Assert.Equal("—", AccessRequestsModel.SodOutcomeLabel(""));
    }

    [Fact]
    public void BuildCreateBody_binds_the_principal_and_trims_fields()
    {
        var input = new RequestAccessInput
        {
            AccessPackageCode = " branch-approver ",
            Justification = "  cover for the manager  ",
            RequestedDurationMinutes = 90,
        };

        var body = AccessRequestsModel.BuildCreateBody("user-manager1", input);

        Assert.Equal("user-manager1", body.PrincipalId);
        Assert.Equal("branch-approver", body.AccessPackageCode);
        Assert.Equal("cover for the manager", body.Justification);
        Assert.Equal(90, body.RequestedDurationMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(null)]
    public void BuildCreateBody_collapses_non_positive_duration_to_null(int? duration)
    {
        var input = new RequestAccessInput
        {
            AccessPackageCode = "treasury-oversight",
            Justification = "oversight",
            RequestedDurationMinutes = duration,
        };

        var body = AccessRequestsModel.BuildCreateBody("user-compliance1", input);

        Assert.Null(body.RequestedDurationMinutes);
    }

    [Theory]
    [InlineData(200, "OK (decision recorded)")]
    [InlineData(201, "Created (request submitted)")]
    [InlineData(400, "400 Bad Request (missing justification or invalid input)")]
    [InlineData(403, "403 Forbidden (segregation of duties, or ineligible approver)")]
    [InlineData(404, "404 Not Found (unknown principal, package, or request)")]
    [InlineData(409, "409 Conflict (segregation of duties, or already decided)")]
    [InlineData(503, "503 Service Unavailable (PDP unreachable — fail-closed)")]
    public void RequestOutcomeLabel_maps_known_status_codes(int statusCode, string expected)
    {
        Assert.Equal(expected, AccessRequestsModel.RequestOutcomeLabel(statusCode));
    }

    [Fact]
    public void RequestOutcomeLabel_falls_back_to_the_raw_code_for_unknown_status()
    {
        Assert.Equal("418", AccessRequestsModel.RequestOutcomeLabel(418));
    }
}
