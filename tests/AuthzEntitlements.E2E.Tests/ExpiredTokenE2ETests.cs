using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS61 — verifies the SERVER contract for an expired access token against the real stack: the edge
/// gateway must reject it with <c>401</c> and an RFC 6750 <c>WWW-Authenticate: Bearer
/// error="invalid_token"</c> challenge whose description says the token expired. This is the error
/// the Bank.Web client captures to tell the user their session lapsed (see the in-process
/// <c>SessionExpiredNoticeTests</c> for the client-side handling).
///
/// <para>The realm defines a dedicated <c>bank-shortlived</c> client whose access tokens live 1
/// second, so a password grant yields a token that expires almost immediately. Because the gateway
/// allows a 30s <c>ClockSkew</c> (CS18 hardening), the test waits it out before asserting the
/// rejection — and first proves the SAME token is accepted while fresh, so the 401 is unambiguously
/// due to expiry.</para>
///
/// <para>Opt-in via <see cref="AspireStackE2EFactAttribute"/> (<c>RUN_ASPIRE_E2E=1</c>); pins
/// Keycloak to host port 8088 like the other authenticated e2e tests.</para>
/// </summary>
public sealed class ExpiredTokenE2ETests
{
    private readonly ITestOutputHelper _output;

    public ExpiredTokenE2ETests(ITestOutputHelper output) => _output = output;

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

    // > the 1s token lifespan + the gateway's 30s ClockSkew, so the token is unambiguously expired.
    private static readonly TimeSpan ExpiryWait = TimeSpan.FromSeconds(35);

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task Expired_token_is_rejected_by_the_gateway_with_www_authenticate_invalid_token()
    {
        if (IsTcpPortInUse("127.0.0.1", KeycloakPort))
        {
            Assert.Fail(
                $"Port {KeycloakPort} is in use — stop any active `aspire run`/stale Keycloak container before running the e2e.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await E2EStack.CreateBuilderAsync(cts.Token);
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
            $"Keycloak must bind the fixed host port {KeycloakPort} (DcpPublisher:RandomizePorts=false) but bound {keycloakEndpoint.Port}.");

        var tokenUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/protocol/openid-connect/token";
        var discoveryUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/.well-known/openid-configuration";
        using var kc = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var discovery = await GetUntilOkAsync(kc, discoveryUrl, TimeSpan.FromSeconds(90), cts.Token);
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);

        // (1) Obtain a 1-second-lifespan token from the dedicated short-lived client (aud=bank-api,
        // bank.read, teller1's tenant/roles — valid in every respect except that it expires at once).
        var shortLivedToken = await FetchShortLivedTokenAsync(kc, tokenUrl, "teller1", cts.Token);
        var obtainedAt = DateTime.UtcNow;

        using var gateway = app.CreateHttpClient("edge-gateway", "http");
        gateway.Timeout = TimeSpan.FromSeconds(30);

        // (2) While fresh (inside the ClockSkew window) the SAME token is accepted — proving it is
        // otherwise valid, so the later 401 is due purely to expiry, not a bad token.
        using (var freshRequest = new HttpRequestMessage(HttpMethod.Get, "/api/accounts"))
        {
            freshRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shortLivedToken);
            using var freshResponse = await SendWithRetryUntilOkAsync(gateway, freshRequest, shortLivedToken, TimeSpan.FromSeconds(20), cts.Token);
            Assert.True(
                freshResponse.StatusCode == HttpStatusCode.OK,
                $"the short-lived token should be accepted while fresh (GET /api/accounts) but was {(int)freshResponse.StatusCode}.");
        }

        // (3) Wait past the token lifespan + the gateway ClockSkew so the token is definitively expired.
        var elapsed = DateTime.UtcNow - obtainedAt;
        if (elapsed < ExpiryWait)
        {
            await Task.Delay(ExpiryWait - elapsed, cts.Token);
        }

        // (4) The expired token must now be rejected with 401 + WWW-Authenticate invalid_token/expired.
        using var expiredRequest = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        expiredRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", shortLivedToken);
        using var expiredResponse = await gateway.SendAsync(expiredRequest, cts.Token);

        Assert.True(
            expiredResponse.StatusCode == HttpStatusCode.Unauthorized,
            $"an expired token should be rejected with 401 but the gateway returned {(int)expiredResponse.StatusCode}.");

        var challenge = expiredResponse.Headers.TryGetValues("WWW-Authenticate", out var values)
            ? string.Join(" ", values)
            : string.Empty;
        _output.WriteLine($"WWW-Authenticate: {challenge}");

        Assert.Contains("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_token", challenge, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired", challenge, StringComparison.OrdinalIgnoreCase);
    }

    // Password grant against the dedicated bank-shortlived client (access.token.lifespan=1s).
    private static async Task<string> FetchShortLivedTokenAsync(
        HttpClient kc, string tokenUrl, string username, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "bank-shortlived",
                ["client_secret"] = "bank-shortlived-secret",
                ["username"] = username,
                ["password"] = "Passw0rd!",
                ["scope"] = "openid bank.read",
            }),
        };

        using var response = await kc.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"the bank-shortlived password grant for {username} should return 200 but was {(int)response.StatusCode}: {Truncate(body)}");

        using var json = JsonDocument.Parse(body);
        var token = json.RootElement.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null;
        Assert.False(string.IsNullOrWhiteSpace(token), "the short-lived token response must carry an access_token.");
        return token!;
    }

    // The stack's JWT handlers warm their JWKS on first use, so a freshly-booted gateway can briefly
    // 401 a valid token; retry (re-attaching the bearer on each fresh request) until 200 or the window
    // elapses. Kept well under the 30s ClockSkew so the token is still fresh when accepted.
    private static async Task<HttpResponseMessage> SendWithRetryUntilOkAsync(
        HttpClient client, HttpRequestMessage seed, string bearer, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        HttpResponseMessage? last = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(seed.Method, seed.RequestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            try
            {
                var response = await client.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.OK || DateTime.UtcNow >= deadline)
                {
                    last?.Dispose();
                    return response;
                }

                last?.Dispose();
                last = response;
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

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
