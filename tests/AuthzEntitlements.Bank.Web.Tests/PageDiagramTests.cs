using System.Net;
using System.Security.Claims;
using System.Text;
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

// CS62 — verifies the per-page AuthFlowDiagram actually renders into the page markup (in-process,
// no Docker), colour-coding AuthN/AuthZ and naming the backend hops it describes.
public sealed class PageDiagramTests
{
    [Fact]
    public async Task Accounts_page_renders_the_data_flow_diagram_with_authn_and_authz()
    {
        using var factory = new DiagramFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/accounts");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Data flow &amp; authorization", html);
        Assert.Contains("class=\"flow-node\"", html);
        Assert.Contains("Edge Gateway", html);
        Assert.Contains("Bank.Api", html);
        // Colour-coded AuthN + AuthZ badges are both present.
        Assert.Contains("flow-badge authn", html);
        Assert.Contains("flow-badge authz", html);
        // The AuthN story explicitly surfaces the token + WWW-Authenticate handling.
        Assert.Contains("WWW-Authenticate", html);
    }

    private sealed class DiagramFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Keycloak:Authority", "http://localhost:5/realms/authz-bank-test");
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Test";
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // The diagram is static markup, so any benign API response renders it; return an
                // empty account list (200) so /accounts renders normally.
                services.AddScoped<IBankApiClient>(sp => new BankApiClient(
                    new HttpClient(new OkEmptyJsonHandler()) { BaseAddress = new Uri("http://bank-api.test") },
                    sp.GetRequiredService<AuthChallengeState>()));
            });
        }
    }

    private sealed class OkEmptyJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
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
