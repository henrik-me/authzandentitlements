using System.Net.Http;
using System.Text.Json;
using Aserto.Authorizer.V2;
using Aserto.Authorizer.V2.Api;
using Aserto.Clients.Authorizer;
using Aserto.Clients.Options;
using AuthzEntitlements.Authz.Pdp.Contracts;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Aserto.Authorizer.V2.Authorizer;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;

// The live Topaz integration: lazy Aserto authorizer client construction + the single forward Query the
// provider bridges to. Registered as a singleton so the gRPC channel is built once per process. Topaz is
// OPA-based — the authorizer evaluates an OPA policy bundle — so this queries data.authz.bank.decision
// with the AccessRequest as `input`, over the SAME Rego the OPA adapter uses (infra/opa/policy), and
// returns the raw Rego decision object for the provider to map. It is the head-to-head "OPA standalone
// vs OPA-inside-Topaz".
//
// Implements ITopazCheckClient (LRN-038): TopazDecisionProvider depends on that narrow forward-decision
// seam, not this concrete type, so the full-decision reason/obligation mapping is unit-testable offline
// via a test double.
//
// Async bridging: the Aserto authorizer client is async-only, but the CS05 IAuthorizationDecisionProvider
// contract is synchronous, so Check bridges with GetAwaiter().GetResult() (as the SpiceDB adapter does).
// The provider wraps the call in a fail-closed catch, so a blocked/faulted call denies, never throws
// through /api/authz/evaluate.
//
// Fails closed on configuration: the client is built only on first actual use, and if Endpoint is blank
// or not a well-formed absolute http(s):// URI the first check throws a clear, actionable error BEFORE
// any network call — DI registration and the default deterministic run never touch a server. The gRPC
// channel is built and OWNED here (not by the Aserto wrapper) so it can be disposed on a failed build
// and at shutdown, mirroring the SpiceDB dispose-on-failure discipline.
public sealed class TopazCheckService : ITopazCheckClient, IDisposable
{
    // The Rego query: bind the shared decision rule to a variable; the authorizer returns the bound
    // value under result[0].bindings.<variable>. This reuses the SAME rule the OPA adapter POSTs to.
    private const string QueryBindingVariable = "x";
    private const string DecisionQuery = "x = data.authz.bank.decision";

    // Web defaults give camelCase serialization matching the wire contract the Rego policy reads
    // (input.subject.*, input.action.name, input.resource.*, input.context.scopes).
    private static readonly JsonSerializerOptions InputSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly TopazOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Volatile so the fast-path (pre-semaphore) read of _client safely publishes the channel: the
    // volatile write of _channel before the volatile write of _client guarantees a thread that observes
    // a non-null _client also sees the fully-constructed channel it owns.
    private volatile GrpcChannel? _channel;
    private volatile IAuthorizerAPIClient? _client;

    public TopazCheckService(IOptions<TopazOptions> options, ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
    }

    public TopazCheckOutcome Check(AccessRequest request)
    {
        var client = GetOrBuildClient();

        var queryRequest = new QueryRequest
        {
            Query = DecisionQuery,
            Input = JsonSerializer.Serialize(request, InputSerializerOptions),

            // Anonymous identity: the fintech decision is computed entirely from the policy `input`, not
            // from a Topaz directory identity (the parity boundary). The authorizer REJECTS an unset /
            // UNKNOWN identity type ("identity type UNKNOWN"), so an explicit IDENTITY_TYPE_NONE is
            // required for the query to run.
            IdentityContext = new IdentityContext { Type = IdentityType.None, Identity = string.Empty },
        };

        // Bound the forward query to the configured fail-closed deadline: a hung or unreachable authorizer
        // must DENY promptly rather than block evaluation indefinitely. On timeout WaitAsync throws
        // TimeoutException, which the provider's catch turns into a fail-closed deny (Task.WaitAsync(TimeSpan)
        // is available on net10). The TimeoutSeconds value is validated (> 0) when the client is built.
        var response = client.QueryAsync(queryRequest)
            .WaitAsync(TimeSpan.FromSeconds(_options.TimeoutSeconds))
            .GetAwaiter().GetResult();
        return ExtractOutcome(response);
    }

