using System.Security.Cryptography;
using AuthzEntitlements.Bank.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// CS18 security hardening: proves the JWT bearer token-validation is fail-closed.
// Part A asserts the hardened TokenValidationParameters (tightened ClockSkew, required
// expiration, required signatures) plus regression guards on the existing invariants.
// Part B crafts tokens offline (no Keycloak/network) with JsonWebTokenHandler and drives
// them through the REAL configured parameters (only the signing material + issuer/audience
// are overridden for offline testability) to prove expired/forged/wrong-issuer/wrong-audience/
// non-expiring tokens are rejected. See docs/security/threat-model.md (Spoofing/Tampering).
public sealed class TokenValidationSecurityTests
{
    private const string TestIssuer = "https://test-issuer/realms/authz-bank";
    private const string TestAudience = "bank-api";

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static TokenValidationParameters BuildParameters()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBankJwtAuthentication(
            Config(("Keycloak:AuthServerUrl", "https://kc")), new TestHostEnvironment());
        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;
    }

    // ---- Part A: configuration assertions on the resolved TokenValidationParameters ----

    [Fact]
    public void ClockSkew_IsTightenedTo30Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), AuthenticationSetup.MaxClockSkew);
        var parameters = BuildParameters();
        Assert.Equal(AuthenticationSetup.MaxClockSkew, parameters.ClockSkew);
        // Explicitly below the lenient 5-minute .NET default.
        Assert.True(parameters.ClockSkew < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void RequireExpirationTime_IsEnabled()
    {
        Assert.True(BuildParameters().RequireExpirationTime);
    }

    [Fact]
    public void RequireSignedTokens_IsEnabled()
    {
        Assert.True(BuildParameters().RequireSignedTokens);
    }

    [Fact]
    public void ExistingValidationInvariants_StillHold()
    {
        var p = BuildParameters();
        Assert.True(p.ValidateIssuer);
        Assert.True(p.ValidateAudience);
        Assert.True(p.ValidateLifetime);
        Assert.True(p.ValidateIssuerSigningKey);
        Assert.Equal(TestAudience, p.ValidAudience);
    }

    // ---- Part B: functional token-rejection tests (offline, deterministic) ----

    // Clones the REAL configured parameters and overrides ONLY the signing material and
    // issuer/audience for offline testability, keeping the security-relevant params under
    // test (ClockSkew, ValidateLifetime, RequireExpirationTime, RequireSignedTokens,
    // ValidateIssuerSigningKey, ValidateIssuer, ValidateAudience) sourced from production.
    private static TokenValidationParameters OfflineParameters(SecurityKey signingKey)
    {
        var p = BuildParameters().Clone();
        p.IssuerSigningKey = signingKey;
        p.ValidIssuer = TestIssuer;
        p.ValidAudience = TestAudience;
        return p;
    }

    private static SymmetricSecurityKey NewKey() =>
        new(RandomNumberGenerator.GetBytes(32));

    private static string CreateToken(
        SecurityKey signingKey,
        string issuer = TestIssuer,
        string audience = TestAudience,
        DateTime? expires = null,
        bool includeExpiration = true)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = "test-user" },
        };

        if (includeExpiration)
        {
            descriptor.Expires = expires ?? DateTime.UtcNow.AddMinutes(5);
        }
        else
        {
            // Suppress the handler's default exp/iat/nbf so the token carries no exp claim.
            handler.SetDefaultTimesOnTokenCreation = false;
        }

        return handler.CreateToken(descriptor);
    }

    private static async Task<bool> IsValidAsync(string jwt, TokenValidationParameters parameters)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(jwt, parameters);
        return result.IsValid;
    }

    [Fact]
    public async Task ValidToken_IsAccepted()
    {
        var key = NewKey();
        var jwt = CreateToken(key);
        Assert.True(await IsValidAsync(jwt, OfflineParameters(key)));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var key = NewKey();
        // Well beyond the 30s ClockSkew in the past.
        var jwt = CreateToken(key, expires: DateTime.UtcNow.AddMinutes(-5));
        Assert.False(await IsValidAsync(jwt, OfflineParameters(key)));
    }

    [Fact]
    public async Task TamperedSignature_IsRejected()
    {
        var signingKey = NewKey();
        var jwt = CreateToken(signingKey);
        // Validate against a DIFFERENT key than the one that signed the token.
        Assert.False(await IsValidAsync(jwt, OfflineParameters(NewKey())));
    }

    [Fact]
    public async Task WrongAudience_IsRejected()
    {
        var key = NewKey();
        var jwt = CreateToken(key, audience: "not-bank-api");
        Assert.False(await IsValidAsync(jwt, OfflineParameters(key)));
    }

    [Fact]
    public async Task WrongIssuer_IsRejected()
    {
        var key = NewKey();
        var jwt = CreateToken(key, issuer: "https://evil-issuer");
        Assert.False(await IsValidAsync(jwt, OfflineParameters(key)));
    }

    [Fact]
    public async Task MissingExpiration_IsRejected()
    {
        var key = NewKey();
        var jwt = CreateToken(key, includeExpiration: false);
        Assert.False(await IsValidAsync(jwt, OfflineParameters(key)));
    }

    [Fact]
    public async Task TokenExpiredWithinOldDefaultButOutsideTightenedSkew_IsRejected()
    {
        var key = NewKey();
        // Expired ~2 minutes ago: accepted under the lenient 5-minute default, rejected
        // under the tightened 30s skew — demonstrates the hardening actually matters.
        var jwt = CreateToken(key, expires: DateTime.UtcNow.AddMinutes(-2));
        Assert.False(await IsValidAsync(jwt, OfflineParameters(key)));
    }
}
