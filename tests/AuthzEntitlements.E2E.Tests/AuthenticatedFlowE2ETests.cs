using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS58 — the authenticated end-to-end gate. Where the CS57 smoke test stops at "bank-web serves
/// 200", this test logs in as <c>teller1</c> and <c>manager1</c> (realm password grant) and drives
/// the <em>authenticated</em> flow through the real <c>aspire run</c> stack: a tenant-scoped read,
/// a role-gated create, the maker-checker create path, and a governance break-glass — the coverage
/// that would have caught the CS58 regression (the five internal services defaulting to
/// <c>ASPNETCORE_ENVIRONMENT=Production</c>, whose <c>RequireHttpsMetadata=true</c> made the JWT
/// handler reject the HTTP dev Keycloak authority → HTTP 500 on every authenticated request).
///
/// <para>Fixed-port issuer alignment (Decision #5 / R5): under default <c>Aspire.Hosting.Testing</c>
/// the fixed host port 8088 is proxied to a dynamic port (LRN-088), so the services could not reach
/// JWKS at <c>:8088</c> and the token issuer would not match their injected
/// <c>http://localhost:8088</c> authority → 401. This test disables DCP port randomization so
/// Keycloak binds 8088 and the whole auth chain (issuer + JWKS + authority) agrees exactly as under
/// <c>aspire run</c>.</para>
///
/// <para>It is opt-in via <see cref="AspireStackE2EFactAttribute"/> (env <c>RUN_ASPIRE_E2E=1</c>),
/// so the default Docker-free <c>dotnet test</c> reports it Skipped. Assembly parallelization is
/// disabled (see <c>E2ECollectionBehavior.cs</c>) so it never boots concurrently with the CS57
/// smoke test.</para>
/// </summary>
public sealed class AuthenticatedFlowE2ETests
{
    private readonly ITestOutputHelper _output;

    public AuthenticatedFlowE2ETests(ITestOutputHelper output) => _output = output;

    /// <summary>The seven project resources that must reach Healthy before the flow runs.</summary>
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

    // Seeded identifiers (BankSeeder). The E2E project references only the AppHost, so the
    // request bodies are black-box JSON with these literal ids rather than typed DTOs. Keycloak
    // stamps each user's `sub` to their Bank.Api User.Id (realm import), so the maker id on a
    // create must equal the caller's subject.
    private const string ContosoTenantId = "11111111-1111-1111-1111-111111111111";
    private const string NorthMainBranchId = "20000000-0000-0000-0000-000000000001";
    private const string Teller1Sub = "40000000-0000-0000-0000-000000000001";
    private const string Manager1Sub = "40000000-0000-0000-0000-000000000002";
    private const string ContosoCheckingAccountId = "50000000-0000-0000-0000-000000000001";

    // The scopes bank-web requests (Bank.Web/Program.cs): read + the two optional write scopes.
    private const string TokenScopes = "openid bank.read bank.transactions.write bank.approvals.write";

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task Authenticated_teller_and_manager_flow_works_through_the_stack()
    {
        // (0) Fail-fast guard. A live `aspire run` binds 8088 — and because this test PINS ports
        // (RandomizePorts=false) so Keycloak binds its declared 8088, a busy 8088 is a hard
        // conflict, not just wasteful. Stop any active stack first.
        if (IsTcpPortInUse("127.0.0.1", KeycloakPort))
        {
            Assert.Fail(
                $"Port {KeycloakPort} is in use — stop any active `aspire run`/stale Keycloak container before running the e2e.");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>(cts.Token);

        // (Decision #5 / R5) Pin host ports so Keycloak binds its declared 8088, matching the
        // `http://localhost:8088` Keycloak__Authority the AppHost injects into edge-gateway /
        // bank-api / governance-service. `RandomizePorts` is bound from the `DcpPublisher`
        // configuration section; setting it false BEFORE BuildAsync makes DCP honour the fixed
        // endpoint instead of proxying it to a dynamic port (LRN-088). Without this the token
        // issuer/JWKS/authority cannot agree and every authenticated call would 401.
        appHost.Configuration["DcpPublisher:RandomizePorts"] = "false";

        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        // (1) Every project service must reach Healthy. StopOnResourceUnavailable fails fast on a
        // broken resource rather than blocking to the 5-minute cap.
        foreach (var name in ProjectServices)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                name,
                WaitBehavior.StopOnResourceUnavailable,
                cts.Token);
        }

