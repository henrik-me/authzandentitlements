using System.Net;
using System.Text.RegularExpressions;

namespace AuthzEntitlements.Bank.Web.Clients;

// A parsed HTTP 401 challenge from a downstream API (edge gateway / Bank.Api / governance),
// derived from the RFC 6750 "WWW-Authenticate: Bearer" header the JWT-bearer middleware emits.
// Distinguishing an EXPIRED / invalid token (error="invalid_token") from a plain authorization
// denial (403) or a token-less request lets the UI tell the user to sign in again rather than
// wrongly implying they lack permission.
public sealed record AuthChallenge(int StatusCode, string? Error, string? ErrorDescription)
{
    // RFC 6750 §3.1: an expired, malformed, or otherwise invalid access token.
    public bool IsInvalidToken =>
        string.Equals(Error, "invalid_token", StringComparison.OrdinalIgnoreCase);

    // The middleware's error_description for an expired token is "The token expired at '…'".
    public bool IsTokenExpired =>
        IsInvalidToken
        && ErrorDescription is not null
        && ErrorDescription.Contains("expired", StringComparison.OrdinalIgnoreCase);

    // Parses the raw WWW-Authenticate header(s) itself rather than via
    // HttpResponseHeaders.WwwAuthenticate, whose AuthenticationHeaderValue parsing splits the
    // comma-separated Bearer parameters into bogus challenges.
    public static AuthChallenge FromResponse(HttpResponseMessage response)
    {
        var raw = response.Headers.TryGetValues("WWW-Authenticate", out var values)
            ? string.Join(", ", values)
            : null;

        return new AuthChallenge(
            (int)response.StatusCode,
            raw is null ? null : ExtractParameter(raw, "error"),
            raw is null ? null : ExtractParameter(raw, "error_description"));
    }

    // Extracts a single Bearer auth-param value. The \b…\b around the name keeps "error" from
    // matching inside "error_description". Handles both quoted and bare values.
    private static string? ExtractParameter(string raw, string name)
    {
        var quoted = Regex.Match(
            raw, "\\b" + Regex.Escape(name) + "\\b\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        if (quoted.Success)
        {
            return quoted.Groups[1].Value;
        }

        var bare = Regex.Match(
            raw, "\\b" + Regex.Escape(name) + "\\b\\s*=\\s*([^,\\s]+)", RegexOptions.IgnoreCase);
        return bare.Success ? bare.Groups[1].Value : null;
    }
}

// Scoped, per-request record of the most recent 401 challenge seen while calling downstream APIs
// during this request. The token-forwarding typed clients (BankApiClient / GovernanceClient) call
// Capture; static-SSR pages read Current to render a "your session has expired" notice instead of a
// generic "not authorized" one. Scoped so it never leaks across requests/users.
public sealed class AuthChallengeState
{
    public AuthChallenge? Current { get; private set; }

    // Records the challenge when a response is 401; a no-op otherwise. Last-write-wins within the
    // request (the most recent upstream 401 is the one the page reports). Returns the captured
    // challenge (or null) so callers can enrich their own failure envelope in the same call.
    public AuthChallenge? Capture(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return null;
        }

        var challenge = AuthChallenge.FromResponse(response);
        Current = challenge;
        return challenge;
    }

    // A user-facing message for a write outcome, preferring the token-expiry / invalid-token
    // explanation over the raw (usually empty) 401 body so the UI never says "denied" for an
    // expired session.
    public static string DescribeFailure(AuthChallenge? challenge, string? body, string? reasonPhrase)
    {
        if (challenge?.IsTokenExpired == true)
        {
            return "Your session has expired. Please sign in again.";
        }

        if (challenge?.IsInvalidToken == true)
        {
            return "Your session is no longer valid. Please sign in again.";
        }

        return string.IsNullOrWhiteSpace(body)
            ? reasonPhrase ?? "The request was denied."
            : body;
    }
}