    // Navigates the OPA query result the authorizer returns — a Struct shaped
    // { "result": [ { "bindings": { "x": <decision object> }, "expressions": [...] } ] } — down to the
    // decision object and reads its raw fields. Any structural deviation (no result list, an empty
    // result, a missing/wrong-typed binding) yields TopazCheckOutcome.None so the provider fails closed
    // rather than fabricating a decision. Internal (not private) so the obligations-parsing seam can be
    // asserted offline without a live authorizer (InternalsVisibleTo the test assembly).
    internal static TopazCheckOutcome ExtractOutcome(QueryResponse? response)
    {
        var root = response?.Response;
        if (root is null
            || !root.Fields.TryGetValue("result", out var resultValue)
            || resultValue.KindCase != Value.KindOneofCase.ListValue
            || resultValue.ListValue.Values.Count == 0)
        {
            return TopazCheckOutcome.None;
        }

        var firstResult = resultValue.ListValue.Values[0];
        if (firstResult.KindCase != Value.KindOneofCase.StructValue
            || !firstResult.StructValue.Fields.TryGetValue("bindings", out var bindingsValue)
            || bindingsValue.KindCase != Value.KindOneofCase.StructValue
            || !bindingsValue.StructValue.Fields.TryGetValue(QueryBindingVariable, out var decisionValue)
            || decisionValue.KindCase != Value.KindOneofCase.StructValue)
        {
            return TopazCheckOutcome.None;
        }

        var decision = decisionValue.StructValue;
        var (obligations, obligationsMalformed) = GetObligations(decision, "obligations");
        return new TopazCheckOutcome(
            GetString(decision, "decision"),
            GetString(decision, "reason"),
            GetString(decision, "rule"),
            obligations,
            obligationsMalformed);
    }

    private static string? GetString(Struct source, string field) =>
        source.Fields.TryGetValue(field, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;

    // Reads a Rego string array, distinguishing THREE cases so a malformed obligations field FAILS CLOSED
    // instead of being silently read as a legitimate no-obligation permit:
    //   * field ABSENT                 → (Obligations: null,  Malformed: false) — a legitimate
    //                                    no-obligation permit (a read or a below-threshold transaction).
    //   * field PRESENT but not a list → (Obligations: null,  Malformed: true)  — the policy emitted a
    //                                    non-array obligations value (e.g. a bare string); the provider
    //                                    fails closed rather than dropping a maker-checker obligation.
    //   * field IS a list              → (Obligations: items, Malformed: false) — a NON-string element is
    //                                    surfaced as its kind name so the provider's obligation mapping
    //                                    fails closed on it rather than silently dropping it.
    private static (IReadOnlyList<string>? Obligations, bool Malformed) GetObligations(
        Struct source, string field)
    {
        if (!source.Fields.TryGetValue(field, out var value))
        {
            return (null, false);
        }

        if (value.KindCase != Value.KindOneofCase.ListValue)
        {
            return (null, true);
        }

        var values = value.ListValue.Values;
        var items = new List<string>(values.Count);
        foreach (var item in values)
        {
            items.Add(item.KindCase == Value.KindOneofCase.StringValue
                ? item.StringValue
                : item.KindCase.ToString());
        }

        return (items, false);
    }

    private IAuthorizerAPIClient GetOrBuildClient()
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

    private IAuthorizerAPIClient BuildClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new InvalidOperationException(
                "Topaz Endpoint is not configured. Set \"Pdp:Topaz:Endpoint\" (and \"Pdp:Provider\" to " +
                "\"topaz\"); under `aspire run` the AppHost injects it as Pdp__Topaz__Endpoint once the " +
                "'topaz' container is started.");
        }

