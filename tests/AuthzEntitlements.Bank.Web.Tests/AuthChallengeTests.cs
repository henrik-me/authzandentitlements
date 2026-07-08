using System.Net;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS61 — parsing/behaviour of the 401 WWW-Authenticate challenge capture (offline, no server).
// The JWT-bearer middleware answers an expired/invalid token with
// `WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at '…'"`;
// the client must distinguish that from a plain deny so the UI says "sign in again".
public class AuthChallengeTests
{
    private static HttpResponseMessage Response(HttpStatusCode status, string? wwwAuthenticate)
    {
        var response = new HttpResponseMessage(status);
        if (wwwAuthenticate is not null)
        {
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", wwwAuthenticate);
        }

        return response;
    }

    [Fact]
    public void FromResponse_parses_invalid_token_and_detects_expiry()
    {
        using var response = Response(
            HttpStatusCode.Unauthorized,
            "Bearer error=\"invalid_token\", error_description=\"The token expired at '01/01/2026 00:00:00'\"");

        var challenge = AuthChallenge.FromResponse(response);

        Assert.Equal(401, challenge.StatusCode);
        Assert.Equal("invalid_token", challenge.Error);
        Assert.Contains("expired", challenge.ErrorDescription!);
        Assert.True(challenge.IsInvalidToken);
        Assert.True(challenge.IsTokenExpired);
    }

    [Fact]
    public void FromResponse_invalid_token_without_expiry_is_not_expired()
    {
        using var response = Response(
            HttpStatusCode.Unauthorized,
            "Bearer error=\"invalid_token\", error_description=\"The signature key was not found\"");

        var challenge = AuthChallenge.FromResponse(response);

        Assert.True(challenge.IsInvalidToken);
        Assert.False(challenge.IsTokenExpired);
    }

    [Fact]
    public void FromResponse_does_not_read_error_from_error_description()
    {
        // The \b anchors must keep "error" from matching inside "error_description".
        using var response = Response(
            HttpStatusCode.Unauthorized,
            "Bearer realm=\"authz-bank\", error=\"invalid_token\", error_description=\"token is expired\"");

        var challenge = AuthChallenge.FromResponse(response);

        Assert.Equal("invalid_token", challenge.Error);
        Assert.Equal("token is expired", challenge.ErrorDescription);
    }

    [Fact]
    public void FromResponse_bearer_without_parameters_has_no_error()
    {
        // A token-LESS 401 (no bearer sent) challenges with a bare "Bearer" — not invalid_token.
        using var response = Response(HttpStatusCode.Unauthorized, "Bearer");

        var challenge = AuthChallenge.FromResponse(response);

        Assert.Null(challenge.Error);
        Assert.False(challenge.IsInvalidToken);
    }

    [Fact]
    public void FromResponse_without_header_has_no_error()
    {
        using var response = Response(HttpStatusCode.Unauthorized, null);

        var challenge = AuthChallenge.FromResponse(response);

        Assert.Null(challenge.Error);
        Assert.Null(challenge.ErrorDescription);
        Assert.False(challenge.IsInvalidToken);
    }

    [Fact]
    public void Capture_ignores_non_401_responses()
    {
        var state = new AuthChallengeState();
        using var forbidden = Response(HttpStatusCode.Forbidden, null);

        Assert.Null(state.Capture(forbidden));
        Assert.Null(state.Current);
    }

    [Fact]
    public void Capture_records_the_challenge_on_401()
    {
        var state = new AuthChallengeState();
        using var response = Response(
            HttpStatusCode.Unauthorized,
            "Bearer error=\"invalid_token\", error_description=\"The token expired at 'x'\"");

        var captured = state.Capture(response);

        Assert.NotNull(captured);
        Assert.True(state.Current!.IsTokenExpired);
    }

    [Fact]
    public void DescribeFailure_prefers_the_expiry_message()
    {
        var challenge = new AuthChallenge(401, "invalid_token", "The token expired at 'x'");
        Assert.Equal(
            "Your session has expired. Please sign in again.",
            AuthChallengeState.DescribeFailure(challenge, string.Empty, "Unauthorized"));
    }

    [Fact]
    public void DescribeFailure_uses_invalid_token_message_when_not_expired()
    {
        var challenge = new AuthChallenge(401, "invalid_token", "malformed");
        Assert.Equal(
            "Your session is no longer valid. Please sign in again.",
            AuthChallengeState.DescribeFailure(challenge, string.Empty, "Unauthorized"));
    }

    [Fact]
    public void DescribeFailure_falls_back_to_body_when_no_token_challenge()
    {
        Assert.Equal("boom", AuthChallengeState.DescribeFailure(null, "boom", "Bad Request"));
    }
}
