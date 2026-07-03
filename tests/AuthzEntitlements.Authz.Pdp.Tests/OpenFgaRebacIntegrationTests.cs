using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Integration tests against a REAL OpenFGA server. They SOFT-SKIP (early return, no failure) when
// OPENFGA_TEST_API_URL is unset, so the default `dotnet test` — which has no Docker/OpenFGA — stays
// green. To run them: start the `openfga` container (or any OpenFGA HTTP endpoint) and set
// OPENFGA_TEST_API_URL=http://localhost:8080. No Testcontainers / extra NuGet dependency is used.
public sealed class OpenFgaRebacIntegrationTests
{
    private static OpenFgaRebacService? ServiceOrSkip()
    {
        var url = Environment.GetEnvironmentVariable("OPENFGA_TEST_API_URL");
        if (string.IsNullOrEmpty(url))
        {
            return null; // Soft-skip: no server configured.
        }

        // Unique store per run so repeated integration runs don't collide on the shared server.
        var options = new OpenFgaOptions { ApiUrl = url, StoreName = $"authz-rebac-it-{Guid.NewGuid():N}" };
        return new OpenFgaRebacService(Options.Create(options));
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();
        await service.EnsureBootstrappedAsync(); // second call must not throw or duplicate.
    }

    [Fact]
    public async Task ForwardChecks_MatchCatalogExpectations()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();

        foreach (var s in RebacScenarioCatalog.Forward)
        {
            var allowed = await service.CheckAsync(
                $"{RebacTypes.User}:{s.UserId}", s.Relation, $"{RebacTypes.Account}:{s.ObjectId}");
            Assert.True(allowed == s.ExpectAllowed, $"{s.Id}: expected {s.ExpectAllowed} got {allowed}");
        }
    }

    [Fact]
    public async Task WhoCanAccess_ReturnsExpectedUsers()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();

        foreach (var s in RebacScenarioCatalog.WhoCanAccess)
        {
            var users = await service.WhoCanAccessAsync(s.ObjectType, s.ObjectId, s.Relation);
            foreach (var expected in s.ExpectedUserIds)
            {
                Assert.Contains(expected, users);
            }
        }
    }

    [Fact]
    public async Task WhatCanUserAccess_ReturnsExpectedObjects_AndExcludesOthers()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();

        foreach (var s in RebacScenarioCatalog.WhatCanUserAccess)
        {
            var objects = await service.WhatCanUserAccessAsync(s.UserId, s.Relation, s.ObjectType);
            foreach (var expected in s.ExpectedObjectIds)
            {
                Assert.Contains(expected, objects);
            }

            foreach (var excluded in s.ExcludedObjectIds)
            {
                Assert.DoesNotContain(excluded, objects);
            }
        }
    }

    [Fact]
    public async Task BlankApiUrl_FailsClosed_WithClearMessage()
    {
        // This one does not need a server: it asserts the fail-closed behaviour when ApiUrl is blank.
        var service = new OpenFgaRebacService(Options.Create(new OpenFgaOptions { ApiUrl = "" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBootstrappedAsync());
        Assert.Contains("ApiUrl", ex.Message);
    }
}
