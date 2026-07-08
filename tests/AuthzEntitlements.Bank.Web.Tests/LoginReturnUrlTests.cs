using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS65 — the returnUrl sanitizer that /login feeds into the OIDC challenge RedirectUri. It must
// keep only LOCAL paths (open-redirect guard) and never redirect back at an auth endpoint (which
// would loop the user through sign-in after the OIDC round-trip).
public sealed class LoginReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/accounts", "/accounts")]
    [InlineData("/accounts/abc", "/accounts/abc")]
    [InlineData("/accounts/abc?tab=tx", "/accounts/abc?tab=tx")]
    [InlineData("/a/b/c?x=1&y=2#frag", "/a/b/c?x=1&y=2#frag")]
    public void Local_paths_are_honored(string returnUrl, string expected) =>
        Assert.Equal(expected, LoginReturnUrl.SafeLocalReturnUrl(returnUrl));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.com")]           // protocol-relative -> external origin
    [InlineData("/\\evil.com")]          // backslash variant browsers treat as protocol-relative
    [InlineData("https://evil.com")]     // absolute
    [InlineData("http://evil.com/path")] // absolute
    [InlineData("javascript:alert(1)")]  // scheme URL
    [InlineData("accounts")]             // no leading slash (relative/ambiguous)
    [InlineData("  /accounts")]          // leading whitespace -> does not start with '/'
    [InlineData("/\t/evil.com")]         // tab control char — browsers strip it -> "//evil.com"
    [InlineData("/\n//evil.com")]        // newline control char
    [InlineData("/\r/evil.com")]         // carriage-return control char
    public void Non_local_or_empty_falls_back_to_home(string? returnUrl) =>
        Assert.Equal("/", LoginReturnUrl.SafeLocalReturnUrl(returnUrl));

    [Theory]
    [InlineData("/login")]
    [InlineData("/logout")]
    [InlineData("/LOGIN")]                      // case-insensitive
    [InlineData("/login?returnUrl=/accounts")]  // loop attempt with a query string
    [InlineData("/logout#x")]                   // loop attempt with a fragment
    public void Auth_endpoints_fall_back_to_home_to_avoid_loop(string returnUrl) =>
        Assert.Equal("/", LoginReturnUrl.SafeLocalReturnUrl(returnUrl));
}
