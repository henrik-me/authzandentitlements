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
public sealed class SpiceDbCheckService : ISpiceDbCheckClient, IDisposable
{
    private readonly SpiceDbOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // All volatile so the fast-path (pre-semaphore) read of _bootstrapped safely publishes the
    // clients: the volatile writes of the clients before the volatile write of _bootstrapped
    // guarantee a thread that observes _bootstrapped==true also sees fully-constructed clients.
    private volatile GrpcChannel? _channel;
    private volatile SchemaService.SchemaServiceClient? _schema;
    private volatile PermissionsService.PermissionsServiceClient? _permissions;
    private volatile bool _bootstrapped;

    static SpiceDbCheckService()
    {
        // SpiceDB's dev container serves gRPC over cleartext HTTP/2 (h2c). Grpc.Net.Client requires the
        // process to opt into unencrypted HTTP/2 before any h2c call — without it every call to the
        // http:// endpoint throws and the provider fail-closes even when the container is running
        // (grpc-dotnet: "SocketsHttpHandler.Http2UnencryptedSupport"). Set once, process-wide; it only
        // ENABLES h2c for callers that request it (TLS / HTTP-1.1 clients are unaffected). This adapter
        // is the only h2c consumer, and the switch is set before its lazily-built channel is ever used.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

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

            var (channel, schema, permissions) = BuildClients();
            try
            {
                await WriteSchemaAsync(schema, cancellationToken).ConfigureAwait(false);
                await WriteSeedRelationshipsAsync(permissions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A failed bootstrap (e.g. SpiceDB unreachable) must not leak the channel: the next
                // check re-enters and builds a fresh one, so dispose this attempt's channel before
                // rethrowing (the provider turns the throw into a fail-closed deny). Without this,
                // repeated fail-closed checks accumulate abandoned channels/sockets until restart.
                channel.Dispose();
                throw;
            }

            _channel = channel;
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
                // first check (no stale-cache false negative). Only SpiceDB pins consistency here; the
                // OpenFGA adapter uses the SDK/server default.
                Consistency = new Consistency { FullyConsistent = true },
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Permissionship == CheckPermissionResponse.Types.Permissionship.HasPermission;
    }

    private (GrpcChannel Channel, SchemaService.SchemaServiceClient Schema, PermissionsService.PermissionsServiceClient Permissions) BuildClients()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "SpiceDB Endpoint is not configured. Set \"Pdp:SpiceDb:Endpoint\" (and \"Pdp:Provider\" " +
                "to \"spicedb\"); under `aspire run` the AppHost injects it as Pdp__SpiceDb__Endpoint " +
                "once the 'spicedb' container is started.");
        }

        // Validate the endpoint is a well-formed absolute http/https URI so a misconfiguration
        // (a missing scheme like "localhost:50051", stray whitespace, or a typo) fails closed with a
        // clear message here rather than as a cryptic Uri/gRPC exception from GrpcChannel.ForAddress.
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "SpiceDB Endpoint is not a well-formed absolute http:// URI (e.g. \"http://localhost:50051\"). " +
                "Set \"Pdp:SpiceDb:Endpoint\" to the SpiceDB container's cleartext gRPC address.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "SpiceDB Endpoint uses https:// but this adapter only speaks cleartext h2c (http://). " +
                "TLS (SslCredentials) is a documented follow-on; configure an http:// endpoint.");
        }

        // The preshared key travels as an "authorization: Bearer <key>" gRPC metadata header on every
        // call (SpiceDB's `serve --grpc-preshared-key` auth). The interceptor lambda runs per request.
        var callCredentials = CallCredentials.FromInterceptor((_, metadata) =>
        {
            if (!string.IsNullOrEmpty(_options.PresharedKey))
            {
                metadata.Add("authorization", $"Bearer {_options.PresharedKey}");
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
        return (channel, new SchemaService.SchemaServiceClient(channel), new PermissionsService.PermissionsServiceClient(channel));
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

    // Disposes the cached gRPC channel (and the bootstrap gate) when the DI container disposes this
    // singleton at shutdown. The failure path in EnsureBootstrappedAsync disposes its own channel, so
    // this only releases the one successfully-bootstrapped channel.
    public void Dispose()
    {
        _channel?.Dispose();
        _gate.Dispose();
    }
}
