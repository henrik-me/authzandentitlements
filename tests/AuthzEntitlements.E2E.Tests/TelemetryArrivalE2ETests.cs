using System.Globalization;
using System.Net;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;
using Xunit.Abstractions;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// CS60 — the telemetry-arrival regression guard. The CS57/CS58 e2e assert that services
/// return 200/health and that auth flows work, but never that <em>telemetry actually lands</em>
/// in the observability backend — which is exactly why the empty-Grafana regression stayed
/// invisible (see LRN-092 and the CS32/LRN-014 "full-run confirmation" follow-up).
///
/// This test boots the real <c>aspire run</c> stack, drives inbound HTTP traffic to <em>every</em>
/// instrumented project service (so each emits <c>http.server.request.duration</c>), and then
/// asserts that the <c>grafana/otel-lgtm</c> collector's Prometheus holds a non-zero
/// <c>http_server_request_duration_seconds_count</c> <c>job</c> series <em>for each service</em>
/// (CS61 — the maintainer's per-service guard) — the exact metric the CS12 Service Health /
/// Request Rates dashboards query. It reaches Prometheus via the internal <c>prometheus</c>
/// endpoint the AppHost exposes on the observability container (CS60), so the assertion mirrors
/// what a dashboard sees. It polls (bounded) for per-service arrival and fails closed if any
/// service is absent or zero.
///
/// The Aspire-dashboard leg of the CS60 dual-export has no guaranteed programmatic read path, so
/// it stays a documented manual check (see docs/observability/observability-stack.md); this
/// automated guard covers the collector/Grafana leg. Opt-in via <see cref="AspireStackE2EFactAttribute"/>
/// (env <c>RUN_ASPIRE_E2E=1</c>); the assembly disables test parallelization so it never boots
/// concurrently with the other e2e facts.
/// </summary>
public sealed class TelemetryArrivalE2ETests
{
    private readonly ITestOutputHelper _output;

    public TelemetryArrivalE2ETests(ITestOutputHelper output) => _output = output;

    /// <summary>Every instrumented project service/app — each MUST push server telemetry to the collector.</summary>
    private static readonly string[] ProjectServices =
    [
        "bank-api",
        "edge-gateway",
        "entitlements-service",
        "governance-service",
        "audit-service",
        "authz-pdp",
        "bank-web",
    ];

    [AspireStackE2EFact]
    [Trait("Category", "e2e")]
    public async Task Telemetry_reaches_the_lgtm_collector()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>(cts.Token);

        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        // The observability collector must be Running before we can query its Prometheus.
        await app.ResourceNotifications.WaitForResourceHealthyAsync(
            "observability",
            WaitBehavior.StopOnResourceUnavailable,
            cts.Token);

        // Resolve the collector's internal Prometheus read API (a tcp endpoint → build the http URL).
        var promRef = app.GetEndpoint("observability", "prometheus");
        var promBase = new UriBuilder("http", promRef.Host, promRef.Port).Uri;
        _output.WriteLine($"prometheus endpoint: {promBase}");

        // Drive inbound HTTP to EVERY instrumented service so ASP.NET Core server instrumentation
        // records http.server.request.duration for each. /alive is unauthenticated, 200, and
        // instrumented (the ServiceDefaults trace filter excludes it from tracing only, not metrics).
        // We assert each /alive actually succeeds so a broken target fails here rather than silently
        // producing only error telemetry.
        foreach (var service in ProjectServices)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                service,
                WaitBehavior.StopOnResourceUnavailable,
                cts.Token);

            using var client = app.CreateHttpClient(service, "http");
            var anySuccess = false;
            for (var i = 0; i < 15; i++)
            {
                try
                {
                    using var response = await client.GetAsync("/alive", cts.Token);
                    anySuccess |= response.IsSuccessStatusCode;
                }
                catch (HttpRequestException)
                {
                    // transient readiness blip — keep driving traffic
                }
            }

            Assert.True(
                anySuccess,
                $"Service '{service}' did not serve a successful GET /alive — cannot verify its telemetry " +
                "when its own endpoint is broken.");
        }

        using var prom = new HttpClient { BaseAddress = promBase };

        // Poll until EVERY project service has a non-zero http_server_request_duration_seconds_count
        // `job` series in the collector — i.e. telemetry is pushed to the collector for EACH service
        // (the maintainer's per-service guard), not merely in aggregate. OTLP batch export + Prometheus
        // ingest lag is a few seconds; poll (bounded) so we don't fail on transient lag, and fail closed
        // if any service is still absent/zero at the deadline.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        Dictionary<string, double> jobCounts = new(StringComparer.Ordinal);
        string[] missing = ProjectServices;

        while (DateTime.UtcNow < deadline)
        {
            cts.Token.ThrowIfCancellationRequested();

            jobCounts = await QueryJobCountsAsync(
                prom, "http_server_request_duration_seconds_count", cts.Token);
            missing = ProjectServices
                .Where(s => !(jobCounts.TryGetValue(s, out var count) && count > 0))
                .ToArray();

            if (missing.Length == 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }

        _output.WriteLine(
            "per-service http_server_request_duration_seconds_count: " +
            string.Join(", ", jobCounts.Select(kv => $"{kv.Key}={kv.Value}")));

        // Every instrumented service/app must have pushed server telemetry to the collector.
        Assert.True(
            missing.Length == 0,
            "These services did not deliver a non-zero http_server_request_duration_seconds_count to the " +
            $"grafana/otel-lgtm collector: [{string.Join(", ", missing)}]. Seen jobs: " +
            $"[{string.Join(", ", jobCounts.Keys)}].");
    }

    /// <summary>
    /// Runs <c>sum by (job) (&lt;metric&gt;)</c> and returns each <c>job</c> → sample count. Used to
    /// verify per-service delivery: every project service must appear with a positive count.
    /// </summary>
    private static async Task<Dictionary<string, double>> QueryJobCountsAsync(
        HttpClient prom, string metric, CancellationToken ct)
    {
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            var query = Uri.EscapeDataString($"sum by (job) ({metric})");
            using var response = await prom.GetAsync($"/api/v1/query?query={query}", ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return counts;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var series in json.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
            {
                var job = series.GetProperty("metric").GetProperty("job").GetString();
                var value = series.GetProperty("value")[1].GetString();
                if (job is not null &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var count))
                {
                    counts[job] = count;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // return whatever was parsed before the failure — the caller polls + fails closed
        }

        return counts;
    }
}
