using System.Security.Claims;
using AuthzEntitlements.Bank.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class AccessTokenHandlerTests
{
    [Fact]
    public async Task Attaches_bearer_header_when_access_token_present()
    {
        var accessor = AccessorWithToken("tok-abc123");
        var handler = new AccessTokenHandler(accessor)
        {
            InnerHandler = new StubHttpMessageHandler(
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://edge-gateway/api/accounts");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tok-abc123", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Omits_header_when_no_token_present()
    {
        var accessor = AccessorWithToken(null);
        var handler = new AccessTokenHandler(accessor)
        {
            InnerHandler = new StubHttpMessageHandler(
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://edge-gateway/api/accounts");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Omits_header_when_no_http_context()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var handler = new AccessTokenHandler(accessor)
        {
            InnerHandler = new StubHttpMessageHandler(
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://edge-gateway/api/accounts");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        Assert.Null(request.Headers.Authorization);
    }

    // Builds an accessor whose HttpContext resolves the given access_token via
    // GetTokenAsync (a null token models "not signed in / no token").
    private static IHttpContextAccessor AccessorWithToken(string? token)
    {
        AuthenticateResult result;
        if (token is null)
        {
            result = AuthenticateResult.NoResult();
        }
        else
        {
            var props = new AuthenticationProperties();
            props.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));
            result = AuthenticateResult.Success(
                new AuthenticationTicket(principal, props, "test"));
        }

        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new StubAuthenticationService(result));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class StubAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(result);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }
}
