using AuthzEntitlements.Governance.Service.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// CS29 — guards the JWT bearer wiring for the tenant-scoped governance request endpoints
// (GovernanceAuthenticationSetup). Mirrors Bank.Api's AuthenticationSetupTests: the
// MapInboundClaims / claim-type / audience / authority-resolution / HTTPS-metadata contract
// is validated here since synthetic-principal tests cannot observe the JwtBearer options.
public sealed class GovernanceAuthenticationSetupTests
{
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

    private static JwtBearerOptions BuildJwtOptions(
        IConfiguration config, IHostEnvironment? environment = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGovernanceJwtAuthentication(config, environment ?? new TestHostEnvironment());
        return services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void JwtBearer_DoesNotMapInboundClaims()
    {
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.False(options.MapInboundClaims);
    }

    [Fact]
    public void JwtBearer_UsesKeycloakRoleAndNameClaimTypes()
    {
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.Equal("roles", options.TokenValidationParameters.RoleClaimType);
        Assert.Equal("preferred_username", options.TokenValidationParameters.NameClaimType);
    }

    [Fact]
    public void JwtBearer_DefaultsAudienceToBankApi_WhenUnset()
    {
        // The forwarded Bank.Web user token carries the "bank-api" audience (realm bank-claims
        // scope). Governance validates that same audience by default (CS29 Decision 4).
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.Equal("bank-api", options.Audience);
        Assert.Equal("bank-api", options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void JwtBearer_HonoursConfiguredAudience()
    {
        var options = BuildJwtOptions(Config(
            ("Keycloak:AuthServerUrl", "http://localhost:8080"),
            ("Keycloak:Audience", "governance-service")));
        Assert.Equal("governance-service", options.Audience);
        Assert.Equal("governance-service", options.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public void JwtBearer_UsesTightenedClockSkew()
    {
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.Equal(GovernanceAuthenticationSetup.MaxClockSkew, options.TokenValidationParameters.ClockSkew);
        Assert.True(options.TokenValidationParameters.RequireExpirationTime);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
    }

    [Fact]
    public void ResolveAuthority_PrefersExplicitAuthority()
    {
        var config = Config(
            ("Keycloak:Authority", "http://kc/realms/authz-bank/"),
            ("Keycloak:AuthServerUrl", "http://other"));
        Assert.Equal("http://kc/realms/authz-bank", GovernanceAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_BuildsFromServerUrlAndRealm()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"), ("Keycloak:Realm", "authz-bank"));
        Assert.Equal("http://kc/realms/authz-bank", GovernanceAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_DefaultsRealm_WhenOnlyServerUrlConfigured()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"));
        Assert.Equal("http://kc/realms/authz-bank", GovernanceAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_ReturnsNull_WhenNothingConfigured()
    {
        Assert.Null(GovernanceAuthenticationSetup.ResolveAuthority(Config()));
    }

    [Fact]
    public void JwtBearer_AllowsHttpMetadata_InDevelopment()
    {
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void JwtBearer_RequiresHttpsMetadata_OutsideDevelopment()
    {
        var options = BuildJwtOptions(
            Config(("Keycloak:Authority", "https://kc/realms/authz-bank")),
            new TestHostEnvironment { EnvironmentName = Environments.Production });
        Assert.True(options.RequireHttpsMetadata);
    }

    [Fact]
    public void AddGovernanceJwtAuthentication_FailsClosed_WhenAuthorityMissingOutsideDevelopment()
    {
        // No authority + non-Development => throw rather than register a validator that can
        // never discover signing keys (fail closed).
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddGovernanceJwtAuthentication(
                Config(), new TestHostEnvironment { EnvironmentName = Environments.Production }));
    }
}
