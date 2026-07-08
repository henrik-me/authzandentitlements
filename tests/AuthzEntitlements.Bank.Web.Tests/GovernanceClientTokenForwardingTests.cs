using System.Net;
using System.Security.Claims;
using AuthzEntitlements.Bank.Web.Clients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS29 — the governance client now forwards the signed-in user's bearer token (its access-
// request endpoints are tenant-scoped and token-bound). This mirrors the bank-api client:
// when the AccessTokenHandler is in the governance client's pipeline, the user's token reaches
// governance-service; when there is no token, the request goes out unauthenticated (fail-closed
// — governance then answers 401/403 rather than the UI fabricating an identity).
public sealed class GovernanceClientTokenForwardingTests
{
    [Fact]
    public async Task GovernanceClient_ForwardsBearerToken_WhenUserHasAccessToken()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = GovernanceClientWithToken(stub, "gov-user-token");

        await client.GetRequestsAsync();

        Assert.Equal("/api/governance/requests", stub.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(stub.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", stub.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("gov-user-token", stub.LastRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GovernanceClient_SendsNoAuthorization_WhenUserHasNoToken()
    {
        var stub = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = GovernanceClientWithToken(stub, token: null);

        await client.GetRequestsAsync();

        Assert.Null(stub.LastRequest!.Headers.Authorization);
    }

    // Builds a GovernanceClient whose HttpClient pipeline is AccessTokenHandler -> stub, with an
    // HttpContext that resolves the given access_token via GetTokenAsync (null models "not signed
    // in / no token").
    private static IGovernanceClient GovernanceClientWithToken(StubHttpMessageHandler stub, string? token)
    {
        var handler = new AccessTokenHandler(AccessorWithToken(token)) { InnerHandler = stub };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://governance-service") };
        return new GovernanceClient(http, new AuthChallengeState());
    }

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
            result = AuthenticateResult.Success(new AuthenticationTicket(principal, props, "test"));
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