        // (2) Verify the fixed-port pin actually bound Keycloak to 8088. If it did not, the
        // issuer/JWKS/authority chain cannot align and the scenarios would 401 for the wrong
        // reason — fail with a message pointing straight at the port strategy (Decision #5).
        var keycloakEndpoint = app.GetEndpoint("keycloak", "http");
        _output.WriteLine($"keycloak endpoint: {keycloakEndpoint}");
        Assert.True(
            keycloakEndpoint.Port == KeycloakPort,
            $"Keycloak must bind the fixed host port {KeycloakPort} (set DcpPublisher:RandomizePorts=false) so the " +
            $"token issuer/JWKS align with the services' injected http://localhost:{KeycloakPort} authority, but it " +
            $"bound {keycloakEndpoint.Port}. The fixed-port mechanism did not take effect (Decision #5 / R5); the " +
            "authenticated flow cannot be validated until it does.");

        // Tokens are minted at — and validated against — the fixed authority the AppHost injects,
        // so the issuer stamped into the token matches the services' ValidIssuer exactly.
        var tokenUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/protocol/openid-connect/token";
        var discoveryUrl = $"http://localhost:{KeycloakPort}/realms/{Realm}/.well-known/openid-configuration";

        // Realm import adds ~20–40 s after the container is up, so poll for discovery readiness.
        using var kc = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var discovery = await GetUntilOkAsync(kc, discoveryUrl, TimeSpan.FromSeconds(90), cts.Token);
        Assert.True(
            discovery.StatusCode == HttpStatusCode.OK,
            $"Keycloak OIDC discovery at {discoveryUrl} should return 200 (realm imported, bound on {KeycloakPort}) " +
            $"but returned {(int)discovery.StatusCode}.");

        var teller1Token = await FetchAccessTokenAsync(kc, tokenUrl, "teller1", cts.Token);
        var manager1Token = await FetchAccessTokenAsync(kc, tokenUrl, "manager1", cts.Token);

        // API traffic goes THROUGH the edge-gateway (as bank-web's BankApiClient does); governance
        // is called DIRECTLY (as bank-web's GovernanceClient does — the gateway has no governance
        // route). Both forward the signed-in user's bearer.
        using var teller1Gateway = CreateAuthorizedClient(app, "edge-gateway", teller1Token);
        using var manager1Gateway = CreateAuthorizedClient(app, "edge-gateway", manager1Token);
        using var teller1Governance = CreateAuthorizedClient(app, "governance-service", teller1Token);
        using var manager1Governance = CreateAuthorizedClient(app, "governance-service", manager1Token);

        var users = new[]
        {
            new AuthenticatedUser("teller1", Teller1Sub, teller1Gateway, teller1Governance),
            new AuthenticatedUser("manager1", Manager1Sub, manager1Gateway, manager1Governance),
        };

        // (3) Tenant-scoped read: GET /api/accounts → 200 with the three seeded CONTOSO accounts
        // present and the FABRIKAM account absent (tenant isolation). This is the exact read that
        // 500'd pre-fix (services in Production rejected the HTTP Keycloak authority).
        foreach (var u in users)
        {
            using var resp = await GetUntilOkAsync(u.Gateway, "/api/accounts", TimeSpan.FromSeconds(90), cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                resp.StatusCode == HttpStatusCode.OK,
                $"{u.Label} GET /api/accounts should return 200 (pre-fix this 500s: the internal services default to " +
                $"Production, where RequireHttpsMetadata=true rejects the HTTP dev Keycloak authority) but returned " +
                $"{(int)resp.StatusCode}: {Truncate(body)}");

            var numbers = ReadStringArrayProperty(body, "accountNumber");
            Assert.Contains("CONTOSO-CHK-0001", numbers);
            Assert.Contains("CONTOSO-SAV-0001", numbers);
            Assert.Contains("CONTOSO-LON-0001", numbers);
            Assert.True(
                numbers.Count >= 3,
                $"{u.Label} should see at least the 3 seeded CONTOSO accounts but saw {numbers.Count}.");
            Assert.DoesNotContain("FABRIKAM-CHK-0001", numbers);
        }

        // (4) GET /api/transactions → 200 with at least the 3 seeded transactions. Lower-bound (the
        // DB is a persistent volume that accumulates across runs), never an exact count.
        foreach (var u in users)
        {
            using var resp = await GetUntilOkAsync(u.Gateway, "/api/transactions", TimeSpan.FromSeconds(60), cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                resp.StatusCode == HttpStatusCode.OK,
                $"{u.Label} GET /api/transactions should return 200 but returned {(int)resp.StatusCode}: {Truncate(body)}");
            Assert.True(
                JsonArrayLength(body) >= 3,
                $"{u.Label} should see at least the 3 seeded CONTOSO transactions.");
        }

