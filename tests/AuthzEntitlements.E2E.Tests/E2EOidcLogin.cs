using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace AuthzEntitlements.E2E.Tests;

// Shared headless OIDC authorization-code (response_mode=form_post) browser-login flow for the
// full-stack e2e tests. Extracted from ApprovalsAntiforgeryE2ETests (CS60) so it and the CS68
// return-URL test drive the SAME proven flow — including the hand-managed cookie jar that works
// around .NET's CookieContainer silently dropping Keycloak's AUTH_SESSION_ID/KC_RESTART cookies
// over the plain-HTTP dev endpoint (which made the login-actions request arrive with no session
// and Keycloak reject it with an empty-body 400).
internal static class E2EOidcLogin
{
    // Drives the OIDC authorization-code flow headlessly from <paramref name="startPath"/> on
    // bank-web, following redirects manually (the caller's handler must set AllowAutoRedirect=false)
    // and carrying cookies via the hand-managed <paramref name="jar"/>. Keycloak's auth endpoint
    // 302-redirects (establishing the auth session) BEFORE rendering the login form, so we follow the
    // chain until the form (a 200 carrying a password field), POST the credentials, replay the
    // form_post callback to bank-web's /signin-oidc, then follow the OIDC handler's post-login
    // RedirectUri 302. Returns the URI of the final non-redirect app page (the landing).
    public static async Task<Uri> LoginAsync(
        HttpClient http, Dictionary<string, string> jar, Uri bankWebBase, string startPath,
        string username, string password, CancellationToken ct, Action<string>? log = null)
    {
        var url = new Uri(bankWebBase, startPath);
        var resp = await SendAsync(http, jar, HttpMethod.Get, url, null, ct);
        var postedCredentials = false;

        try
        {
            for (var hop = 0; hop < 12; hop++)
            {
                log?.Invoke($"[oidc] hop {hop}: {(int)resp.StatusCode} {url} -> {resp.Headers.Location?.ToString() ?? "(no location)"} (jar: {string.Join(",", jar.Keys)})");

                if (IsRedirect(resp.StatusCode) && resp.Headers.Location is not null)
                {
                    url = Absolute(url, resp.Headers.Location);
                    resp.Dispose();
                    resp = await SendAsync(http, jar, HttpMethod.Get, url, null, ct);
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                var action = ExtractLoginFormAction(body);

                if (action is not null && !postedCredentials)
                {
                    log?.Invoke($"[oidc] posting credentials to {action}");
                    resp.Dispose();
                    url = new Uri(action);
                    resp = await SendAsync(
                        http, jar, HttpMethod.Post, url,
                        new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["username"] = username,
                            ["password"] = password,
                            ["credentialId"] = string.Empty,
                        }),
                        ct);
                    postedCredentials = true;
                    continue;
                }

                // The OIDC handler uses response_mode=form_post, so after a successful login Keycloak
                // returns a 200 HTML page with a self-submitting <form action=".../signin-oidc"> that
                // POSTs code+state to bank-web (there is no 302). Replay that POST to complete the code
                // exchange and have bank-web set the auth cookie.
                var (callbackAction, callbackFields) = ExtractCallbackForm(body);
                if (callbackAction is not null)
                {
                    log?.Invoke($"[oidc] submitting form_post callback to {callbackAction} (fields: {string.Join(",", callbackFields.Select(f => f.Key))})");
                    resp.Dispose();
                    url = new Uri(callbackAction);
                    resp = await SendAsync(http, jar, HttpMethod.Post, url, new FormUrlEncodedContent(callbackFields), ct);
                    continue;
                }

                // Reached a non-redirect page with no login form. Either login succeeded and we are
                // back on the authenticated app, or (if the form re-appears after a POST) it failed.
                Assert.True(
                    postedCredentials && action is null,
                    action is not null
                        ? $"Keycloak re-rendered the login form after posting credentials (HTTP {(int)resp.StatusCode}) — the login was rejected (bad credentials or a pending required action)."
                        : $"the OIDC flow ended on an unexpected HTTP {(int)resp.StatusCode} page before the login form was reached (url {url}); body:\n{Truncate(body, 800)}");
                return url;
            }

            Assert.Fail("the OIDC login flow did not complete within the redirect-hop budget (possible redirect loop).");
            return url;
        }
        finally
        {
            resp.Dispose();
        }
    }

    // Sends a request with the manually-tracked cookie jar and captures Set-Cookie from the response.
    // bank-web and Keycloak are both on 'localhost', so a single jar spans them exactly as a browser's
    // host-scoped cookie store does — which is why the flow works interactively.
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, Dictionary<string, string> jar, HttpMethod method, Uri url, HttpContent? content, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (content is not null)
        {
            req.Content = content;
        }

        if (jar.Count > 0)
        {
            req.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", jar.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        var resp = await http.SendAsync(req, ct);
        CaptureCookies(jar, resp);
        return resp;
    }

    private static void CaptureCookies(Dictionary<string, string> jar, HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var raw in values)
        {
            var pair = raw.Split(';', 2)[0];
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            // A cleared cookie (empty value / deletion) is removed so a stale value is never replayed.
            if (value.Length == 0 || value == "\"\"")
            {
                jar.Remove(name);
            }
            else
            {
                jar[name] = value;
            }
        }
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.Found or HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.SeeOther;

    private static Uri Absolute(Uri requestUri, Uri location) =>
        location.IsAbsoluteUri ? location : new Uri(requestUri, location);

    private static string? ExtractLoginFormAction(string html)
    {
        // Keycloak's login form: <form id="kc-form-login" ... action="…" method="post">.
        var match = Regex.Match(html, "id=\"kc-form-login\"[^>]*action=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        // Fallback: a post form that carries a password field (a login form) — never a generic app
        // form, so landing back on the authenticated app is not mistaken for a login prompt.
        if (Regex.IsMatch(html, "type=\"password\"", RegexOptions.IgnoreCase))
        {
            var form = Regex.Match(html, "<form\\b[^>]*action=\"([^\"]+)\"[^>]*method=\"post\"", RegexOptions.IgnoreCase);
            if (form.Success)
            {
                return WebUtility.HtmlDecode(form.Groups[1].Value);
            }
        }

        return null;
    }

    // Extracts the self-submitting form_post callback (Keycloak response_mode=form_post): the form
    // whose action targets bank-web's /signin-oidc, plus its hidden inputs (code, state, session_state…).
    private static (string? Action, List<KeyValuePair<string, string>> Fields) ExtractCallbackForm(string html)
    {
        var form = Regex.Match(
            html,
            "<form\\b[^>]*action=\"([^\"]*signin-oidc[^\"]*)\"[^>]*>(.*?)</form>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!form.Success)
        {
            return (null, []);
        }

        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(form.Groups[2].Value, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            var name = Regex.Match(tag, "\\bname=\"([^\"]*)\"", RegexOptions.IgnoreCase);
            if (!name.Success)
            {
                continue;
            }

            var value = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase);
            fields.Add(new(
                WebUtility.HtmlDecode(name.Groups[1].Value),
                value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty));
        }

        return (WebUtility.HtmlDecode(form.Groups[1].Value), fields);
    }

    private static string Truncate(string value, int max = 400) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
