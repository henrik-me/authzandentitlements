using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Options;
using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;
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
        if (string.IsNullOrWhiteSpace(url))
        {
            return null; // Soft-skip: no server configured (blank or whitespace-only).
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
    public async Task WhoCanAccess_ReturnsExactlyExpectedUsers()
    {
        var service = ServiceOrSkip();
        if (service is null) { return; }

        await service.EnsureBootstrappedAsync();

        foreach (var s in RebacScenarioCatalog.WhoCanAccess)
        {
            var users = await service.WhoCanAccessAsync(s.ObjectType, s.ObjectId, s.Relation);
            var expected = s.ExpectedUserIds.OrderBy(u => u, StringComparer.Ordinal).ToList();
            // Exact-set: WhoCanAccessAsync already returns user ids sorted ordinal, so compare the
            // full sequences — catches both a missing expected viewer and an unexpected extra one.
            Assert.Equal(expected, users);
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

    [Fact]
    public async Task PinnedModelId_Bootstraps_WithoutWritingANewModelVersion()
    {
        // LRN-031: a PINNED authorization-model id must bootstrap WITHOUT writing a new model version
        // (the per-boot growth this pin exists to stop) yet still seed tuples and answer forward checks.
        // The test writes a model directly (owning its id), records the model-version count, bootstraps a
        // service pinned to that id, and asserts the count is UNCHANGED and every forward scenario still
        // resolves. Self-skips offline like the others.
        var url = Environment.GetEnvironmentVariable("OPENFGA_TEST_API_URL");
        if (string.IsNullOrWhiteSpace(url)) { return; } // Soft-skip: no server configured.

        var storeName = $"authz-rebac-pin-{Guid.NewGuid():N}";
        using var admin = new OpenFgaClient(new ClientConfiguration { ApiUrl = url });
        var store = await admin.CreateStore(new ClientCreateStoreRequest { Name = storeName });
        admin.StoreId = store.Id;

        var modelRequest = JsonSerializer.Deserialize<ClientWriteAuthorizationModelRequest>(RebacModel.Json)
            ?? throw new InvalidOperationException("The embedded ReBAC model failed to parse.");
        var written = await admin.WriteAuthorizationModel(modelRequest);
        var pinnedModelId = written.AuthorizationModelId;

        var modelsBefore = (await admin.ReadAuthorizationModels()).AuthorizationModels.Count;

        var pinned = new OpenFgaRebacService(Options.Create(new OpenFgaOptions
        {
            ApiUrl = url,
            StoreName = storeName,
            AuthorizationModelId = pinnedModelId,
        }));
        await pinned.EnsureBootstrappedAsync();

        // The pin wrote NO new authorization-model version — the whole point of LRN-031.
        var modelsAfter = (await admin.ReadAuthorizationModels()).AuthorizationModels.Count;
        Assert.Equal(modelsBefore, modelsAfter);

        // ...and the pinned model still answers the forward-check catalog (seed tuples were reconciled).
        foreach (var s in RebacScenarioCatalog.Forward)
        {
            var allowed = await pinned.CheckAsync(
                $"{RebacTypes.User}:{s.UserId}", s.Relation, $"{RebacTypes.Account}:{s.ObjectId}");
            Assert.True(allowed == s.ExpectAllowed, $"{s.Id}: expected {s.ExpectAllowed} got {allowed}");
        }
    }
}
