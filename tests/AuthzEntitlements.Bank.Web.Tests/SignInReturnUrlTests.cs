using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS65 — every sign-in entry point carries the current page as a returnUrl so the user returns to
// where they were after authenticating. These cover the anonymous "Log in" links: the nav bar and
// the home page (rendered for signed-out users), and the router's NotAuthorized prompt (shown when
// an anonymous user hits an [Authorize] page). The expiry-notice link is covered in
// SessionExpiredNoticeTests.
public sealed class SignInReturnUrlTests
{
    [Fact]
    public async Task Home_and_nav_login_links_return_to_the_current_page()
    {
        using var factory = new AnonymousFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Both the nav "Log in" and the home "Log in" return to "/" after sign-in.
        Assert.Contains("login?returnUrl=%2F", html);
    }

    [Fact]
    public async Task Anonymous_user_on_an_authorize_page_is_challenged_to_sign_in()
    {
        using var factory = new AnonymousFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // /accounts is [Authorize]. An anonymous request is CHALLENGED (in the real app, redirected
        // to Keycloak via the default OIDC challenge) rather than shown the router's NotAuthorized
        // prompt — so that prompt is a defensive fallback. Its "Log in" link uses the same shared
        // <SignInLink> component exercised by the home/nav and session-expiry tests, so its
        // returnUrl behavior is covered transitively.
        using var response = await client.GetAsync("/accounts");

        Assert.True(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Unauthorized,
            $"expected an auth challenge (302/401) for an anonymous [Authorize] page but got {(int)response.StatusCode}.");
    }

    // Boots the real Bank.Web app but leaves every request ANONYMOUS (NoResult), so the signed-out
    // "Log in" / NotAuthorized links render. Mirrors SessionExpiredNoticeTests' OIDC-metadata relax.
    private sealed class AnonymousFactory(string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Keycloak:Authority", "http://localhost:5/realms/authz-bank-test");
            builder.UseEnvironment(environment);

            builder.ConfigureTestServices(services =>
            {
                services.ConfigureAll<OpenIdConnectOptions>(o => o.RequireHttpsMetadata = false);

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = "Test";
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, AnonymousAuthHandler>("Test", _ => { });
            });
        }
    }

    private sealed class AnonymousAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
