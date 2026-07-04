using System.Net.Sockets;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.AspNetCore;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Casbin;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cedar;

namespace AuthzEntitlements.Benchmarks;

// The set of benchmarkable authorization engines and how to obtain each one.
//
// In-process engines (reference/aspnet/casbin/cedar) are deterministic and self-contained: they
// are constructed via parameterless constructors exactly like the Pdp test suite's
// LifecycleTestSupport.ProviderByName, so the benchmark exercises the same engines the parity
// tests do — no Docker, no live servers.
//
// Live engines (opa/openfga) are out-of-process and OPTIONAL. Benchmarking them requires a running
// server, so they are only ever attempted when explicitly requested (e.g. --engines all) and, even
// then, self-skip when the endpoint is unreachable — mirroring the repo's "live-engine tests
// self-skip offline" convention. The default run never touches them.
public static class EngineCatalog
{
    // The deterministic in-process RBAC/ABAC engine family — the default benchmark set.
    public static readonly string[] InProcessEngineNames = ["reference", "aspnet", "casbin", "cedar"];

    // Out-of-process engines that require a live server; benchmarked only on request and skipped
    // when offline.
    public static readonly string[] LiveEngineNames = ["opa", "openfga"];

    // Every known engine name, in-process first then live.
    public static IReadOnlyList<string> AllEngineNames { get; } =
        [.. InProcessEngineNames, .. LiveEngineNames];

    // Default TCP endpoints probed to decide whether a live engine is reachable. These mirror the
    // engines' conventional local ports; a probe only tests reachability, it does not authenticate.
    private static readonly IReadOnlyDictionary<string, (string Host, int Port)> LiveEndpoints =
        new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["opa"] = ("127.0.0.1", 8181),
            ["openfga"] = ("127.0.0.1", 8080),
        };

    public static bool IsInProcess(string name) =>
        InProcessEngineNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsLive(string name) =>
        LiveEngineNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string name) =>
        IsInProcess(name) || IsLive(name);

    // Constructs a deterministic in-process provider by name. Same construction as
    // LifecycleTestSupport.ProviderByName so the benchmark and the parity tests share one engine
    // family. Throws for a non-in-process name (callers guard with IsInProcess first).
    public static IAuthorizationDecisionProvider CreateInProcessProvider(string name) => name switch
    {
        "reference" => new ReferenceDecisionProvider(),
        "aspnet" => new AspNetCorePolicyProvider(),
        "casbin" => new CasbinDecisionProvider(),
        "cedar" => new CedarDecisionProvider(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(name), name, "Not a deterministic in-process benchmark engine."),
    };

    // Best-effort reachability probe for a live engine: attempts a short TCP connect to the engine's
    // conventional local endpoint. Returns false on any failure (connection refused, timeout,
    // unknown engine) so an offline engine self-skips rather than failing the run. Never throws.
    public static bool ProbeLiveReachable(string name, int timeoutMs = 250)
    {
        if (!LiveEndpoints.TryGetValue(name, out var endpoint))
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(endpoint.Host, endpoint.Port);
            return connect.Wait(timeoutMs) && client.Connected;
        }
        catch
        {
            // Any probe failure means "not reachable" — the engine self-skips, offline.
            return false;
        }
    }

    // A human-readable description of the endpoint probed for a live engine, for skip reasons.
    public static string LiveEndpointDescription(string name) =>
        LiveEndpoints.TryGetValue(name, out var endpoint)
            ? $"{endpoint.Host}:{endpoint.Port}"
            : "(no known endpoint)";
}
