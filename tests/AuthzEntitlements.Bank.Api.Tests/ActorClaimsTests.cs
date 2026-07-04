using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Offline unit tests for the actor / on-behalf-of (OBO) claim reader — no live Keycloak or
// Postgres. They build synthetic ClaimsPrincipals and assert the fail-closed contract: absent
// subject_type is the human default, a non-human is recognised, a blank/absent on_behalf_of is
// NOT a delegation, and TryGetDelegation succeeds only for a non-human carrying a non-blank OBO.
public sealed class ActorClaimsTests
{
    private static ClaimsPrincipal Principal(
        string? subjectType = null, string? onBehalfOf = null, string? sub = null)
    {
        var claims = new List<Claim>();
        if (subjectType is not null)
        {
            claims.Add(new Claim(ActorClaims.SubjectTypeClaim, subjectType));
        }

        if (onBehalfOf is not null)
        {
            claims.Add(new Claim(ActorClaims.OnBehalfOfClaim, onBehalfOf));
        }

        if (sub is not null)
        {
            claims.Add(new Claim(ActorClaims.SubjectClaim, sub));
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

    [Fact]
    public void Blank_subject_type_defaults_to_user_and_is_not_non_human()
    {
        var principal = Principal(subjectType: "   ");

        Assert.Equal("user", principal.GetSubjectType());
        Assert.False(principal.IsNonHuman());
    }

    [Fact]
    public void Explicit_user_subject_type_is_not_non_human()
    {
        var principal = Principal(subjectType: "user");

        Assert.Equal("user", principal.GetSubjectType());
        Assert.False(principal.IsNonHuman());
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("service")]
    [InlineData("AGENT")]
    public void Non_user_subject_type_is_non_human(string subjectType)
    {
        var principal = Principal(subjectType: subjectType);

        Assert.Equal(subjectType, principal.GetSubjectType());
        Assert.True(principal.IsNonHuman());
    }

    [Fact]
    public void GetOnBehalfOf_is_null_when_absent()
    {
        Assert.Null(Principal().GetOnBehalfOf());
    }

    [Fact]
    public void GetOnBehalfOf_is_null_when_blank()
    {
        Assert.Null(Principal(onBehalfOf: "   ").GetOnBehalfOf());
    }

    [Fact]
    public void GetOnBehalfOf_returns_the_value_when_present()
    {
        Assert.Equal("user-1", Principal(onBehalfOf: "user-1").GetOnBehalfOf());
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

        Assert.False(principal.TryGetDelegation(out var actorId, out var onBehalfOfUserId));
        Assert.Equal(string.Empty, actorId);
        Assert.Equal(string.Empty, onBehalfOfUserId);
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

    [Fact]
    public void TryGetDelegation_false_when_agent_token_has_no_sub()
    {
        var principal = Principal(subjectType: "agent", onBehalfOf: "user-1");

        Assert.False(principal.TryGetDelegation(out _, out _));
    }
}
