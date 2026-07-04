using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AuthzEntitlements.Governance.Service.Auth;

// CS29 — JWT bearer authentication for the tenant-scoped governance request endpoints.
// Mirrors Bank.Api's AuthenticationSetup: the Keycloak authority/audience are supplied at
// runtime by the AppHost via configuration; this type owns the exact config-key contract
// the AppHost must inject. Only the access-request endpoints require this token; every other
// governance endpoint stays anonymous (see GovernanceEndpoints).
public static class GovernanceAuthenticationSetup
{
    // Integration contract: config keys the AppHost injects (env or appsettings).
    // A full realm URL, e.g. "http://keycloak:8080/realms/authz-bank".
    public const string AuthorityConfigKey = "Keycloak:Authority";

    // The Keycloak base URL (no realm segment), e.g. "http://keycloak:8080".
    public const string AuthServerUrlConfigKey = "Keycloak:AuthServerUrl";

    // The realm name; defaults to DefaultRealm when unset.
    public const string RealmConfigKey = "Keycloak:Realm";

    // The expected access-token audience; defaults to DefaultAudience when unset.
    public const string AudienceConfigKey = "Keycloak:Audience";

    public const string DefaultRealm = "authz-bank";

    // The forwarded caller is the signed-in Bank.Web user, whose Keycloak access token is
    // stamped with the "bank-api" audience by the realm's bank-claims client scope
    // (aud-bank-api mapper). Governance validates that SAME audience so the least-invasive
    // change accepts the existing token with no realm change (CS29 Decision 4). See
    // docs/governance/tenant-scoping.md.
    public const string DefaultAudience = "bank-api";

    // Custom Keycloak protocol-mapper claim types carried in the access token. The tenant
    // scoping reads the "tenant" claim (see GovernanceTenantClaims).
    public const string RolesClaimType = "roles";
    public const string NameClaimType = "preferred_username";

    // Tightened lifetime-validation clock skew, mirroring Bank.Api/Edge.Gateway. The .NET
    // default is a lenient 5 minutes, which widens the expired-token / replay window. Exposed
    // as a constant so security tests can assert it.
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);

    // Resolves the token issuer/authority from configuration, applying the documented
    // precedence: an explicit full realm URL wins; otherwise the authority is constructed
    // from the base URL + realm. Returns null when neither is configured so callers can
    // fail-closed as appropriate.
    public static string? ResolveAuthority(IConfiguration config)
    {
        var authority = config[AuthorityConfigKey];
        if (!string.IsNullOrWhiteSpace(authority))
        {
            return authority.TrimEnd('/');
        }

        var authServerUrl = config[AuthServerUrlConfigKey];
        if (!string.IsNullOrWhiteSpace(authServerUrl))
        {
            var realm = config[RealmConfigKey];
            if (string.IsNullOrWhiteSpace(realm))
            {
                realm = DefaultRealm;
            }

            return $"{authServerUrl.TrimEnd('/')}/realms/{realm}";
        }

        return null;
    }

    public static IServiceCollection AddGovernanceJwtAuthentication(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment environment)
    {
        var authority = ResolveAuthority(config);

        var audience = config[AudienceConfigKey];
        if (string.IsNullOrWhiteSpace(audience))
        {
            audience = DefaultAudience;
        }

        if (string.IsNullOrWhiteSpace(authority))
        {
            // Fail-closed outside Development: without an authority the token signing keys
            // cannot be discovered and validation is impossible.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Keycloak authority is not configured. Set 'Keycloak:Authority' " +
                    "(a full realm URL) or 'Keycloak:AuthServerUrl' (+ optional " +
                    "'Keycloak:Realm'). The AppHost injects these at runtime.");
            }

            // Development: warn but still register. The AppHost always injects the authority;
            // a missing value here means the app was launched standalone.
            Console.Error.WriteLine(
                "[warning] Keycloak authority is not configured; JWT bearer will " +
                "reject all tokens until 'Keycloak:Authority' or 'Keycloak:AuthServerUrl' is set.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keycloak carries roles/tenant/etc. under their literal claim names. Leave
                // inbound claims unmapped so the legacy JwtSecurityTokenHandler map does NOT
                // rewrite them (LRN-010); the tenant scoping reads the literal "tenant" claim.
                options.MapInboundClaims = false;

                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                }

                options.Audience = audience;

                // Dev reaches Keycloak over plain HTTP; every other environment must fail
                // closed and require HTTPS for the issuer metadata/JWKS.
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    // Shrink the replay/expired-token window, reject unsigned or non-expiring
                    // tokens, and validate the signing key explicitly (token-forgery defense).
                    ClockSkew = MaxClockSkew,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = RolesClaimType,
                    NameClaimType = NameClaimType,
                };
            });

        return services;
    }
}
