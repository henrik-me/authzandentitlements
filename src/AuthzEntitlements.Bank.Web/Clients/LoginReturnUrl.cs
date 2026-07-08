namespace AuthzEntitlements.Bank.Web.Clients;

// Sanitizes the post-sign-in return URL. The /login endpoint uses the result as the OIDC
// challenge RedirectUri, so a returnUrl the caller supplies (via a sign-in link, or a crafted
// link an attacker sends a victim) MUST be constrained to a LOCAL path — never an absolute or
// protocol-relative URL (which would be an open redirect) — and must not point back at an auth
// endpoint (which would loop the user through sign-in again after the OIDC round-trip).
internal static class LoginReturnUrl
{
    // Returns returnUrl when it is a safe local path to land on after sign-in; otherwise "/".
    public static string SafeLocalReturnUrl(string? returnUrl)
    {
        if (!IsLocalUrl(returnUrl))
        {
            return "/";
        }

        // Loop guard: a returnUrl whose path is itself /login or /logout would bounce the user
        // back through sign-in after the OIDC round-trip. Compare the path only (ignore any
        // query string or fragment).
        var path = returnUrl!;
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        if (path.Equals("/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/logout", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        return returnUrl!;
    }

    // Mirrors ASP.NET Core's IUrlHelper.IsLocalUrl for the "/rooted" case: a single leading '/'
    // that is not "//" or "/\" (which browsers treat as protocol-relative / external). Rejects
    // null/empty, absolute URLs (https://…) and scheme URLs (javascript:…), which do not start
    // with '/'.
    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
