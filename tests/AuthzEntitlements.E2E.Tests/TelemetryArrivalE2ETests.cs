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
/// service is absent or unchanged from its pre-traffic baseline.
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

        using var prom = new HttpClient { BaseAddress = promBase };

        // Capture a per-service baseline BEFORE driving traffic. The collector uses a persistent
        // /data volume, so on a dev/self-hosted machine a service may already carry a stale
        // http_server counter series from a previous run. We therefore require each service's series
        // to CHANGE from its pre-traffic baseline during this run. A "change" (not a strict increase)
        // is the correct, reset-aware signal: each run starts fresh service processes whose counters
        // begin at 0, so a new export overwrites a stale-high value with a LOWER one (a counter
        // reset). Requiring change — not `> baseline` — proves this run delivered without failing
        // spuriously on that reset. In fresh CI the baselines are simply absent, so any sample counts.
        var baseline = await QueryJobCountsAsync(
            prom, "http_server_request_duration_seconds_count", cts.Token);

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

        // Poll until EVERY project service's http_server_request_duration_seconds_count `job` series
        // has CHANGED from its pre-traffic baseline — i.e. telemetry was pushed to the collector for
        // EACH service during THIS run (the maintainer's per-service guard), not merely in aggregate
        // and not merely stale series. "Changed" (rather than strictly increased) is reset-aware: a
        // fresh service process resets its counter to 0, so a new export replaces a stale-high value
        // with a lower one — still proof of this run's delivery. A series absent from the baseline but
        // present now is also delivery. A service that never pushes stays exactly at its baseline (or
        // absent) and is flagged. OTLP batch export + Prometheus ingest lag is up to ~60s; poll
        // (bounded) so we don't fail on transient lag, and fail closed if any service is unchanged at
        // the deadline.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        Dictionary<string, double> jobCounts = new(StringComparer.Ordinal);
        string[] missing = ProjectServices;

        while (DateTime.UtcNow < deadline)
        {
            cts.Token.ThrowIfCancellationRequested();

            jobCounts = await QueryJobCountsAsync(
                prom, "http_server_request_duration_seconds_count", cts.Token);
            missing = ProjectServices.Where(s => !ServiceDelivered(s, baseline, jobCounts)).ToArray();

            if (missing.Length == 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }

        _output.WriteLine(
            "per-service http_server_request_duration_seconds_count (baseline → final): " +
            string.Join(", ", ProjectServices.Select(s =>
                $"{s}={baseline.GetValueOrDefault(s, 0)}→{jobCounts.GetValueOrDefault(s, 0)}")));

        // Every instrumented service/app must have pushed NEW server telemetry to the collector.
        Assert.True(
            missing.Length == 0,
            "These services did not deliver server telemetry (their http_server_request_duration_" +
            "seconds_count series was unchanged from its pre-traffic baseline) to the " +
            $"grafana/otel-lgtm collector during this run: [{string.Join(", ", missing)}]. Seen jobs: " +
            $"[{string.Join(", ", jobCounts.Keys)}].");
    }

    /// <summary>
    /// A service "delivered" telemetry this run when its <c>job</c> series is present now AND either
    /// it was absent from the pre-traffic baseline (brand-new series) or its sample count changed
    /// from the baseline. Change — not strict increase — is required because a fresh service process
    /// resets its counter, so a new export can be numerically LOWER than a stale-high baseline while
    /// still proving this run pushed telemetry.
    /// </summary>
    private static bool ServiceDelivered(
        string service,
        IReadOnlyDictionary<string, double> baseline,
        IReadOnlyDictionary<string, double> current)
    {
        if (!current.TryGetValue(service, out var now))
        {
            return false;
        }

        // Absent from baseline → any present sample is this run's delivery.
        // Present in baseline → require the count to have moved by at least one sample.
        return !baseline.TryGetValue(service, out var was) || Math.Abs(now - was) >= 1.0;
    }

    /// <summary>
    /// Runs <c>sum by (job) (&lt;metric&gt;)</c> and returns each <c>job</c> → sample count. Used to
    /// verify per-service delivery: every project service's count must change from its pre-traffic
    /// baseline (see <see cref="ServiceDelivered"/>).
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
