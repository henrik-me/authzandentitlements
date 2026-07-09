using System.Net;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS68 — end-to-end guard for the CS65 sign-in return-URL behavior. Drives the REAL Keycloak
/// authorization-code browser flow starting at <c>/login?returnUrl=/accounts</c> as <c>teller1</c>
/// and asserts the signed-in user lands back on <c>/accounts</c> (not home) — proving the sanitized
/// <c>returnUrl</c> round-trips through the OIDC state as the challenge <c>RedirectUri</c>. Before
/// CS65 the <c>RedirectUri</c> was hardcoded <c>/</c>, so this very flow would have landed on home.
///
/// <para>Shares the headless OIDC-login flow with <see cref="ApprovalsAntiforgeryE2ETests"/> via
/// <see cref="E2EOidcLogin"/> (one copy of the fiddly Keycloak-cookie handling). Opt-in via
/// <see cref="AspireStackE2EFactAttribute"/> (<c>RUN_ASPIRE_E2E=1</c>); pins Keycloak to host port
/// 8088 so the issuer/JWKS/authority align, exactly like the other authenticated e2e tests.
/// Assembly parallelization is disabled (<c>E2ECollectionBehavior.cs</c>) so full-stack boots never
/// run concurrently.</para>
/// </summary>
public sealed class SignInReturnUrlE2ETests
{
    private readonly ITestOutputHelper _output;

    public SignInReturnUrlE2ETests(ITestOutputHelper output) => _output = output;

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
    private const string Password = "Passw0rd!";

    // The page the user "was on" and must be returned to after sign-in.
    private const string ReturnUrl = "/accounts";

    // A specific seeded account (BankSeeder) in teller1's CONTOSO tenant, rendered by the /accounts
    // page (Account # + Customer columns). Asserting a specific seeded row — not a count — proves the
    // landing is genuinely the authenticated Accounts list scoped to teller1's token.
    private const string SeededAccountNumber = "CONTOSO-CHK-0001";
    private const string SeededCustomerName = "Alice Anderson";

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task SignIn_with_returnUrl_lands_on_that_page_after_the_oidc_round_trip()
    {
        if (IsTcpPortInUse("127.0.0.1", KeycloakPort))
        {
            Assert.Fail(
                $"Port {KeycloakPort} is in use — stop any active `aspire run`/stale Keycloak container before running the e2e.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await E2EStack.CreateBuilderAsync(cts.Token);

        // Pin host ports so Keycloak binds its declared 8088, matching the http://localhost:8088
        // authority the AppHost injects — otherwise the token issuer/JWKS would not align and the
        // authenticated landing GET would 401 for the wrong reason.
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

        // Realm import lags container health by ~20–40 s and the project-service health-wait does not
        // gate on it, so poll OIDC discovery before driving the login (otherwise the redirect to
        // Keycloak could hit a not-yet-imported realm and fail flakily).
        var discoveryUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/.well-known/openid-configuration";
        using (var kc = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
        using (var discovery = await GetUntilOkAsync(kc, discoveryUrl, TimeSpan.FromSeconds(90), cts.Token))
        {
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        }

        var bankWebBase = app.GetEndpoint("bank-web", "http");
        _output.WriteLine($"bank-web endpoint: {bankWebBase}");

        // Hand-managed cookie jar (UseCookies=false): .NET's CookieContainer silently drops Keycloak's
        // AUTH_SESSION_ID/KC_RESTART cookies over the plain-HTTP dev endpoint, which breaks the login.
        var jar = new Dictionary<string, string>(StringComparer.Ordinal);
        using var browserHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        using var browser = new HttpClient(browserHandler) { Timeout = TimeSpan.FromSeconds(30) };

        // Log in through the REAL OIDC browser flow starting at /login?returnUrl=/accounts. The helper
        // follows the Keycloak redirects, posts teller1's credentials, replays the /signin-oidc
        // form_post callback, then follows the OIDC handler's post-login RedirectUri 302 to the landing.
        var landing = await E2EOidcLogin.LoginAsync(
            browser, jar, bankWebBase, $"/login?returnUrl={ReturnUrl}", "teller1", Password, cts.Token, _output.WriteLine);

        // The core assertion: the sanitized returnUrl round-tripped through the OIDC state and became
        // the post-login RedirectUri, so the user lands on /accounts — NOT home ("/"), which is where
        // the pre-CS65 hardcoded RedirectUri="/" would have landed.
        Assert.Equal(ReturnUrl, landing.AbsolutePath);

        // Confirm the landing is genuinely the authenticated Accounts page for teller1: re-GET it with
        // the session cookie and assert a specific seeded row renders (proves the full slice: session
        // cookie -> edge gateway -> tenant-scoped read), not a generic/error page.
        using var accountsResp = await E2EOidcLogin.SendAsync(
            browser, jar, HttpMethod.Get, new Uri(bankWebBase, ReturnUrl), null, cts.Token);
        var accountsHtml = await accountsResp.Content.ReadAsStringAsync(cts.Token);
        Assert.True(
            accountsResp.StatusCode == HttpStatusCode.OK,
            $"GET {ReturnUrl} should render 200 for the signed-in teller but was {(int)accountsResp.StatusCode}.");
        Assert.Contains(SeededAccountNumber, accountsHtml);
        Assert.Contains(SeededCustomerName, accountsHtml);
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

    // Polls GET <paramref name="url"/> until it returns 200 or the timeout elapses, tolerating the
    // connection refusals / transient non-200s that occur while Keycloak finishes importing the realm.
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
}
