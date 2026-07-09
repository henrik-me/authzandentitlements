using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS65 — end-to-end proof that /login threads the (sanitized) returnUrl into the OIDC challenge's
// post-login RedirectUri, so a later regression (dropping the sanitizer, or mis-binding the
// parameter) would fail a test, not just slip past a unit test of the helper. The challenge is
// short-circuited at the redirect-to-IdP event so no live Keycloak / metadata fetch is needed;
// the captured Properties.RedirectUri is surfaced on a response header the test asserts.
public sealed class LoginEndpointReturnUrlTests
{
    [Theory]
    [InlineData("/accounts", "/accounts")]
    [InlineData("/accounts/abc?tab=tx", "/accounts/abc?tab=tx")]
    [InlineData("//evil.com", "/")]            // open-redirect vector -> home
    [InlineData("/\t/evil.com", "/")]          // control-char bypass -> home
    [InlineData("https://evil.com", "/")]      // absolute URL -> home
    [InlineData("/login", "/")]                // auth-endpoint loop -> home
    [InlineData("/logout", "/")]               // auth-endpoint loop -> home
    [InlineData("", "/")]                       // present-but-empty returnUrl -> home
    public async Task Login_challenge_redirectUri_is_the_sanitized_returnUrl(string returnUrl, string expected)
    {
        using var factory = new LoginCaptureFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(expected, CapturedRedirectUri(response));
    }

    [Fact]
    public async Task Login_without_returnUrl_challenges_with_home()
    {
        using var factory = new LoginCaptureFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/login");

        Assert.Equal("/", CapturedRedirectUri(response));
    }

    private static string? CapturedRedirectUri(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Login-RedirectUri", out var values) ? values.FirstOrDefault() : null;

    private sealed class LoginCaptureFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Keycloak:Authority", "http://localhost:5/realms/authz-bank-test");
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                // Intercept the OIDC challenge that /login issues: capture the post-login RedirectUri
                // it passes to ChallengeAsync and short-circuit, so the assertion needs no live
                // Keycloak / OIDC round-trip. Every other authentication call delegates to the real
                // service.
                services.AddScoped<IAuthenticationService>(sp =>
                    new CapturingAuthenticationService(
                        ActivatorUtilities.CreateInstance<AuthenticationService>(sp)));
            });
        }
    }

    private sealed class CapturingAuthenticationService(IAuthenticationService inner) : IAuthenticationService
    {
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.Headers["X-Login-RedirectUri"] = properties?.RedirectUri ?? string.Empty;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            inner.AuthenticateAsync(context, scheme);

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            inner.ForbidAsync(context, scheme, properties);

        public Task SignInAsync(
            HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            inner.SignInAsync(context, scheme, principal, properties);

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            inner.SignOutAsync(context, scheme, properties);
    }
}
