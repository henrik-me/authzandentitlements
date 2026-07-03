// CS03 stub: minimal ASP.NET Core web app wiring OIDC authorization-code login
// against Keycloak and displaying the signed-in user's claims. This is a login
// stub only — CS14 builds the real Blazor product UI on top of this identity wiring.

using System.Net;
using System.Text;
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
var authority = keycloak["Authority"]
    ?? $"{keycloak["AuthServerUrl"]?.TrimEnd('/')}/realms/{realm}";
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
        options.RequireHttpsMetadata = false; // dev-only: Keycloak runs over http in the lab.
        options.ClientId = "bank-web";
        options.ClientSecret = clientSecret;
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.UsePkce = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("bank.read");
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

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (HttpContext ctx) =>
{
    var user = ctx.User;
    var signedIn = user.Identity?.IsAuthenticated == true;
    var who = signedIn
        ? WebUtility.HtmlEncode(user.Identity!.Name ?? "(unknown)")
        : null;

    var status = signedIn
        ? $"<p>Signed in as <strong>{who}</strong>.</p>"
        : "<p>You are <strong>not signed in</strong>.</p>";

    var body = new StringBuilder()
        .Append("<h1>Bank.Web (CS03 OIDC login stub)</h1>")
        .Append(status)
        .Append("<ul>")
        .Append("<li><a href=\"/login\">Log in</a></li>")
        .Append("<li><a href=\"/claims\">View my claims</a></li>")
        .Append("<li><a href=\"/logout\">Log out</a></li>")
        .Append("</ul>")
        .ToString();

    return Results.Content(HtmlPage("Bank.Web", body), "text/html");
});

app.MapGet("/login", () =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/claims" },
        [OidcScheme]));

app.MapGet("/claims", (HttpContext ctx) =>
{
    var user = ctx.User;

    string First(string type) =>
        WebUtility.HtmlEncode(user.FindFirst(type)?.Value ?? "(none)");

    string All(string type)
    {
        var values = user.FindAll(type).Select(c => c.Value).ToArray();
        return values.Length == 0
            ? "(none)"
            : WebUtility.HtmlEncode(string.Join(", ", values));
    }

    var highlights = new StringBuilder()
        .Append("<h2>Identity</h2>")
        .Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\">")
        .Append($"<tr><th>preferred_username</th><td>{First("preferred_username")}</td></tr>")
        .Append($"<tr><th>name</th><td>{First("name")}</td></tr>")
        .Append($"<tr><th>tenant</th><td>{First("tenant")}</td></tr>")
        .Append($"<tr><th>branch</th><td>{First("branch")}</td></tr>")
        .Append($"<tr><th>roles</th><td>{All("roles")}</td></tr>")
        .Append($"<tr><th>scope</th><td>{First("scope")}</td></tr>")
        .Append("</table>");

    var allRows = new StringBuilder("<h2>All claims</h2>")
        .Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\">")
        .Append("<tr><th>Type</th><th>Value</th></tr>");
    foreach (var claim in user.Claims)
    {
        allRows.Append("<tr><td>")
            .Append(WebUtility.HtmlEncode(claim.Type))
            .Append("</td><td>")
            .Append(WebUtility.HtmlEncode(claim.Value))
            .Append("</td></tr>");
    }
    allRows.Append("</table>");

    var body = new StringBuilder()
        .Append("<h1>Your claims</h1>")
        .Append(highlights)
        .Append(allRows)
        .Append("<p><a href=\"/\">Home</a> &middot; <a href=\"/logout\">Log out</a></p>")
        .ToString();

    return Results.Content(HtmlPage("Claims", body), "text/html");
})
.RequireAuthorization();

app.MapGet("/logout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OidcScheme]));

app.MapDefaultEndpoints();

app.Run();

static string HtmlPage(string title, string body) =>
    $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title></head><body>{body}</body></html>";
