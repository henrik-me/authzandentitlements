using AuthzEntitlements.Authz.Pdp.Contracts;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// The live Cerbos integration: lazy gRPC client construction + the single forward CheckResources the
// provider bridges to. Registered as a singleton so the client (and its underlying channel) is built
// once per process. The CS05 IAuthorizationDecisionProvider contract is synchronous; the Cerbos SDK
// exposes a synchronous CheckResources, so — like the OPA adapter's synchronous HttpClient.Send —
// this stays synchronous with no async-over-sync bridging.
//
// Implements ICerbosCheckClient (LRN-038): CerbosDecisionProvider depends on that narrow
// forward-decision seam, not this concrete type, so the full-decision reason/obligation mapping is
// unit-testable offline via a test double.
//
// Fails closed on configuration: the client is built only on first actual use, and if Endpoint is
// blank or not a well-formed absolute http:// URI the first check throws a clear, actionable error
// BEFORE any network call — DI registration and the default deterministic run never touch a server.
public sealed class CerbosCheckService : ICerbosCheckClient, IDisposable
{
    private readonly CerbosOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile ICerbosClient? _client;

    static CerbosCheckService()
    {
        // Cerbos' dev container serves gRPC over cleartext HTTP/2 (h2c). Grpc.Net.Client requires the
        // process to opt into unencrypted HTTP/2 before any h2c call — the switch is cached at first
        // SocketsHttpHandler use, so it must be set before the lazily-built channel is ever used.
        // The Cerbos SDK's .WithPlaintext() configures a cleartext channel, but this switch is the
        // process-wide prerequisite grpc-dotnet keys on; setting it here (idempotent, and only
        // ENABLES h2c for callers that request it — TLS/HTTP-1.1 clients are unaffected) guarantees a
        // live cleartext check succeeds regardless of whether the SDK sets it internally. Without it a
        // running Cerbos would still fail every check closed.
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public CerbosCheckService(IOptions<CerbosOptions> options)
    {
        _options = options.Value;
    }

    public CerbosCheckOutcome Check(AccessRequest request)
    {
        var client = GetOrBuildClient();

        var response = client.CheckResources(CerbosRequestMapper.Map(request), new Metadata());
        var entry = response.Find(CerbosRequestMapper.ResourceIdFor(request));
        if (entry is null)
        {
            // A well-formed CheckResources always echoes the requested resource entry; an absent
            // entry means a malformed/partial response — surface it so the provider fails closed.
            throw new InvalidOperationException(
                "Cerbos returned no result entry for the requested resource.");
        }

        var allowed = entry.IsAllowed(request.Action.Name);
        return new CerbosCheckOutcome(allowed, ExtractOutputToken(entry));
    }

    // The matching rule's output token (Cerbos `output.when.ruleActivated`), or null when the policy
    // emitted no usable output (Cerbos' default deny for an unmatched/unknown action, or an ambiguous
    // multi-rule activation). The "bank" policy's allow/deny rules are mutually exclusive, so exactly
    // one rule fires and emits exactly one string output; MORE than one output means multiple/ambiguous
    // rules activated — a policy bug — so return null and let the provider fail closed rather than
    // arbitrarily picking one.
    private static string? ExtractOutputToken(CheckResourcesResponse.Types.ResultEntry entry)
    {
        var outputs = entry.Outputs?.ToDictionary();
        if (outputs is null || outputs.Count != 1)
        {
            return null;
        }

        foreach (var value in outputs.Values)
        {
            return value is { KindCase: Value.KindOneofCase.StringValue } ? value.StringValue : null;
        }

        return null;
    }

    private ICerbosClient GetOrBuildClient()
    {
        var existing = _client;
        if (existing is not null)
        {
            return existing;
        }

        _gate.Wait();
        try
        {
            return _client ??= BuildClient();
        }
        finally
        {
            _gate.Release();
        }
    }

    private ICerbosClient BuildClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "Cerbos Endpoint is not configured. Set \"Pdp:Cerbos:Endpoint\" (and \"Pdp:Provider\" " +
                "to \"cerbos\"); under `aspire run` the AppHost injects it as Pdp__Cerbos__Endpoint once " +
                "the 'cerbos' container is started.");
        }

        // Validate the endpoint is a well-formed absolute http/https URI so a misconfiguration (a
        // missing scheme like "localhost:3593", stray whitespace, or a typo) fails closed with a clear
        // message here rather than as a cryptic Uri/gRPC exception from the SDK's channel builder.
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Cerbos Endpoint is not a well-formed absolute http:// URI (e.g. \"http://localhost:3593\"). " +
                "Set \"Pdp:Cerbos:Endpoint\" to the Cerbos container's cleartext gRPC address.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Cerbos Endpoint uses https:// but this adapter only speaks cleartext h2c (http://). " +
                "TLS is a documented follow-on; configure an http:// endpoint.");
        }

        return CerbosClientBuilder.ForTarget(_options.Endpoint).WithPlaintext().Build();
    }

    // Disposes the bootstrap gate (and the client if it happens to own disposable resources) when the
    // DI container disposes this singleton at shutdown. The SDK owns the gRPC channel internally and
    // the client is not IDisposable on the current SDK, so the runtime cast is a defensive no-op today
    // that stays correct if a future SDK makes the client disposable.
    public void Dispose()
    {
        if (_client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _gate.Dispose();
    }
}
