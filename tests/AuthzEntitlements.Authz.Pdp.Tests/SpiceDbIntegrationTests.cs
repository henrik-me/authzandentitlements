using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Integration tests against a REAL SpiceDB server. They SOFT-SKIP (early return, no failure) when
// SPICEDB_TEST_ENDPOINT is unset, so the default `dotnet test` — which has no Docker/SpiceDB — stays
// green. To run them: start the `spicedb` container (or any SpiceDB gRPC endpoint) and set
// SPICEDB_TEST_ENDPOINT=http://localhost:50051 (and SPICEDB_TEST_PRESHARED_KEY to its preshared key).
// No Testcontainers / extra NuGet dependency is used.
//
// The forward-check catalog is the SHARED RebacScenarioCatalog.Forward — the very same scenarios the
// OpenFGA integration suite runs — so a green run here is a genuine head-to-head: SpiceDB and OpenFGA
// answer every account question identically from one seed graph.
public sealed class SpiceDbIntegrationTests
{
    private static SpiceDbCheckService? ServiceOrSkip()
    {
        var endpoint = Environment.GetEnvironmentVariable("SPICEDB_TEST_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null; // Soft-skip: no server configured (blank or whitespace-only).
        }

        var options = new SpiceDbOptions
        {
            Endpoint = endpoint,
            PresharedKey = Environment.GetEnvironmentVariable("SPICEDB_TEST_PRESHARED_KEY") ?? string.Empty,
        };
        return new SpiceDbCheckService(Options.Create(options));
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();
        await service.EnsureBootstrappedAsync(); // second call must not throw or duplicate (TOUCH).
    }

    [Fact]
    public async Task ForwardChecks_MatchCatalogExpectations()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();

        foreach (var s in RebacScenarioCatalog.Forward)
        {
            var allowed = await service.CheckAsync(s.UserId, s.Relation, s.ObjectId);
            Assert.True(allowed == s.ExpectAllowed, $"{s.Id}: expected {s.ExpectAllowed} got {allowed}");
        }
    }

    [Fact]
    public async Task BlankEndpoint_FailsClosed_WithClearMessage()
    {
        // This one does not need a server: it asserts the fail-closed behaviour when Endpoint is blank.
        var service = new SpiceDbCheckService(Options.Create(new SpiceDbOptions { Endpoint = "" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBootstrappedAsync());
        Assert.Contains("Endpoint", ex.Message);
    }
}
