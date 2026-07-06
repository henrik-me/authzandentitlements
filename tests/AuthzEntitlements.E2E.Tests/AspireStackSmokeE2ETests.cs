using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS57 — the first end-to-end smoke gate. Unlike the CS50 app-model smoke test
/// (which stops at <c>BuildAsync</c> and never touches Docker), this test calls
/// <c>StartAsync</c> to boot the <em>real</em> <c>aspire run</c> stack (Keycloak +
/// Postgres + observability + all seven project services) and asserts the four
/// "basics" validated by hand at CS56 close-out:
/// <list type="number">
///   <item>every one of the 7 project services reaches Healthy;</item>
///   <item>Keycloak OIDC discovery returns 200 with the stable issuer;</item>
///   <item>a <c>teller1</c>/<c>Passw0rd!</c> password-grant token round-trip yields a
///   non-empty <c>access_token</c>;</item>
///   <item>the <c>bank-web</c> root serves HTTP 200.</item>
/// </list>
/// It is opt-in via <see cref="AspireStackE2EFactAttribute"/> (env <c>RUN_ASPIRE_E2E=1</c>),
/// so the default <c>dotnet test</c> and the Docker-free CI stay fast. The stack is torn
/// down deterministically by the <c>await using</c> on the built application.
/// </summary>
public sealed class AspireStackSmokeE2ETests
{
    private readonly ITestOutputHelper _output;

    public AspireStackSmokeE2ETests(ITestOutputHelper output) => _output = output;

    /// <summary>The seven project resources that must reach Healthy (Decision #2a).</summary>
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

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task Aspire_stack_boots_and_the_basics_work()
    {
        // (0) Guard against a concurrent live `aspire run` (Decision #4b). Aspire.Hosting.Testing
        // proxies Keycloak's fixed 8088 to a dynamically-allocated port (see the OIDC section
        // below), so this build does NOT itself need 8088 — but a live `aspire run` binds 8088,
        // and running two full stacks at once is wasteful/flaky, so fail-fast if 8088 is in use.
        if (IsTcpPortInUse("127.0.0.1", KeycloakPort))
        {
            Assert.Fail(
                $"Port {KeycloakPort} is in use — stop any active `aspire run`/stale Keycloak container before running the e2e.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>(cts.Token);

        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        // (1) Every project service must reach Healthy. StopOnResourceUnavailable makes a
        // failed resource fail fast rather than blocking to the 5-minute cap (R1).
        foreach (var name in ProjectServices)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                name,
                WaitBehavior.StopOnResourceUnavailable,
                cts.Token);
        }

        // (2) Keycloak OIDC discovery → 200 with a coherent realm issuer. Under
        // Aspire.Hosting.Testing the fixed host port 8088 is proxied to a dynamically
        // allocated host port (unlike `aspire run`, which binds 8088 directly), and
        // Keycloak (dev mode) stamps the issuer from the request host — so resolve the
        // actual endpoint via the app model rather than hard-coding localhost:8088.
        var keycloakBase = app.GetEndpoint("keycloak", "http");
        _output.WriteLine($"keycloak endpoint: {keycloakBase}");

        var realmBase = new Uri(keycloakBase, $"/realms/{Realm}/");
        var discoveryUrl = new Uri(realmBase, ".well-known/openid-configuration");
        var tokenUrl = new Uri(realmBase, "protocol/openid-connect/token");

        // Realm import adds ~20–40 s after the container is up, so poll (bounded ~60 s).
        using var kc = new HttpClient();
        var discovery = await GetWithRetryAsync(kc, discoveryUrl.ToString(), TimeSpan.FromSeconds(60), cts.Token);

        Assert.True(
            discovery.StatusCode == HttpStatusCode.OK,
            $"Keycloak OIDC discovery {discoveryUrl} should return 200 but returned {(int)discovery.StatusCode}.");

        var discoveryBody = await discovery.Content.ReadAsStringAsync(cts.Token);
        using var discoveryJson = JsonDocument.Parse(discoveryBody);
        var issuer = discoveryJson.RootElement.GetProperty("issuer").GetString();
        _output.WriteLine($"issuer: {issuer}");

        // The issuer must be the realm's OIDC issuer served over http (dev realm,
        // sslRequired=none). We assert the realm path + http scheme rather than the fixed
        // 8088 authority because the testing proxy remaps the host port.
        Assert.False(string.IsNullOrWhiteSpace(issuer), "Keycloak OIDC discovery must return a non-empty issuer.");
        Assert.EndsWith($"/realms/{Realm}", issuer);
        Assert.StartsWith("http://", issuer);

        // (3) Token round-trip: bank-web client + teller1/Passw0rd! password grant → a
        // non-empty access_token (Decision #2c).
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "bank-web",
                ["client_secret"] = "bank-web-secret",
                ["username"] = "teller1",
                ["password"] = "Passw0rd!",
                ["scope"] = "openid",
            }),
        };

        using var tokenResponse = await kc.SendAsync(tokenRequest, cts.Token);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cts.Token);
        Assert.True(
            tokenResponse.StatusCode == HttpStatusCode.OK,
            $"Token endpoint {tokenUrl} should return 200 but returned {(int)tokenResponse.StatusCode}: {tokenBody}");

        using var tokenJson = JsonDocument.Parse(tokenBody);
        Assert.True(
            tokenJson.RootElement.TryGetProperty("access_token", out var accessToken) &&
            !string.IsNullOrWhiteSpace(accessToken.GetString()),
            "The password-grant token response must contain a non-empty access_token.");

        // (4) bank-web root serves HTTP 200. Resolve the explicit "http" endpoint (bank-web
        // also has an https launch profile). Allow a short readiness retry.
        using var web = app.CreateHttpClient("bank-web", "http");
        var webResponse = await GetWithRetryAsync(web, "/", TimeSpan.FromSeconds(60), cts.Token);
        Assert.True(
            webResponse.StatusCode == HttpStatusCode.OK,
            $"bank-web root (GET /) should return 200 but returned {(int)webResponse.StatusCode}.");
    }

    /// <summary>
    /// Returns true if a TCP connection to <paramref name="host"/>:<paramref name="port"/>
    /// is accepted within a short timeout — i.e. something is already listening on the port.
    /// </summary>
    private static bool IsTcpPortInUse(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            return connect.Wait(TimeSpan.FromSeconds(1)) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// GETs <paramref name="url"/>, retrying on connection failure or a non-2xx/401 status
    /// until <paramref name="timeout"/> elapses. Used to absorb container/realm-import
    /// readiness lag. A 401 is treated as "endpoint is up" (auth-required root pages count).
    /// </summary>
    private static async Task<HttpResponseMessage> GetWithRetryAsync(
        HttpClient client,
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.Unauthorized ||
                    DateTime.UtcNow >= deadline)
                {
                    return response;
                }

                // Not ready yet and time remains — dispose this response before retrying so
                // the readiness loop doesn't leak sockets/handlers.
                response.Dispose();
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
                // endpoint not yet reachable — retry below
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }
}
