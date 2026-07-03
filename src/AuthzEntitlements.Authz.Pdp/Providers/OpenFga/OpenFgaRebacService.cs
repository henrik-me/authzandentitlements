using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
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
// Fails closed on configuration: the client is built only on first actual use, and if ApiUrl is
// blank the first call throws a clear "start the openfga container / set Pdp:Provider=openfga"
// error — DI registration and the default deterministic run never touch a server.
public sealed class OpenFgaRebacService
{
    private readonly OpenFgaOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OpenFgaClient? _client;
    private bool _bootstrapped;

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
            await WriteModelAsync(client, cancellationToken).ConfigureAwait(false);
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
                "OpenFGA ApiUrl is not configured. Start the 'openfga' container and set " +
                "\"Pdp:Provider\" to \"openfga\" (the AppHost injects Pdp__OpenFga__ApiUrl when the " +
                "container runs).");
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

    private static async Task WriteModelAsync(OpenFgaClient client, CancellationToken cancellationToken)
    {
        // Authorization models are immutable/versioned; writing our exact model and pinning its id
        // guarantees checks run against the CS07 model even if an older version exists in the store.
        var request = JsonSerializer.Deserialize<ClientWriteAuthorizationModelRequest>(RebacModel.Json)
            ?? throw new InvalidOperationException("The embedded ReBAC authorization model failed to parse.");
        var response = await client.WriteAuthorizationModel(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        client.AuthorizationModelId = response.AuthorizationModelId;
    }

    private static async Task WriteMissingTuplesAsync(OpenFgaClient client, CancellationToken cancellationToken)
    {
        var existing = await ReadAllTupleKeysAsync(client, cancellationToken).ConfigureAwait(false);

        var missing = RebacSeedTuples.Tuples
            .Where(t => existing.Add(TupleKey(t.User, t.Relation, t.Object)))
            .Select(t => new ClientTupleKey { User = t.User, Relation = t.Relation, Object = t.Object })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        await client.Write(new ClientWriteRequest { Writes = missing }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<HashSet<string>> ReadAllTupleKeysAsync(
        OpenFgaClient client, CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;
        do
        {
            var options = new ClientReadOptions { ContinuationToken = continuationToken };
            var page = await client.Read(new ClientReadRequest(), options, cancellationToken)
                .ConfigureAwait(false);
            foreach (var tuple in page.Tuples)
            {
                keys.Add(TupleKey(tuple.Key.User, tuple.Key.Relation, tuple.Key.Object));
            }

            continuationToken = string.IsNullOrEmpty(page.ContinuationToken) ? null : page.ContinuationToken;
        }
        while (continuationToken is not null);

        return keys;
    }

    private static string TupleKey(string user, string relation, string @object) =>
        $"{user}|{relation}|{@object}";
}
