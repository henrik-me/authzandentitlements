// CS14: Blazor Web App (Interactive Server enabled) for the fintech product UI. This
// REPLACES the CS03 minimal-API login stub while PRESERVING its exact Keycloak/OIDC +
// cookie wiring (same config-key contract and options). Token-dependent pages render as
// static SSR so IHttpContextAccessor.HttpContext (and GetTokenAsync) are available on the
// request; anonymous-service widgets may opt into Interactive Server.

using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

const string OidcScheme = "oidc";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Keycloak/OIDC configuration. The AppHost injects these at runtime (do not hardcode
// a port). Config-key contract consumed here:
//   Keycloak:Authority     — full realm URL (e.g. http://localhost:8080/realms/authz-bank).
//   Keycloak:AuthServerUrl — Keycloak base URL, used when Authority is not supplied.
//   Keycloak:Realm         — realm name (default "authz-bank").
//   Keycloak:ClientSecret  — confidential-client secret (default "bank-web-secret").
var keycloak = builder.Configuration.GetSection("Keycloak");
var realm = keycloak["Realm"] ?? "authz-bank";
var authServerUrl = keycloak["AuthServerUrl"];
var authority = keycloak["Authority"];
if (string.IsNullOrWhiteSpace(authority))
{
    authority = string.IsNullOrWhiteSpace(authServerUrl)
        ? null
        : $"{authServerUrl.TrimEnd('/')}/realms/{realm}";
}

if (string.IsNullOrWhiteSpace(authority))
{
    // Fail fast: a relative/blank authority breaks OIDC discovery with a confusing
    // runtime error. The AppHost injects Keycloak:Authority at runtime.
    throw new InvalidOperationException(
        "Keycloak authority is not configured. Set 'Keycloak:Authority' (a full realm URL) " +
        "or 'Keycloak:AuthServerUrl'.");
}

var clientSecret = keycloak["ClientSecret"] ?? "bank-web-secret";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OidcScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(OidcScheme, options =>
    {
        options.Authority = authority;
        // Dev reaches Keycloak over plain HTTP; every other environment must require
        // HTTPS for the OIDC metadata/JWKS (mirrors Bank.Api's AuthenticationSetup).
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.ClientId = "bank-web";
        options.ClientSecret = clientSecret;
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.UsePkce = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("bank.read");
        // CS14: request the write scopes so the issued access token can call Bank.Api's
        // maker (transactions) and checker (approvals) endpoints through the gateway.
        // These are optional client scopes on the bank-web Keycloak client.
        options.Scope.Add("bank.transactions.write");
        options.Scope.Add("bank.approvals.write");
        // Identity claims (preferred_username, email, tenant, branch, roles) come from
        // the realm's default "bank-claims" client scope, so profile/email are not
        // requested — they are not defined as client scopes in the dev realm.

        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";

        // Keep Keycloak's literal claim names (roles/tenant/branch/preferred_username)
        // instead of the legacy JwtSecurityTokenHandler URIs, so RoleClaimType below
        // and the claims view resolve the real claims.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Scoped identity + OIDC-identity-to-Bank.Api-user resolver used by the product pages.
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Scoped, per-request capture of the most recent downstream 401 WWW-Authenticate challenge, so
// pages can distinguish an expired/invalid token (sign in again) from a plain authorization deny.
builder.Services.AddScoped<AuthChallengeState>();

// Attaches the signed-in user's bearer token to Bank.Api calls (bank client only).
builder.Services.AddTransient<AccessTokenHandler>();

// Typed clients. All base addresses use Aspire service discovery; AddServiceDefaults
// already added the resilience + discovery handlers. Bank.Api traffic is routed THROUGH
// the coarse edge gateway so that layer is exercised. Both the bank-api client and the
// governance client forward the signed-in user's token: CS29 made the governance access-
// request endpoints (create/list/decide) tenant-scoped and token-bound, so the user's
// bearer must reach governance-service. The entitlements/pdp services are anonymous (no token).
builder.Services.AddHttpClient<IBankApiClient, BankApiClient>(client =>
        client.BaseAddress = new Uri("https+http://edge-gateway"))
    .AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddHttpClient<IEntitlementsClient, EntitlementsClient>(client =>
    client.BaseAddress = new Uri("https+http://entitlements-service"));

builder.Services.AddHttpClient<IGovernanceClient, GovernanceClient>(client =>
        client.BaseAddress = new Uri("https+http://governance-service"))
    .AddHttpMessageHandler<AccessTokenHandler>();

builder.Services.AddHttpClient<IPdpClient, PdpClient>(client =>
    client.BaseAddress = new Uri("https+http://authz-pdp"));

// CS15 — Audit Explorer read model + chain verification. The audit API is anonymous in this lab
// (called intra-cluster by decision producers), so no token handler is attached.
builder.Services.AddHttpClient<IAuditClient, AuditClient>(client =>
    client.BaseAddress = new Uri("https+http://audit-service"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Serve wwwroot (app.css) and the framework static web assets — notably
// _framework/blazor.web.js, without which the Interactive Server island cannot
// hydrate. MapStaticAssets is the .NET 10 optimized static-asset pipeline.
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// OIDC login/logout endpoints. The OIDC middleware owns /signin-oidc and
// /signout-callback-oidc (the CallbackPath/SignedOutCallbackPath above).
app.MapGet("/login", () =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/" },
        [OidcScheme]));

app.MapGet("/logout", async (HttpContext httpContext) =>
{
    // RP-initiated logout only makes sense when there is an active OIDC session. If the local
    // session is already gone (cookie expired, a prior sign-out, or a lost id_token), the OIDC
    // handler would still redirect to Keycloak's end-session endpoint with a post_logout_redirect_uri
    // but NO id_token_hint (it sources the hint from GetTokenAsync(SignOutScheme,"id_token"), which is
    // now empty) — and Keycloak rejects that with "Missing parameters: id_token_hint". So when there
    // is no id_token to use as the hint, just clear any local cookie and return home instead of
    // bouncing through the identity provider. When authenticated, the id_token is present and the
    // normal sign-out carries id_token_hint correctly. Read the token from the cookie scheme
    // explicitly (where SaveTokens persists it, matching the handler's SignOutScheme) so the guard
    // is independent of the default-scheme configuration.
    var idToken = await httpContext.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "id_token");
    if (string.IsNullOrEmpty(idToken))
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.LocalRedirect("/");
    }

    return Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme]);
});

app.MapDefaultEndpoints();

app.Run();

// Exposes the implicitly-generated top-level Program class to the test project so
// WebApplicationFactory<Program> can boot the real Bank.Web app in-process (CS60 antiforgery
// integration test). Test-visibility only — no behavioural change.
public partial class Program;
