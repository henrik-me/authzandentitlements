using AuthzEntitlements.Edge.Gateway.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Edge.Gateway.Tests;

// Guards the gateway JWT bearer wiring. Mirrors Bank.Api's AuthenticationSetup
// tests: the edge must validate the SAME tokens the API does (MapInboundClaims
// off, Keycloak claim names, authority precedence, HTTPS-metadata fail-closed).
public sealed class GatewayAuthenticationSetupTests
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
        services.AddGatewayJwtAuthentication(config, environment ?? new TestHostEnvironment());
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
        var options = BuildJwtOptions(Config(("Keycloak:AuthServerUrl", "http://localhost:8080")));
        Assert.Equal("bank-api", options.Audience);
        Assert.Equal("bank-api", options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public void JwtBearer_UsesConfiguredAudience_WhenSet()
    {
        var options = BuildJwtOptions(Config(
            ("Keycloak:AuthServerUrl", "http://localhost:8080"),
            ("Keycloak:Audience", "custom-aud")));
        Assert.Equal("custom-aud", options.Audience);
    }

    [Fact]
    public void ResolveAuthority_PrefersExplicitAuthority()
    {
        var config = Config(
            ("Keycloak:Authority", "http://kc/realms/authz-bank/"),
            ("Keycloak:AuthServerUrl", "http://other"));
        Assert.Equal("http://kc/realms/authz-bank", GatewayAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_BuildsFromServerUrlAndRealm()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"), ("Keycloak:Realm", "authz-bank"));
        Assert.Equal("http://kc/realms/authz-bank", GatewayAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_DefaultsRealm_WhenOnlyServerUrlConfigured()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"));
        Assert.Equal("http://kc/realms/authz-bank", GatewayAuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_ReturnsNull_WhenNothingConfigured()
    {
        Assert.Null(GatewayAuthenticationSetup.ResolveAuthority(Config()));
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
    public void AddGatewayJwtAuthentication_Throws_WhenAuthorityMissingOutsideDevelopment()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddGatewayJwtAuthentication(
                Config(),
                new TestHostEnvironment { EnvironmentName = Environments.Production }));
    }
}
