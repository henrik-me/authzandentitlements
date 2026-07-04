using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AuthzEntitlements.Bank.Api.Auth;

// JWT bearer authentication for Keycloak-issued access tokens. The Keycloak
// authority/audience are supplied at runtime by the AppHost via configuration;
// this type owns the exact config-key contract the AppHost must inject.
public static class AuthenticationSetup
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
    public const string DefaultAudience = "bank-api";

    // Custom Keycloak protocol-mapper claim types carried in the access token.
    public const string RolesClaimType = "roles";
    public const string NameClaimType = "preferred_username";

    // CS18 hardening: tightened lifetime-validation clock skew. The .NET default is a
    // lenient 5 minutes, which widens the expired-token / replay acceptance window.
    // Host-local clocks in this lab are trivially in sync, so 30s is ample. Exposed as
    // a constant so security tests can assert it. See docs/security/threat-model.md
    // (Spoofing/Tampering). Mirrored in Edge.Gateway's GatewayAuthenticationSetup.
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);

    // Resolves the token issuer/authority from configuration, applying the
    // documented precedence: an explicit full realm URL wins; otherwise the
    // authority is constructed from the base URL + realm. Returns null when
    // neither is configured so callers can fail-closed as appropriate.
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

    public static IServiceCollection AddBankJwtAuthentication(
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
            // Fail-closed outside Development: without an authority the token
            // signing keys cannot be discovered and validation is impossible.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Keycloak authority is not configured. Set 'Keycloak:Authority' " +
                    "(a full realm URL) or 'Keycloak:AuthServerUrl' (+ optional " +
                    "'Keycloak:Realm'). The AppHost injects these at runtime.");
            }

            // Development: warn but still register. The AppHost always injects the
            // authority; a missing value here means the app was launched standalone.
            Console.Error.WriteLine(
                "[warning] Keycloak authority is not configured; JWT bearer will " +
                "reject all tokens until 'Keycloak:Authority' or 'Keycloak:AuthServerUrl' is set.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keycloak carries roles/tenant/etc. under their literal claim names.
                // Leave inbound claims unmapped so the legacy JwtSecurityTokenHandler
                // map does NOT rewrite "roles" -> ClaimTypes.Role (which would break
                // RequireRole, since RoleClaimType below is the literal "roles").
                options.MapInboundClaims = false;

                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                }

                options.Audience = audience;

                // Dev reaches Keycloak over plain HTTP; every other environment must
                // fail closed and require HTTPS for the issuer metadata/JWKS.
                options.RequireHttpsMetadata = !environment.IsDevelopment();

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(authority),
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    // CS18 hardening: shrink the replay/expired-token window and reject
                    // unsigned or non-expiring tokens (defense against token forgery and
                    // non-expiring tokens). See docs/security/threat-model.md
                    // (Spoofing/Tampering).
                    ClockSkew = MaxClockSkew,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    RoleClaimType = RolesClaimType,
                    NameClaimType = NameClaimType,
                };
            });

        return services;
    }
}
