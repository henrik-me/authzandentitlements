using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;
using OpenFga.Sdk.Model;

namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// The live OpenFGA integration: lazy client construction, idempotent bootstrap (store +
// authorization model + seed tuples), the forward Check the provider bridges to, and the two
// reverse-index queries that answer the CS07 exit criterion ("who can view account X" /
// "what can user Y access"). Registered as a singleton so the store/model ids are cached and
// bootstrap runs once per process. Everything here is async; OpenFgaProvider.Evaluate bridges
// with GetAwaiter().GetResult() because the CS05 IAuthorizationDecisionProvider contract is sync.
//
// Implements IOpenFgaCheckClient (LRN-038): OpenFgaProvider depends on that narrow forward-Check
// seam, not this concrete type, so the ReBAC permit/deny explanation is unit-testable offline via a
// test double. The reverse-index queries stay on the concrete service (used by RebacEndpoints).
//
// Fails closed on configuration: the client is built only on first actual use, and if ApiUrl is
// blank the first call throws a clear "start the openfga container / set Pdp:Provider=openfga"
// error — DI registration and the default deterministic run never touch a server.
public sealed class OpenFgaRebacService : IOpenFgaCheckClient
{
    private readonly OpenFgaOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Both volatile so the fast-path (pre-semaphore) read of _bootstrapped safely publishes _client:
    // the volatile write of _client before the volatile write of _bootstrapped guarantees a thread
    // that observes _bootstrapped==true also sees the fully-constructed client (no null publication).
    private volatile OpenFgaClient? _client;
    private volatile bool _bootstrapped;

    public OpenFgaRebacService(IOptions<OpenFgaOptions> options)
    {
        _options = options.Value;
    }