        // (5) Authz contract (Decision #4): a Teller may NOT create accounts (POST /api/accounts is
        // BranchManager-only). Assert the 403 deny — the auth path is warm from the reads above, so
        // this is a deterministic authorization outcome, not a startup race.
        using (var teller1CreateAttempt = await PostJsonAsync(
            teller1Gateway, "/api/accounts", CreateAccountJson(UniqueAccountNumber()), TimeSpan.FromSeconds(30), cts.Token))
        {
            Assert.True(
                teller1CreateAttempt.StatusCode == HttpStatusCode.Forbidden,
                $"teller1 POST /api/accounts must be 403 (Teller is not BranchManager) but returned " +
                $"{(int)teller1CreateAttempt.StatusCode}.");
        }

        // (6) manager1 (BranchManager) creates an account → 201, then it is retrievable by id.
        var newAccountNumber = UniqueAccountNumber();
        string newAccountId;
        using (var createResp = await PostJsonAsync(
            manager1Gateway, "/api/accounts", CreateAccountJson(newAccountNumber), TimeSpan.FromSeconds(30), cts.Token))
        {
            var body = await createResp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                createResp.StatusCode == HttpStatusCode.Created,
                $"manager1 POST /api/accounts should return 201 but returned {(int)createResp.StatusCode}: {Truncate(body)}");
            newAccountId = ReadStringProperty(body, "id");
            Assert.False(string.IsNullOrWhiteSpace(newAccountId), "the created account must carry an id.");
        }

        using (var getResp = await GetUntilOkAsync(
            manager1Gateway, $"/api/accounts/{newAccountId}", TimeSpan.FromSeconds(30), cts.Token))
        {
            var body = await getResp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                getResp.StatusCode == HttpStatusCode.OK,
                $"the created account {newAccountId} should be retrievable (200) but was {(int)getResp.StatusCode}.");
            Assert.Equal(newAccountNumber, ReadStringProperty(body, "accountNumber"));
        }

        // (7) Both users create a below-threshold Debit transaction (maker = caller) → 201, then it
        // is retrievable. Amount is well below the maker-checker (10k) and high-value (50k)
        // thresholds so it posts immediately and needs no approval or premium feature.
        foreach (var u in users)
        {
            string transactionId;
            using (var createResp = await PostJsonAsync(
                u.Gateway, "/api/transactions", CreateTransactionJson(u.Sub), TimeSpan.FromSeconds(30), cts.Token))
            {
                var body = await createResp.Content.ReadAsStringAsync(cts.Token);
                Assert.True(
                    createResp.StatusCode == HttpStatusCode.Created,
                    $"{u.Label} POST /api/transactions should return 201 but returned {(int)createResp.StatusCode}: {Truncate(body)}");
                transactionId = ReadStringProperty(body, "id");
                Assert.False(string.IsNullOrWhiteSpace(transactionId), $"{u.Label} created transaction must carry an id.");
            }

            using var getResp = await GetUntilOkAsync(
                u.Gateway, $"/api/transactions/{transactionId}", TimeSpan.FromSeconds(30), cts.Token);
            Assert.True(
                getResp.StatusCode == HttpStatusCode.OK,
                $"{u.Label} created transaction {transactionId} should be retrievable (200) but was {(int)getResp.StatusCode}.");
        }

        // (8) Break-glass through governance-service WITH the bearer attached (as bank-web's
        // AccessTokenHandler does). governance-service runs the same RequireHttpsMetadata JWT path,
        // so pre-fix a bearer-bearing request 500s (the handler fetches OIDC metadata over HTTP and
        // throws in Production); post-fix it is 201. The endpoint itself is anonymous — the bearer
        // is what exercises the JWT path.
        foreach (var u in users)
        {
            using var resp = await PostJsonAsync(
                u.Governance, "/api/governance/break-glass", BreakGlassJson(u.Sub), TimeSpan.FromSeconds(30), cts.Token);
            var body = await resp.Content.ReadAsStringAsync(cts.Token);
            Assert.True(
                resp.StatusCode == HttpStatusCode.Created,
                $"{u.Label} POST /api/governance/break-glass (with bearer) should return 201 (pre-fix this 500s on the " +
                $"RequireHttpsMetadata path) but returned {(int)resp.StatusCode}: {Truncate(body)}");
        }
    }

    /// <summary>A signed-in user plus the gateway/governance clients that carry its bearer.</summary>
    private sealed record AuthenticatedUser(string Label, string Sub, HttpClient Gateway, HttpClient Governance);

    /// <summary>Fetches a bank-web password-grant access token for <paramref name="username"/>.</summary>
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
                ["password"] = "Passw0rd!",
                ["scope"] = TokenScopes,
            }),
        };

        using var response = await kc.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"password grant for {username} should return 200 but returned {(int)response.StatusCode}: {Truncate(body)}");

        using var json = JsonDocument.Parse(body);
        var token = json.RootElement.TryGetProperty("access_token", out var accessToken)
            ? accessToken.GetString()
            : null;
        Assert.False(string.IsNullOrWhiteSpace(token), $"the token response for {username} must carry a non-empty access_token.");
        return token!;
    }

    /// <summary>Resolves an <c>http</c> client for a resource and attaches a bearer token.</summary>
    private static HttpClient CreateAuthorizedClient(DistributedApplication app, string resource, string token)
    {
        var client = app.CreateHttpClient(resource, "http");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Per-request cap (like the kc client) — kept at/below the readiness-helper windows (30–90 s)
        // so a single hung request cannot block far past the helper's intended timeout; the helper's
        // own deadline is the overall retry budget and the 5-minute CTS is the hard ceiling.
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string UniqueAccountNumber() =>
        // varchar(34), UNIQUE index — cap at 34 chars; the GUID tail keeps it unique per run.
        ("CONTOSO-E2E-" + Guid.NewGuid().ToString("N"))[..34];

    private static string CreateAccountJson(string accountNumber) =>
        JsonSerializer.Serialize(new
        {
            tenantId = ContosoTenantId,
            branchId = NorthMainBranchId,
            accountNumber,
            customerName = "E2E Customer",
            type = "Checking",
            balance = 1_000.00m,
            currency = "USD",
        });

    private static string CreateTransactionJson(string makerSub) =>
        JsonSerializer.Serialize(new
        {
            accountId = ContosoCheckingAccountId,
            type = "Debit",
            amount = 100.00m,
            makerId = makerSub,
            reference = "e2e below-threshold debit",
        });

    private static string BreakGlassJson(string principalId) =>
        JsonSerializer.Serialize(new
        {
            principalId,
            tenantCode = "CONTOSO",
            action = "bank.transaction.create",
            justification = "e2e break-glass smoke",
            durationMinutes = 60,
        });

    /// <summary>
    /// GETs <paramref name="url"/>. While the timeout window remains, retries on connection failure,
    /// a per-request client timeout, or a non-200 status. Returns the first 200; otherwise, once the
    /// window elapses, returns the most recent non-200 response received (so the caller can assert on
    /// it), or throws <see cref="TimeoutException"/> if the endpoint was never reachable within the
    /// window. The overall CTS deadline always propagates. Absorbs container/realm readiness and
    /// JWT-handler JWKS warm-up; a persistent non-200 surfaces as an assertion failure.
    /// </summary>
    private static async Task<HttpResponseMessage> GetUntilOkAsync(
        HttpClient client, string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        HttpResponseMessage? lastResponse = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    lastResponse?.Dispose();
                    return response;
                }

                // Retain the most recent non-200 so it can be returned (and asserted on) if the
                // window elapses before a 200 arrives.
                lastResponse?.Dispose();
                lastResponse = response;
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
                // endpoint not yet reachable — retry below
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                // per-request client timeout (HttpClient.Timeout), not the overall CTS deadline —
                // e.g. a slow Keycloak realm import can exceed the client timeout; retry below.
            }

            if (DateTime.UtcNow >= deadline)
            {
                return lastResponse ?? throw new TimeoutException(
                    $"GET {url} did not return any response within {timeout} (endpoint never reachable).");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    /// <summary>
    /// POSTs a JSON body and returns the first response received (any status — the auth path is warm
    /// by the time creates run, so 201/403/500 is the real outcome). While the timeout window remains,
    /// retries only on connection failure or a per-request client timeout (never on an HTTP status).
    /// If the endpoint was never reachable within the window, the last such exception propagates. The
    /// overall CTS deadline always propagates.
    /// </summary>
    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client, string url, string json, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                return await client.PostAsync(url, content, cancellationToken);
            }
            catch (HttpRequestException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                // per-request client timeout (HttpClient.Timeout), not the overall CTS deadline — retry.
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static IReadOnlyList<string> ReadStringArrayProperty(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                values.Add(value.GetString()!);
            }
        }

        return values;
    }

    private static int JsonArrayLength(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
    }

    private static string ReadStringProperty(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : string.Concat(value.AsSpan(0, 500), "…");

    /// <summary>
    /// Returns true if a TCP connection to <paramref name="host"/>:<paramref name="port"/> is
    /// accepted within a short timeout — i.e. something is already listening on the port.
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
}
