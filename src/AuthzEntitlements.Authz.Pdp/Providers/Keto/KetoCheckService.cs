using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Options;
using Ory.Keto.Client.Api;
using Ory.Keto.Client.Client;

namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// The live Ory Keto integration: lazy REST client construction, once-per-process bootstrap (seed
// relationships), and the forward permission check the provider bridges to. Registered as a
// singleton so the clients are cached and bootstrap runs once per process. Everything here is async;
// KetoProvider.Evaluate bridges with GetAwaiter().GetResult() because the CS05
// IAuthorizationDecisionProvider contract is sync.
//
// Implements IKetoCheckClient (LRN-038): KetoProvider depends on that narrow forward-check seam, not
// this concrete type, so the ReBAC permit/deny explanation is unit-testable offline via a test double.
//
// Keto splits its API across TWO ports: checks go to the READ endpoint (PermissionApi, default 4466)
// and relationship writes to the WRITE endpoint (a raw HttpClient PUT to /admin/relation-tuples,
// default 4467). Unlike SpiceDB, Keto is plain HTTP REST — there is NO h2c/HTTP2-unencrypted switch
// to set (that is a gRPC-only concern).
// And unlike SpiceDB, the namespace/permission SCHEMA is defined in the container's OPL config
// (infra/keto/namespaces.keto.ts), NOT pushed via the API — so bootstrap only seeds the shared
// relationship tuples.
//
// Fails closed on configuration: the clients are built only on first actual use, and if either
// endpoint is blank the first call throws a clear "start the keto container / set Pdp:Provider=keto"
// error — DI registration and the default deterministic run never touch a server.
//
// Head-to-head faithfulness: the seed relationships are the SHARED RebacSeedTuples that the SpiceDB
// and OpenFGA adapters use, mapped onto Keto relationships (namespace = object's type, object =
// object's id, relation) with the subject expressed as a bare subject_id for a user or a whole-object
// subject_set for another object — exactly how Zanzibar userset rewrites resolve. All three engines
// are seeded from ONE relationship graph and must answer every account question identically.
public sealed class KetoCheckService : IKetoCheckClient, IDisposable
{
    private readonly KetoOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // All volatile so the fast-path (pre-semaphore) read of _bootstrapped safely publishes the
    // clients: the volatile writes of the clients before the volatile write of _bootstrapped
    // guarantee a thread that observes _bootstrapped==true also sees fully-constructed clients.
    private volatile PermissionApi? _read;
    private volatile HttpClient? _write;
    private volatile bool _bootstrapped;

    public KetoCheckService(IOptions<KetoOptions> options)
    {
        _options = options.Value;
    }

    // Build the clients and create every seed relationship, exactly once per process. Keto's
    // `PUT /admin/relation-tuples` APPENDS a row (it is NOT an idempotent upsert), so re-seeding a
    // PERSISTENT store would duplicate tuples. That is harmless here: the dev container's DSN is
    // in-memory and this service is a DI singleton whose double-checked `_bootstrapped` guard runs the
    // seed write exactly once per process, so no duplication accumulates. Safe to call repeatedly and
    // concurrently — callers after the first observe `_bootstrapped == true` and return without writing.
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

