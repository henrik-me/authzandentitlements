using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS60 — authenticated <em>UI</em> drive-through of the maker-checker Approvals page against the
/// real <c>aspire run</c> stack. Where <see cref="AuthenticatedFlowE2ETests"/> drives the API with
/// bearer tokens, this test logs in through the real OIDC browser flow (Keycloak login form →
/// bank-web cookie) and POSTs the static-SSR approve form as <c>teller1</c>.
///
/// <para>It is the end-to-end guard for the duplicate-antiforgery-token bug: a Blazor SSR
/// <c>EditForm</c> auto-emits the antiforgery hidden field, so an explicit <c>&lt;AntiforgeryToken /&gt;</c>
/// produced a second identical field and the POST failed with "A valid antiforgery token was not
/// provided with the request" (HTTP 400) — masking the intended fail-closed 403. The test asserts one
/// token per form and that a teller's approve POST reaches the fail-closed "Decision denied" outcome
/// instead of the antiforgery error.</para>
///
/// <para>Opt-in via <see cref="AspireStackE2EFactAttribute"/> (<c>RUN_ASPIRE_E2E=1</c>); pins Keycloak
/// to host port 8088 (issuer/JWKS/authority alignment, Decision #5) exactly like the CS58 test.
/// Assembly parallelization is disabled (<c>E2ECollectionBehavior.cs</c>) so it never boots
/// concurrently with the other full-stack e2e tests.</para>
/// </summary>
public sealed class ApprovalsAntiforgeryE2ETests
{
    private readonly ITestOutputHelper _output;

    public ApprovalsAntiforgeryE2ETests(ITestOutputHelper output) => _output = output;

    private static readonly string[] ProjectServices =
    [
        "entitlements-service",
        "bank-api",
        "edge-gateway",
        "audit-service",
        "authz-pdp",
        "governance-service",
        "bank-web",
    ];

    private const int KeycloakPort = 8088;
    private const string Realm = "authz-bank";

    // Seeded identifiers (BankSeeder / realm import). teller1's Keycloak sub equals its Bank.Api
    // User.Id, so the maker id on a create must equal the caller's subject.
    private const string ContosoCheckingAccountId = "50000000-0000-0000-0000-000000000001";
    private const string Teller1Sub = "40000000-0000-0000-0000-000000000001";

    // >= the 10k maker-checker threshold and < the 50k high-value premium gate, so the create routes
    // to a pending approval without needing a premium entitlement.
    private const decimal PendingAmount = 15_000m;

    private const string TokenScopes = "openid bank.read bank.transactions.write bank.approvals.write";
    private const string Password = "Passw0rd!";

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task Teller_approval_ui_post_is_not_blocked_by_antiforgery_and_fails_closed()
    {
        if (IsTcpPortInUse("127.0.0.1", KeycloakPort))
        {
            Assert.Fail(
                $"Port {KeycloakPort} is in use — stop any active `aspire run`/stale Keycloak container before running the e2e.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await E2EStack.CreateBuilderAsync(cts.Token);

        // Pin host ports so Keycloak binds its declared 8088, matching the http://localhost:8088
        // Keycloak__Authority the AppHost injects (Decision #5) — otherwise the token issuer/JWKS
        // would not align and every authenticated call 401s.
        appHost.Configuration["DcpPublisher:RandomizePorts"] = "false";

        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        foreach (var name in ProjectServices)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                name, WaitBehavior.StopOnResourceUnavailable, cts.Token);
        }

        var keycloakEndpoint = app.GetEndpoint("keycloak", "http");
        Assert.True(
            keycloakEndpoint.Port == KeycloakPort,
            $"Keycloak must bind the fixed host port {KeycloakPort} (DcpPublisher:RandomizePorts=false) so the OIDC " +
            $"issuer/JWKS align with the services' authority, but it bound {keycloakEndpoint.Port}.");

        var discoveryUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/.well-known/openid-configuration";
        var tokenUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/protocol/openid-connect/token";
        using var kc = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var discovery = await GetUntilOkAsync(kc, discoveryUrl, TimeSpan.FromSeconds(90), cts.Token);
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);