        // Validate the endpoint is a well-formed absolute http/https URI so a misconfiguration (a missing
        // scheme like "localhost:8282", stray whitespace, or a typo) fails closed with a clear message
        // here rather than as a cryptic Uri/gRPC exception from the channel builder.
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Topaz Endpoint is not a well-formed absolute http(s):// URI (e.g. \"https://localhost:8282\"). " +
                "Set \"Pdp:Topaz:Endpoint\" to the Topaz authorizer's gRPC address.");
        }

        // A non-positive timeout would defeat the bounded fail-closed wait: Task.WaitAsync rejects a
        // zero/negative TimeSpan, and an unbounded query wait lets a hung authorizer block evaluation
        // instead of failing closed. Reject it here with a clear, actionable message BEFORE any channel
        // is built (the provider turns this throw into a fail-closed deny).
        if (_options.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Topaz TimeoutSeconds must be greater than zero. Set \"Pdp:Topaz:TimeoutSeconds\" to a " +
                "positive number of seconds (the default is 5).");
        }

        // Build the channel WE own (rather than letting AuthorizerAPIClient build its own from options)
        // so it can be disposed on a failed build and at shutdown. Passing our own AuthorizerClient makes
        // the wrapper use THIS channel, so there is exactly one channel and we control its lifetime.
        var channel = BuildChannel(uri);
        try
        {
            var invoker = BuildCallInvoker(channel);
            var authorizerClient = new AuthorizerClient(invoker);

            // ServiceUrl/PlainText must stay consistent with the channel: AuthorizerAPIClient calls
            // AsertoAuthorizerOptions.Validate, which throws only when PlainText is set on a non-http
            // scheme. Insecure mirrors the self-signed-cert acceptance the channel already applies.
            var asertoOptions = Options.Create(new AsertoAuthorizerOptions
            {
                ServiceUrl = _options.Endpoint,
                Insecure = true,
                PlainText = uri.Scheme == Uri.UriSchemeHttp,
                AuthorizerApiKey = _options.ApiKey,
                TenantID = _options.TenantId,
            });

            var client = new AuthorizerAPIClient(asertoOptions, _loggerFactory, authorizerClient);
            _channel = channel;
            return client;
        }
        catch
        {
            // A failed construction must not leak the channel: the next check re-enters and builds a
            // fresh one, so dispose this attempt's channel before rethrowing (the provider turns the
            // throw into a fail-closed deny). Without this, repeated fail-closed builds would accumulate
            // abandoned channels/sockets until restart.
            channel.Dispose();
            throw;
        }
    }

    // Attaches the Aserto auth headers (Authorization: basic <key>, Aserto-Tenant-Id) when configured.
    // Both are empty by default — the lab Topaz config uses anonymous auth — so the plain channel invoker
    // is used unchanged and no empty headers are sent.
    private CallInvoker BuildCallInvoker(GrpcChannel channel)
    {
        var apiKey = _options.ApiKey;
        var tenantId = _options.TenantId;
        if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(tenantId))
        {
            return channel.CreateCallInvoker();
        }

        return channel.CreateCallInvoker().Intercept(metadata =>
        {
            if (!string.IsNullOrEmpty(tenantId))
            {
                metadata.Add("Aserto-Tenant-Id", tenantId);
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                metadata.Add("Authorization", $"basic {apiKey}");
            }

            return metadata;
        });
    }

    // Builds the gRPC channel for the authorizer endpoint. Internal (not private) so the lazy h2c opt-in
    // — the switch is set ONLY on the cleartext http:// branch, never at type load or on the TLS path —
    // can be asserted offline without a live server (InternalsVisibleTo the test assembly).
    internal static GrpcChannel BuildChannel(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            // Topaz serves the authorizer over TLS with a self-signed dev certificate. Accept any server
            // certificate ONLY for a LOOPBACK endpoint (localhost / 127.0.0.1 / ::1) — the lab posture,
            // where the container's self-signed cert is expected and there is no MITM surface. For a
            // NON-loopback https host, fall back to NORMAL CA validation so a misconfigured remote
            // Pdp:Topaz:Endpoint can never silently accept a forged/MITM certificate (Copilot review,
            // PR #176). The TLS path never touches the h2c switch.
            if (uri.IsLoopback)
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                };

                return GrpcChannel.ForAddress(uri, new GrpcChannelOptions { HttpHandler = handler });
            }

            return GrpcChannel.ForAddress(uri);
        }

        // http:// → cleartext h2c. Grpc.Net.Client requires the process to opt into unencrypted HTTP/2
        // before any h2c call, so enable the switch LAZILY here — only when a cleartext http:// endpoint
        // is actually used — rather than process-wide at type load. That way a run on the TLS (https)
        // default, or with a different provider active, never flips it. It only ENABLES h2c for callers
        // that opt in (TLS / HTTP-1.1 clients are unaffected).
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        return GrpcChannel.ForAddress(uri);
    }

    // Disposes the cached gRPC channel (and the bootstrap gate) when the DI container disposes this
    // singleton at shutdown. The failure path in BuildClient disposes its own channel, so this only
    // releases the one successfully-built channel.
    public void Dispose()
    {
        _channel?.Dispose();
        _gate.Dispose();
    }
}
