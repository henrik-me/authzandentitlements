using AuthzEntitlements.Bank.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Guards the JWT bearer wiring (AuthenticationSetup). The policy tests use synthetic
// ClaimsPrincipals and cannot observe the JwtBearer claim mapping, so the
// MapInboundClaims regression — which silently broke RequireRole end-to-end because
// the legacy handler rewrote "roles" to ClaimTypes.Role — is covered here.
public sealed class AuthenticationSetupTests
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

    private static JwtBearerOptions BuildJwtOptions(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBankJwtAuthentication(config, new TestHostEnvironment());
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
    public void ResolveAuthority_PrefersExplicitAuthority()
    {
        var config = Config(
            ("Keycloak:Authority", "http://kc/realms/authz-bank/"),
            ("Keycloak:AuthServerUrl", "http://other"));
        Assert.Equal("http://kc/realms/authz-bank", AuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_BuildsFromServerUrlAndRealm()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"), ("Keycloak:Realm", "authz-bank"));
        Assert.Equal("http://kc/realms/authz-bank", AuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_DefaultsRealm_WhenOnlyServerUrlConfigured()
    {
        var config = Config(("Keycloak:AuthServerUrl", "http://kc"));
        Assert.Equal("http://kc/realms/authz-bank", AuthenticationSetup.ResolveAuthority(config));
    }

    [Fact]
    public void ResolveAuthority_ReturnsNull_WhenNothingConfigured()
    {
        Assert.Null(AuthenticationSetup.ResolveAuthority(Config()));
    }
}