        // (1) Seed a pending approval: teller1 (maker) creates an at-threshold transaction THROUGH the
        // edge gateway with a bearer, exactly as bank-web's BankApiClient does. It becomes Pending with
        // a pending approval, so the Approvals page renders its forms.
        var teller1Token = await FetchAccessTokenAsync(kc, tokenUrl, "teller1", cts.Token);
        using var gateway = CreateAuthorizedClient(app, "edge-gateway", teller1Token);
        string pendingTransactionId;
        using (var createResp = await PostJsonAsync(
            gateway, "/api/transactions", CreatePendingTransactionJson(), TimeSpan.FromSeconds(60), cts.Token))
        {
            var body = await createResp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                createResp.StatusCode == HttpStatusCode.Created,
                $"seeding a pending transaction (teller1 maker, {PendingAmount}) should return 201 but was " +
                $"{(int)createResp.StatusCode}: {Truncate(body)}");
            pendingTransactionId = ReadStringProperty(body, "id");
            Assert.False(string.IsNullOrWhiteSpace(pendingTransactionId), "the seeded transaction must carry an id.");
        }

        // (2) Log in to bank-web through the real OIDC browser flow (Keycloak login form -> cookie).
        // Cookies are tracked by hand (UseCookies=false): .NET's CookieContainer silently dropped the
        // Keycloak session cookies (AUTH_SESSION_ID/KC_RESTART) over the plain-HTTP dev endpoint, so the
        // login-actions request arrived with no session and Keycloak rejected it (empty-body 400).
        // Capturing Set-Cookie and replaying name=value ourselves mirrors a browser and is quirk-free.
        var bankWebBase = app.GetEndpoint("bank-web", "http");
        _output.WriteLine($"bank-web endpoint: {bankWebBase}");
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);
        using var browserHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        using var browser = new HttpClient(browserHandler) { Timeout = TimeSpan.FromSeconds(30) };
        _ = await E2EOidcLogin.LoginAsync(browser, jar, bankWebBase, "/login", "teller1", Password, cts.Token, _output.WriteLine);

        // (3) GET /approvals as the signed-in teller. The page must render the approve/reject forms,
        // each with EXACTLY ONE antiforgery token. Two per form is the duplicate-<AntiforgeryToken />
        // regression that breaks the POST.
        using var approvalsResp = await E2EOidcLogin.SendAsync(browser, jar, HttpMethod.Get, new Uri(bankWebBase, "/approvals"), null, cts.Token);
        var approvalsHtml = await approvalsResp.Content.ReadAsStringAsync(cts.Token);
        Assert.True(
            approvalsResp.StatusCode == HttpStatusCode.OK,
            $"GET /approvals should render 200 for the signed-in teller but was {(int)approvalsResp.StatusCode}.");
        Assert.Contains(pendingTransactionId, approvalsHtml);

        // Assert PER FORM (not page totals): each Blazor EditForm — identified by its FormName
        // `_handler` hidden field — must carry EXACTLY ONE __RequestVerificationToken. A page-total
        // comparison could pass falsely if one form had two tokens and another had none.
        var editForms = Regex.Matches(approvalsHtml, "<form\\b.*?</form>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(f => f.Contains("name=\"_handler\""))
            .ToList();
        Assert.True(
            editForms.Count >= 2,
            $"the approvals page should render the approve + reject Blazor EditForms but had {editForms.Count}.");
        foreach (var form in editForms)
        {
            var perFormTokens = Regex.Matches(form, "name=\"__RequestVerificationToken\"").Count;
            Assert.True(
                perFormTokens == 1,
                $"each Blazor EditForm must carry exactly one __RequestVerificationToken (EditForm injects it " +
                $"automatically), but one form had {perFormTokens} — more than one per form is the duplicate-token regression.");
        }

        // (4) POST the approve form as the teller. The teller is not a checker, so the server denies
        // (403). The point is that the POST is NOT rejected by antiforgery first: the page must render
        // the fail-closed "Decision denied" outcome, never the antiforgery error.
        var approveForm = ExtractApproveForm(approvalsHtml);
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var token in ExtractHiddenValues(approveForm, "__RequestVerificationToken"))
        {
            fields.Add(new("__RequestVerificationToken", token));
        }

        fields.Add(new("_handler", ExtractHiddenValues(approveForm, "_handler").FirstOrDefault() ?? "approve"));
        var selectName = ExtractSelectName(approveForm);
        Assert.False(string.IsNullOrEmpty(selectName), $"could not find the transaction <select> in the approve form:\n{approveForm}");
        fields.Add(new(selectName!, pendingTransactionId));

        using var postResp = await E2EOidcLogin.SendAsync(
            browser, jar, HttpMethod.Post, new Uri(bankWebBase, "/approvals"), new FormUrlEncodedContent(fields), cts.Token);
        var postBody = await postResp.Content.ReadAsStringAsync(cts.Token);

        _output.WriteLine($"[post] status={(int)postResp.StatusCode} len={postBody.Length}");

        // A duplicate-token antiforgery failure is HTTP 400 with the framework message. The fix makes
        // the POST pass antiforgery and reach the component, which surfaces the fail-closed server
        // denial (teller is not a checker -> 403) instead of the antiforgery error.
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        Assert.DoesNotContain("A valid antiforgery token was not provided", postBody);
        Assert.Contains("Decision denied", postBody);
    }

    private static async Task<string> FetchAccessTokenAsync(
        HttpClient kc, string tokenUrl, string username, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "bank-web",
                ["client_secret"] = "bank-web-secret",
                ["username"] = username,
                ["password"] = Password,
                ["scope"] = TokenScopes,
            }),
        };

        using var response = await kc.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"password grant for {username} should return 200 but was {(int)response.StatusCode}: {Truncate(body)}");
        using var json = JsonDocument.Parse(body);
        var token = json.RootElement.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null;
        Assert.False(string.IsNullOrWhiteSpace(token), $"the token response for {username} must carry an access_token.");
        return token!;
    }

    private static HttpClient CreateAuthorizedClient(DistributedApplication app, string resource, string token)
    {
        var client = app.CreateHttpClient(resource, "http");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string CreatePendingTransactionJson() =>
        JsonSerializer.Serialize(new
        {
            accountId = ContosoCheckingAccountId,
            type = "Debit",
            amount = PendingAmount,
            makerId = Teller1Sub,
            reference = "e2e antiforgery pending approval",
        });

    // ---- HTML helpers (shared shape with the Bank.Web integration test) ----

    private static string ExtractApproveForm(string html)
    {
        var start = html.IndexOf("<form", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return html;
        }

        var end = html.IndexOf("</form>", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html[start..] : html[start..(end + "</form>".Length)];
    }

    private static IEnumerable<string> ExtractHiddenValues(string formHtml, string name)
    {
        foreach (Match input in Regex.Matches(formHtml, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            if (!Regex.IsMatch(tag, $"\\bname=\"{Regex.Escape(name)}\""))
            {
                continue;
            }

            var value = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"");
            if (value.Success)
            {
                yield return WebUtility.HtmlDecode(value.Groups[1].Value);
            }
        }
    }

    private static string? ExtractSelectName(string formHtml)
    {
        var match = Regex.Match(formHtml, "<select\\b[^>]*\\bname=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    // ---- shared infra copies (kept self-contained; the E2E project references only the AppHost) ----

    private static async Task<HttpResponseMessage> GetUntilOkAsync(
        HttpClient client, string url, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        HttpResponseMessage? last = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var response = await client.GetAsync(url, ct);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        last?.Dispose();
                        return response;
                    }

                    last?.Dispose();
                    last = response;
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                }

                if (DateTime.UtcNow >= deadline)
                {
                    if (last is null)
                    {
                        throw new TimeoutException($"GET {url} did not return any response within {timeout}.");
                    }

                    var result = last;
                    last = null;
                    return result;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        finally
        {
            last?.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string url, string json, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, ct);
                // The gateway/Bank.Api JWT handlers warm their JWKS on first use, so a freshly-booted
                // stack can briefly 401 an otherwise-valid bearer (the same warm-up race the other e2e
                // tests guard against). A 401 is rejected by the auth middleware BEFORE the endpoint
                // runs, so no transaction is created — retrying until a non-401 status (or the deadline)
                // is duplicate-safe and keeps the seed step from flaking on a cold JWKS.
                if (response.StatusCode != HttpStatusCode.Unauthorized || DateTime.UtcNow >= deadline)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private static string ReadStringProperty(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static bool IsTcpPortInUse(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(host, port);
            return connect.Wait(TimeSpan.FromMilliseconds(500)) && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string value, int max = 400) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
