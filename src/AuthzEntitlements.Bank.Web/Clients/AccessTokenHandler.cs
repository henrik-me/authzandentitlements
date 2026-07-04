using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace AuthzEntitlements.Bank.Web.Clients;

// Forwards the signed-in user's OIDC access token to Bank.Api (through the edge
// gateway) as a Bearer credential. The token is read from the CURRENT HttpContext via
// GetTokenAsync("access_token"), which is why the pages that use the bank client render
// as static SSR: a static-SSR component executes during the HTTP request so HttpContext
// is non-null, whereas an Interactive Server circuit has no per-event HttpContext.
//
// Fail-closed: when HttpContext or the token is absent we proceed WITHOUT an
// Authorization header. The downstream gateway/API then answers 401/403 and the UI shows
// that outcome — we never fabricate or assume an identity.
public sealed class AccessTokenHandler(IHttpContextAccessor httpContextAccessor)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            var token = await httpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
