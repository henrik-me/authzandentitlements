using Authzed.Api.V1;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

// The live SpiceDB integration: lazy gRPC client construction, idempotent bootstrap (schema +
// seed relationships), and the forward permission check the provider bridges to. Registered as a
// singleton so the channel is cached and bootstrap runs once per process. Everything here is async;
// SpiceDbProvider.Evaluate bridges with GetAwaiter().GetResult() because the CS05
// IAuthorizationDecisionProvider contract is sync.
//
// Implements ISpiceDbCheckClient (LRN-038): SpiceDbProvider depends on that narrow forward-check
// seam, not this concrete type, so the ReBAC permit/deny explanation is unit-testable offline via a
// test double.
//
// Fails closed on configuration: the client is built only on first actual use, and if Endpoint is
// blank the first call throws a clear "start the spicedb container / set Pdp:Provider=spicedb"
// error — DI registration and the default deterministic run never touch a server.
//
// Head-to-head faithfulness: the seed relationships are the SHARED RebacSeedTuples the OpenFGA
// adapter uses, written into SpiceDB's schema (SpiceDbSchema) with the SpiceDB relation names of the
// same spelling, so SpiceDB and OpenFGA are seeded from ONE relationship graph and must answer every
// account question identically.
public sealed class SpiceDbCheckService : ISpiceDbCheckClient
{
    private readonly SpiceDbOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // All volatile so the fast-path (pre-semaphore) read of _bootstrapped safely publishes the
    // clients: the volatile writes of the clients before the volatile write of _bootstrapped
    // guarantee a thread that observes _bootstrapped==true also sees fully-constructed clients.
    private volatile SchemaService.SchemaServiceClient? _schema;
    private volatile PermissionsService.PermissionsServiceClient? _permissions;
    private volatile bool _bootstrapped;

    public SpiceDbCheckService(IOptions<SpiceDbOptions> options)
    {
        _options = options.Value;
    }

    // Idempotent: build the clients, push the schema, and TOUCH every seed relationship. SpiceDB's
    // TOUCH operation is a create-or-update, so re-running bootstrap (or seeding a store that already
    // has the data) never errors or duplicates. Safe to call repeatedly and from concurrent requests.
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

            var (schema, permissions) = BuildClients();
            await WriteSchemaAsync(schema, cancellationToken).ConfigureAwait(false);
            await WriteSeedRelationshipsAsync(permissions, cancellationToken).ConfigureAwait(false);

            _schema = schema;
            _permissions = permissions;
            _bootstrapped = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Forward check: does user:subjectId have <permission> on account:accountId? Ids are bare; the
    // fixed "user"/"account" object types come from the shared RebacTypes.
    public async Task<bool> CheckAsync(
        string subjectId, string permission, string accountId, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrappedAsync(cancellationToken).ConfigureAwait(false);

        var response = await _permissions!.CheckPermissionAsync(
            new CheckPermissionRequest
            {
                Resource = new ObjectReference { ObjectType = RebacTypes.Account, ObjectId = accountId },
                Permission = permission,
                Subject = new SubjectReference
                {
                    Object = new ObjectReference { ObjectType = RebacTypes.User, ObjectId = subjectId },
                },
                // Fully-consistent reads: the freshly-seeded relationships must be visible to the very
                // first check (no stale-cache false negative), matching the deterministic OpenFGA path.
                Consistency = new Consistency { FullyConsistent = true },
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Permissionship == CheckPermissionResponse.Types.Permissionship.HasPermission;
    }

    private (SchemaService.SchemaServiceClient, PermissionsService.PermissionsServiceClient) BuildClients()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "SpiceDB Endpoint is not configured. Set \"Pdp:SpiceDb:Endpoint\" (and \"Pdp:Provider\" " +
                "to \"spicedb\"); under `aspire run` the AppHost injects it as Pdp__SpiceDb__Endpoint " +
                "once the 'spicedb' container is started.");
        }

        // The preshared key travels as an "Authorization: Bearer <key>" gRPC metadata header on every
        // call (SpiceDB's `serve --grpc-preshared-key` auth). The interceptor lambda runs per request.
        var callCredentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            if (!string.IsNullOrEmpty(_options.PresharedKey))
            {
                metadata.Add("Authorization", $"Bearer {_options.PresharedKey}");
            }

            return Task.CompletedTask;
        });

        // The dev container serves gRPC over h2c (cleartext HTTP/2) at an http:// address, so the
        // channel uses insecure transport credentials while still attaching the bearer call
        // credentials (UnsafeUseInsecureChannelCallCredentials permits per-call creds without TLS). A
        // TLS SpiceDB would swap in SslCredentials — a documented follow-on, out of scope for the lab.
        var channelOptions = new GrpcChannelOptions
        {
            UnsafeUseInsecureChannelCallCredentials = true,
            Credentials = ChannelCredentials.Create(ChannelCredentials.Insecure, callCredentials),
        };

        var channel = GrpcChannel.ForAddress(_options.Endpoint, channelOptions);
        return (new SchemaService.SchemaServiceClient(channel), new PermissionsService.PermissionsServiceClient(channel));
    }

    private static async Task WriteSchemaAsync(
        SchemaService.SchemaServiceClient schema, CancellationToken cancellationToken)
    {
        await schema.WriteSchemaAsync(
            new WriteSchemaRequest { Schema = SpiceDbSchema.Schema },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSeedRelationshipsAsync(
        PermissionsService.PermissionsServiceClient permissions, CancellationToken cancellationToken)
    {
        // Reuse the SHARED OpenFGA seed graph so both engines answer from ONE source of truth. Each
        // RebacTuple is a ("type:id" subject, relation, "type:id" object) triple; TOUCH makes the
        // whole write idempotent.
        var request = new WriteRelationshipsRequest();
        foreach (var tuple in RebacSeedTuples.Tuples)
        {
            request.Updates.Add(new RelationshipUpdate
            {
                Operation = RelationshipUpdate.Types.Operation.Touch,
                Relationship = new Relationship
                {
                    Resource = ParseObject(tuple.Object),
                    Relation = tuple.Relation,
                    Subject = new SubjectReference { Object = ParseObject(tuple.User) },
                },
            });
        }

        await permissions.WriteRelationshipsAsync(request, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    // Splits a "type:id" ReBAC object string into a SpiceDB ObjectReference. The seed strings always
    // carry a single ':' separator (e.g. "user:carol", "account:acme-checking").
    private static ObjectReference ParseObject(string typeAndId)
    {
        var separator = typeAndId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == typeAndId.Length - 1)
        {
            throw new InvalidOperationException(
                $"Seed relationship object '{typeAndId}' is not a well-formed \"type:id\" string.");
        }

        return new ObjectReference
        {
            ObjectType = typeAndId[..separator],
            ObjectId = typeAndId[(separator + 1)..],
        };
    }
}
