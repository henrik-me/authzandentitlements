using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AuthzEntitlements.Bank.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS61 — the CLIENT-side handling of an expired/invalid access token, in-process (no Docker).
// The REAL BankApiClient runs against a stub handler that returns exactly what the JWT-bearer
// middleware returns for an expired token (401 + WWW-Authenticate invalid_token/expired), and the
// Accounts page must render the "your session has expired" notice instead of the generic
// "no accounts / not authorized (fail-closed)" one.
public sealed class SessionExpiredNoticeTests
{
    [Fact]
    public async Task Accounts_page_shows_session_expired_notice_on_401_invalid_token()
    {
        using var factory = new SessionExpiredFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/accounts");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your session has expired", html);
        Assert.Contains("Sign in again", html);
        // In Development the raw server-supplied WWW-Authenticate detail is surfaced for debugging.
        Assert.Contains("Server detail", html);
        Assert.Contains("The token expired at", html);
        // The generic "not authorized (fail-closed)" copy must NOT be shown for an expiry.
        Assert.DoesNotContain("No accounts are available", html);
    }

    [Fact]
    public async Task Account_detail_page_shows_session_expired_notice_on_401_invalid_token()
    {
        using var factory = new SessionExpiredFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/accounts/{Guid.NewGuid()}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your session has expired", html);
        // The generic fail-closed "unavailable / not authorized" copy must NOT be shown for an expiry.
        Assert.DoesNotContain("not authorized to read it", html);
    }

    [Fact]
    public async Task Server_error_detail_is_hidden_outside_development()
    {
        using var factory = new SessionExpiredFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/accounts");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The user-facing expiry notice still renders...
        Assert.Contains("Your session has expired", html);
        // ...but the raw server-supplied WWW-Authenticate error_description must NOT leak in Production.
        Assert.DoesNotContain("Server detail", html);
        Assert.DoesNotContain("The token expired at", html);
    }

    private sealed class SessionExpiredFactory(string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Keycloak:Authority", "http://localhost:5/realms/authz-bank-test");
            builder.UseEnvironment(environment);

            builder.ConfigureTestServices(services =>
            {
                // The OIDC handler is a request handler, so it is initialized on every request even
                // though "Test" is the default scheme. Outside Development the app sets
                // RequireHttpsMetadata=true, which rejects the unreachable http:// test authority at
                // PostConfigure time; relax it for the in-memory test host (no metadata is ever
                // fetched — "Test" performs authentication). ConfigureAll runs in the configure
                // phase after the app's per-scheme delegate and before the framework's validating
                // PostConfigure, so the override actually takes effect.
                services.ConfigureAll<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>(
                    o => o.RequireHttpsMetadata = false);

                // Authenticate every request as a signed-in user so [Authorize] pages render.
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Test";
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Replace IBankApiClient with the REAL BankApiClient wired to a direct stub handler
                // (bypassing Aspire service discovery / resilience), sharing the request-scoped
                // AuthChallengeState the page reads — so the actual WWW-Authenticate capture runs.
                services.AddScoped<IBankApiClient>(sp => new BankApiClient(
                    new HttpClient(new ExpiredTokenHandler()) { BaseAddress = new Uri("http://bank-api.test") },
                    sp.GetRequiredService<AuthChallengeState>()));
            });
        }
    }

    // Returns the exact shape the JWT-bearer middleware emits for an expired token.
    private sealed class ExpiredTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.TryAddWithoutValidation(
                "WWW-Authenticate",
                "Bearer error=\"invalid_token\", error_description=\"The token expired at '01/01/2026 00:00:00'\"");
            return Task.FromResult(response);
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "teller1"),
                new Claim("preferred_username", "teller1"),
                new Claim("tenant", "CONTOSO"),
                new Claim("roles", "Teller"),
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
