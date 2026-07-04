using System.Security.Claims;
using AuthzEntitlements.Edge.Gateway.Auth;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Offline unit tests for the gateway's actor / on-behalf-of (OBO) claim reader — the coarse mirror
// of Bank.Api's ActorClaims, independent of the domain API. They assert the same fail-closed
// contract: absent subject_type is the human default, a non-human is recognised, a blank/absent
// on_behalf_of is NOT a delegation, and TryGetDelegation succeeds only for a non-human carrying a
// non-blank OBO.
public sealed class GatewayActorClaimsTests
{
    private static ClaimsPrincipal Principal(
        string? subjectType = null, string? onBehalfOf = null, string? sub = null)
    {
        var claims = new List<Claim>();
        if (subjectType is not null)
        {
            claims.Add(new Claim(GatewayActorClaims.SubjectTypeClaim, subjectType));
        }

        if (onBehalfOf is not null)
        {
            claims.Add(new Claim(GatewayActorClaims.OnBehalfOfClaim, onBehalfOf));
        }

        if (sub is not null)
        {
            claims.Add(new Claim(GatewayActorClaims.SubjectClaim, sub));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public void Absent_subject_type_defaults_to_user_and_is_not_non_human()
    {
        var principal = Principal();

        Assert.Equal("user", principal.GetSubjectType());
        Assert.False(principal.IsNonHuman());
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("service")]
    [InlineData("Service")]
    public void Non_user_subject_type_is_non_human(string subjectType)
    {
        var principal = Principal(subjectType: subjectType);

        Assert.True(principal.IsNonHuman());
    }

    [Fact]
    public void GetOnBehalfOf_is_null_when_absent_or_blank()
    {
        Assert.Null(Principal().GetOnBehalfOf());
        Assert.Null(Principal(onBehalfOf: "   ").GetOnBehalfOf());
    }

    [Fact]
    public void TryGetDelegation_true_for_agent_with_on_behalf_of()
    {
        var principal = Principal(subjectType: "agent", onBehalfOf: "user-1", sub: "agent-9");

        Assert.True(principal.TryGetDelegation(out var actorId, out var onBehalfOfUserId));
        Assert.Equal("agent-9", actorId);
        Assert.Equal("user-1", onBehalfOfUserId);
    }

    [Fact]
    public void TryGetDelegation_false_for_a_human_token()
    {
        var principal = Principal(subjectType: "user", onBehalfOf: "user-1", sub: "user-1");

        Assert.False(principal.TryGetDelegation(out _, out _));
    }

    [Fact]
    public void TryGetDelegation_false_for_an_agent_acting_as_itself_without_on_behalf_of()
    {
        var principal = Principal(subjectType: "agent", sub: "agent-9");

        Assert.False(principal.TryGetDelegation(out _, out _));
    }

    [Fact]
    public void TryGetDelegation_false_when_on_behalf_of_is_whitespace()
    {
        var principal = Principal(subjectType: "agent", onBehalfOf: "   ", sub: "agent-9");

        Assert.False(principal.TryGetDelegation(out _, out _));
    }
}
