using AuthzEntitlements.Authz.Pdp.Providers.Keto;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Integration tests against a REAL Keto server. They SOFT-SKIP (early return, no failure) when
// KETO_TEST_ENDPOINT is unset, so the default `dotnet test` — which has no Docker/Keto — stays green.
// To run them: start the `keto` container (or any Keto deployment) and set
// KETO_TEST_ENDPOINT=http://localhost:4466 (the read/check port). The write/relationship port defaults
// to http://localhost:4467 and can be overridden with KETO_WRITE_TEST_ENDPOINT. No Testcontainers /
// extra NuGet dependency is used.
//
// The forward-check catalog is the SHARED RebacScenarioCatalog.Forward — the very same scenarios the
// SpiceDB and OpenFGA integration suites run — so a green run here is a genuine head-to-head: all three
// engines answer every account question identically from one seed graph.
public sealed class KetoIntegrationTests
{
    private static KetoCheckService? ServiceOrSkip()
    {
        var readEndpoint = Environment.GetEnvironmentVariable("KETO_TEST_ENDPOINT");
        if (string.IsNullOrWhiteSpace(readEndpoint))
        {
            return null; // Soft-skip: no server configured (blank or whitespace-only).
        }

        var writeEndpoint = Environment.GetEnvironmentVariable("KETO_WRITE_TEST_ENDPOINT");
        if (string.IsNullOrWhiteSpace(writeEndpoint))
        {
            writeEndpoint = "http://localhost:4467";
        }

        var options = new KetoOptions { ReadEndpoint = readEndpoint, WriteEndpoint = writeEndpoint };
        return new KetoCheckService(Options.Create(options));
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent()
    {
        using var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();
        await service.EnsureBootstrappedAsync(); // second call no-ops (EnsureBootstrappedAsync short-circuits after the first success), so it never re-writes — Keto's PUT itself is append-only, not an upsert.
    }

    [Fact]
    public async Task ForwardChecks_MatchCatalogExpectations()
    {
        using var service = ServiceOrSkip();
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
        // This one does not need a server: it asserts the fail-closed behaviour when ReadEndpoint is blank.
        using var service = new KetoCheckService(Options.Create(new KetoOptions { ReadEndpoint = "" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureBootstrappedAsync());
        Assert.Contains("ReadEndpoint", ex.Message);
    }
}