    // Idempotent: create-or-find the store, write the authorization model, and write any seed
    // tuples not already present. Safe to call repeatedly and from concurrent requests.
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        if (_bootstrapped)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_bootstrapped)
            {
                return;
            }

            var client = BuildClient();
            await EnsureStoreAsync(client, cancellationToken).ConfigureAwait(false);
            await EnsureModelAsync(client, cancellationToken).ConfigureAwait(false);
            await WriteMissingTuplesAsync(client, cancellationToken).ConfigureAwait(false);

            _client = client;
            _bootstrapped = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Forward check: does user have relation on object? object is a full "type:id" string.
    public async Task<bool> CheckAsync(
        string user, string relation, string @object, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrappedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _client!.Check(
            new ClientCheckRequest { User = user, Relation = relation, Object = @object },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Allowed ?? false;
    }

    // Reverse index — "who can access object X": the user ids OpenFGA resolves for
    // (objectType:objectId, relation). Only concrete users are returned (usersets/wildcards skipped).
    public async Task<IReadOnlyList<string>> WhoCanAccessAsync(
        string objectType, string objectId, string relation, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrappedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _client!.ListUsers(
            new ClientListUsersRequest
            {
                Object = new FgaObject { Type = objectType, Id = objectId },
                Relation = relation,
                UserFilters = [new UserTypeFilter { Type = RebacTypes.User }],
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Users
            .Where(u => u.Object is not null
                && string.Equals(u.Object.Type, RebacTypes.User, StringComparison.Ordinal))
            .Select(u => u.Object!.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    // Reverse index — "what can user Y access": the object ids (of objectType) OpenFGA resolves for
    // (user:userId, relation). Ids are returned bare ("acme-checking"), not "account:acme-checking".
    public async Task<IReadOnlyList<string>> WhatCanUserAccessAsync(
        string userId, string relation, string objectType, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrappedAsync(cancellationToken).ConfigureAwait(false);
        var response = await _client!.ListObjects(
            new ClientListObjectsRequest
            {
                User = $"{RebacTypes.User}:{userId}",
                Relation = relation,
                Type = objectType,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var prefix = $"{objectType}:";
        return response.Objects
            .Select(o => o.StartsWith(prefix, StringComparison.Ordinal) ? o[prefix.Length..] : o)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private OpenFgaClient BuildClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiUrl))
        {
            throw new InvalidOperationException(
                "OpenFGA ApiUrl is not configured. Set \"Pdp:OpenFga:ApiUrl\" (and \"Pdp:Provider\" " +
                "to \"openfga\"); under `aspire run` the AppHost injects it as Pdp__OpenFga__ApiUrl " +
                "once the 'openfga' container is started.");
        }

        return new OpenFgaClient(new ClientConfiguration { ApiUrl = _options.ApiUrl });
    }

    private async Task EnsureStoreAsync(OpenFgaClient client, CancellationToken cancellationToken)
    {
        var stores = await client.ListStores(
            new ClientListStoresRequest { Name = _options.StoreName },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var existing = stores.Stores.FirstOrDefault(
            s => string.Equals(s.Name, _options.StoreName, StringComparison.Ordinal));

        var storeId = existing?.Id;
        if (storeId is null)
        {
            var created = await client.CreateStore(
                new ClientCreateStoreRequest { Name = _options.StoreName },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            storeId = created.Id;
        }

        client.StoreId = storeId;
    }

    // Resolve the authorization model the store's checks run against. When an AuthorizationModelId is
    // configured (LRN-031) we PIN it — set it on the client and skip the write — so a persistent
    // shared store does not accrue a new immutable model VERSION on every boot. Unset (the default)
    // preserves the original write-then-pin: write the exact embedded CS07 model and pin the id the
    // server returns, so a fresh store is always bootstrapped to the CS07 model even if an older
    // version exists.
    private async Task EnsureModelAsync(OpenFgaClient client, CancellationToken cancellationToken)
    {
        if (ResolvePinnedModelId(_options) is { } pinnedModelId)
        {
            client.AuthorizationModelId = pinnedModelId;
            return;
        }

        var request = JsonSerializer.Deserialize<ClientWriteAuthorizationModelRequest>(RebacModel.Json)
            ?? throw new InvalidOperationException("The embedded ReBAC authorization model failed to parse.");
        var response = await client.WriteAuthorizationModel(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        client.AuthorizationModelId = response.AuthorizationModelId;
    }

    // The configured authorization-model id to PIN (skip the per-boot model write), or null to
    // write-then-pin the embedded model. A blank/whitespace-only value is treated as unset so a
    // misconfigured empty string falls back to write-then-pin (fail-safe — never pins a bogus id).
    // Pure and static so the pin decision is unit-testable without a live server.
    public static string? ResolvePinnedModelId(OpenFgaOptions options) =>
        string.IsNullOrWhiteSpace(options.AuthorizationModelId) ? null : options.AuthorizationModelId.Trim();

    private static async Task WriteMissingTuplesAsync(OpenFgaClient client, CancellationToken cancellationToken)
    {
        // Targeted existence reconciliation (LRN-031): probe each seed tuple by its exact
        // (user, relation, object) key rather than paging the ENTIRE store into memory. On a
        // persistent shared store this stays O(seed) small reads of at most one tuple each instead of
        // scanning every unrelated tuple that may have accumulated.
        //
        // The `queued` set dedups within the seed list as well as against the store: OpenFGA's Write
        // is not idempotent and errors on a duplicate, so a key already queued (a duplicate seed row)
        // is skipped before it is probed or written a second time.
        var queued = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<ClientTupleKey>();
        foreach (var t in RebacSeedTuples.Tuples)
        {
            if (!queued.Add(TupleKey(t.User, t.Relation, t.Object)))
            {
                continue;
            }

            if (!await TupleExistsAsync(client, t, cancellationToken).ConfigureAwait(false))
            {
                missing.Add(new ClientTupleKey { User = t.User, Relation = t.Relation, Object = t.Object });
            }
        }

        if (missing.Count == 0)
        {
            return;
        }

        await client.Write(new ClientWriteRequest { Writes = missing }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> TupleExistsAsync(
        OpenFgaClient client, RebacTuple tuple, CancellationToken cancellationToken)
    {
        // A fully-specified Read (user + relation + object) returns the matching tuple if present and
        // an empty page otherwise — a targeted existence check, not a full-store scan.
        var response = await client.Read(
            new ClientReadRequest { User = tuple.User, Relation = tuple.Relation, Object = tuple.Object },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Tuples.Count > 0;
    }

    private static string TupleKey(string user, string relation, string @object) =>
        $"{user}|{relation}|{@object}";
}
