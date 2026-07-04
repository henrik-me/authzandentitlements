using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AuthzEntitlements.Edge.Gateway.Auth;

// JWT bearer authentication for the edge gateway. MIRRORS Bank.Api's
// AuthenticationSetup so the coarse edge validates exactly the same tokens the
// downstream API does (same authority/audience contract, same claim names). The
// Keycloak authority/audience are injected at runtime by the AppHost via config.
public static class GatewayAuthenticationSetup
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

    // CS18 hardening: tightened lifetime-validation clock skew. The .NET default is a
    // lenient 5 minutes, which widens the expired-token / replay acceptance window.
    // Host-local clocks in this lab are trivially in sync, so 30s is ample. Exposed as
    // a constant so security tests can assert it. See docs/security/threat-model.md
    // (Spoofing/Tampering). MIRRORS Bank.Api's AuthenticationSetup.MaxClockSkew.
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

    // Resolves the expected access-token audience from configuration, defaulting to
    // DefaultAudience when unset. Shared by the JWT bearer setup and the audit
    // middleware so both report the exact same audience.
    public static string ResolveAudience(IConfiguration config)
    {
        var audience = config[AudienceConfigKey];
        return string.IsNullOrWhiteSpace(audience) ? DefaultAudience : audience;
    }

    public static IServiceCollection AddGatewayJwtAuthentication(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment environment)
    {
        var authority = ResolveAuthority(config);

        var audience = ResolveAudience(config);

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
                // Keep Keycloak claims under their literal names (see Bank.Api): the
                // legacy handler would otherwise rewrite "roles"/"scope" and break
                // the coarse checks that read those exact claim names.
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
                    // CS18 hardening: shrink the replay/expired-token window, reject
                    // unsigned or non-expiring tokens, and validate the token signing key
                    // explicitly (defense against token forgery/replay). See
                    // docs/security/threat-model.md (Spoofing/Tampering).
                    ClockSkew = MaxClockSkew,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    RoleClaimType = GatewayClaims.RolesClaimType,
                    NameClaimType = GatewayClaims.NameClaimType,
                };
            });

        return services;
    }
}