            var (read, write) = BuildClients();
            try
            {
                await WriteSeedRelationshipsAsync(write, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A failed bootstrap (Keto unreachable, or a non-2xx seed write) must not leak the write
                // HttpClient: the next check re-enters and builds a fresh one, so dispose this attempt's
                // client before rethrowing (the provider turns the throw into a fail-closed deny).
                // Without this, repeated fail-closed checks accumulate abandoned HttpClients/handlers.
                write.Dispose();
                throw;
            }

            _read = read;
            _write = write;
            _bootstrapped = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Forward check: does user:subjectId have <permission> on account:accountId? Ids are bare; the
    // fixed "account" object namespace comes from the shared RebacTypes and the subject is a bare
    // subject_id (the "user" convention shared with the SpiceDB / OpenFGA adapters).
    public async Task<bool> CheckAsync(
        string subjectId, string permission, string accountId, CancellationToken cancellationToken = default)
    {
        await EnsureBootstrappedAsync(cancellationToken).ConfigureAwait(false);

        var result = await _read!.CheckPermissionAsync(
            _namespace: RebacTypes.Account,
            _object: accountId,
            relation: permission,
            subjectId: subjectId,
            subjectSetNamespace: null,
            subjectSetObject: null,
            subjectSetRelation: null,
            maxDepth: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Allowed;
    }

    private (PermissionApi Read, HttpClient Write) BuildClients()
    {
        // Validate BOTH endpoints BEFORE constructing EITHER client (Copilot review, PR #172): a
        // blank/malformed WriteEndpoint must never leave a half-built read client allocated on a
        // fail-closed path. ReadEndpoint is validated first so a blank read endpoint still fails with
        // the "ReadEndpoint" message. Keto is HTTP REST, so each endpoint is just a base address — no
        // gRPC channel, no h2c switch; https:// is accepted equally with http://.
        ValidateEndpoint(_options.ReadEndpoint, propertyName: "ReadEndpoint", example: "http://localhost:4466");
        var writeEndpoint = ValidateEndpoint(
            _options.WriteEndpoint, propertyName: "WriteEndpoint", example: "http://localhost:4467");

        // The read (check) client on a plain Configuration.BasePath (the raw endpoint string, unchanged
        // read wire behaviour). The write (relationship) client is a plain HttpClient — NOT the generated
        // RelationshipApi — so the seed WRITE path controls the exact JSON; see KetoSeedTupleMapper for
        // WHY subject_id must be OMITTED (not sent empty) for subject-set tuples.
        var read = new PermissionApi(new Configuration { BasePath = _options.ReadEndpoint });
        var write = new HttpClient { BaseAddress = writeEndpoint };

        return (read, write);
    }

    // Validates one endpoint and returns it as an absolute Uri. A blank or malformed endpoint fails
    // closed with a clear, actionable message here rather than as a cryptic Uri/HTTP exception from the
    // first call — the provider turns the throw into a fail-closed deny. Shared by BOTH the read
    // (PermissionApi) and write (HttpClient) paths so they reject a blank/non-http(s) endpoint
    // identically. Keto is HTTP REST, so https:// is accepted equally with http:// (no h2c-only
    // restriction like the SpiceDB adapter).
    private static Uri ValidateEndpoint(string endpoint, string propertyName, string example)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                $"Keto {propertyName} is not configured. Set \"Pdp:Keto:{propertyName}\" (and " +
                "\"Pdp:Provider\" to \"keto\"); under `aspire run` the AppHost injects it as " +
                $"Pdp__Keto__{propertyName} once the 'keto' container is started.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Keto {propertyName} is not a well-formed absolute http:// URI (e.g. \"{example}\"). " +
                $"Set \"Pdp:Keto:{propertyName}\" to the Keto container's REST address.");
        }

        return uri;
    }

    private static async Task WriteSeedRelationshipsAsync(HttpClient write, CancellationToken cancellationToken)
    {
        // Reuse the SHARED seed graph so all three ReBAC engines answer from ONE source of truth. Each
        // RebacTuple is a ("type:id" subject, relation, "type:id" object) triple; KetoSeedTupleMapper
        // renders each as the exact PUT body — crucially, subject_id is OMITTED (not sent empty) for
        // subject-set tuples so Keto stores the subject_set instead of silently dropping it. Any non-2xx
        // response fails closed with a clear, non-sensitive message so a partial/failed seed can never
        // masquerade as a healthy engine.
        foreach (var tuple in RebacSeedTuples.Tuples)
        {
            var json = KetoSeedTupleMapper.Serialize(KetoSeedTupleMapper.Map(tuple));
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await write.PutAsync("/admin/relation-tuples", content, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Keto relationship write to the WRITE endpoint failed with HTTP {(int)response.StatusCode}.");
            }
        }
    }

    // Disposes the bootstrap gate and the write HttpClient when the DI container disposes this singleton
    // at shutdown. A FAILED bootstrap already disposes its own write HttpClient in
    // EnsureBootstrappedAsync (mirroring the SpiceDB channel-dispose-on-failure), so this shutdown
    // Dispose releases only the one successfully-bootstrapped write client; the read PermissionApi holds
    // no gRPC channel or other long-lived unmanaged connection to release.
    public void Dispose()
    {
        _write?.Dispose();
        _gate.Dispose();
    }
}
