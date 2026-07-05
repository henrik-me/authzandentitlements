using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace AuthzEntitlements.AppHost.Tests;

/// <summary>
/// CS50 — CI guard for the AppHost application model. `dotnet build` compiles the
/// AppHost but never evaluates its <c>DistributedApplicationBuilder</c>, so wiring
/// defects (notably the CS48 duplicate-resource-name collision that only threw at
/// <c>aspire run</c>) passed CI undetected. Constructing the model here via
/// Aspire.Hosting.Testing — <c>CreateAsync</c> then <c>BuildAsync</c>, never
/// <c>StartAsync</c>, so the test stays Docker-free — makes those defects fail CI.
/// </summary>
public class AppHostApplicationModelSmokeTests
{
    [Fact]
    public async Task AppHost_application_model_builds_without_throwing()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        // Reaching here without an exception proves the app model + BuildAsync succeed.
        // NEVER call app.StartAsync() — that would require Docker and start containers.
    }

    [Fact]
    public async Task AppHost_resource_names_are_unique_case_insensitively()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        var names = appHost.Resources.Select(r => r.Name).ToList();

        var duplicates = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"Aspire resource names must be unique case-insensitively; duplicates: {string.Join(", ", duplicates)}");
    }
}
