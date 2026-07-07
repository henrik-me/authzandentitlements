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
/// asserts that the <c>grafana/otel-lgtm</c> collector's Prometheus receives a fresh
/// <c>http_server_request_duration_seconds_count</c> <c>job</c> series <em>for each service</em>
/// (CS61 — the maintainer's per-service guard) — the exact metric the CS12 Service Health /
/// Request Rates dashboards query. It reaches Prometheus via the internal <c>prometheus</c>
/// endpoint the AppHost exposes on the observability container (CS60), so the assertion mirrors
/// what a dashboard sees. It polls (bounded) for per-service arrival and fails closed if any
/// service's latest-sample timestamp does not advance past its pre-traffic baseline.
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
        // http_server series from a previous run. We therefore require each service's latest SAMPLE
        // TIMESTAMP to advance past its pre-traffic baseline during this run — i.e. a genuinely new
        // sample was ingested for that job. Timestamps (not counter values) are the robust signal:
        //  * value-change is unsafe — `sum by (job)` can change merely because a stale series ages
        //    out of Prometheus's 5m lookback mid-poll, so a broken service could falsely "deliver";
        //  * a strict value-increase is unsafe — a fresh process resets its counter, so a new export
        //    can be numerically LOWER than a stale-high baseline.
        // `max by (job)(timestamp(metric))` gives each job's newest sample time; it only moves
        // forward when a NEW sample arrives, and aging-out makes it stay flat or DECREASE (handled by
        // the directional `> baseline` compare below). In fresh CI baselines are absent, so any first
        // sample counts.
        var baseline = await QueryJobSampleTimestampsAsync(
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
        // has a NEWER sample than its pre-traffic baseline — i.e. telemetry was pushed to the
        // collector for EACH service during THIS run (the maintainer's per-service guard), not merely
        // in aggregate and not merely stale series. A service that never pushes keeps a flat/aging
        // (older-or-equal) max timestamp and is flagged. OTLP batch export + Prometheus ingest lag is
        // up to ~60s; poll (bounded) so we don't fail on transient lag, and fail closed if any
        // service has not produced a newer sample at the deadline.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        Dictionary<string, double> jobStamps = new(StringComparer.Ordinal);
        string[] missing = ProjectServices;

        while (DateTime.UtcNow < deadline)
        {
            cts.Token.ThrowIfCancellationRequested();

            jobStamps = await QueryJobSampleTimestampsAsync(
                prom, "http_server_request_duration_seconds_count", cts.Token);
            missing = ProjectServices.Where(s => !ServiceDelivered(s, baseline, jobStamps)).ToArray();

            if (missing.Length == 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }

        _output.WriteLine(
            "per-service http_server latest-sample unix ts (baseline → final): " +
            string.Join(", ", ProjectServices.Select(s =>
                $"{s}={baseline.GetValueOrDefault(s, 0):0}→{jobStamps.GetValueOrDefault(s, 0):0}")));

        // Every instrumented service/app must have pushed a NEW server-telemetry sample this run.
        Assert.True(
            missing.Length == 0,
            "These services did not push a newer http_server_request_duration_seconds_count sample " +
            "(their latest-sample timestamp did not advance past its pre-traffic baseline) to the " +
            $"grafana/otel-lgtm collector during this run: [{string.Join(", ", missing)}]. Seen jobs: " +
            $"[{string.Join(", ", jobStamps.Keys)}].");
    }

    /// <summary>
    /// A service "delivered" telemetry this run when its <c>job</c> series is present now AND either
    /// it was absent from the pre-traffic baseline (brand-new series) or its latest-sample timestamp
    /// is strictly NEWER than the baseline. The compare is directional (newer, not merely different):
    /// a fresh export always yields a strictly greater sample timestamp, whereas stale series aging
    /// out of Prometheus's lookback can make the per-job max timestamp stay flat or DECREASE — which
    /// must NOT count as delivery. Comparing timestamps (not counter values) is immune both to
    /// per-process counter resets and to stale series aging out of the aggregate.
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
        // Present in baseline → require a strictly newer sample (>= 1s later; export interval ≫ 1s).
        return !baseline.TryGetValue(service, out var was) || now - was >= 1.0;
    }

    /// <summary>
    /// Runs <c>max by (job) (timestamp(&lt;metric&gt;))</c> and returns each <c>job</c> → its newest
    /// sample's unix-seconds timestamp. Used to verify per-service delivery: every project service's
    /// latest sample must be newer than its pre-traffic baseline (see <see cref="ServiceDelivered"/>).
    /// </summary>
    private static async Task<Dictionary<string, double>> QueryJobSampleTimestampsAsync(
        HttpClient prom, string metric, CancellationToken ct)
    {
        var stamps = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            var query = Uri.EscapeDataString($"max by (job) (timestamp({metric}))");
            using var response = await prom.GetAsync($"/api/v1/query?query={query}", ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return stamps;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var series in json.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
            {
                var job = series.GetProperty("metric").GetProperty("job").GetString();
                var value = series.GetProperty("value")[1].GetString();
                if (job is not null &&
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ts))
                {
                    stamps[job] = ts;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // return whatever was parsed before the failure — the caller polls + fails closed
        }

        return stamps;
    }
}
