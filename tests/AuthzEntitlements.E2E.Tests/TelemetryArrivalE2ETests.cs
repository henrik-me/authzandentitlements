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
/// This test boots the real <c>aspire run</c> stack, drives a burst of inbound HTTP traffic
/// (so the ASP.NET Core server instrumentation emits <c>http.server.request.duration</c>), and
/// then asserts that the <c>grafana/otel-lgtm</c> collector's Prometheus holds
/// <c>http_server_request_duration_seconds_count &gt; 0</c> — the exact metric the CS12 Service
/// Health / Request Rates dashboards query. It reaches Prometheus via the internal
/// <c>prometheus</c> endpoint the AppHost exposes on the observability container (CS60), so the
/// assertion mirrors what a dashboard sees.
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

    /// <summary>Services whose HTTP endpoints we drive to generate http.server metrics.</summary>
    private static readonly string[] TrafficTargets = ["bank-web", "edge-gateway"];

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

        // Drive inbound HTTP so ASP.NET Core server instrumentation records http.server.request.duration.
        // /alive is unauthenticated, always 200, and instrumented (the ServiceDefaults trace filter
        // excludes it from tracing only, not metrics).
        foreach (var target in TrafficTargets)
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(
                target,
                WaitBehavior.StopOnResourceUnavailable,
                cts.Token);

            using var client = app.CreateHttpClient(target, "http");
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    using var response = await client.GetAsync("/alive", cts.Token);
                    _ = response.StatusCode; // any served status counts as an inbound request
                }
                catch (HttpRequestException)
                {
                    // transient readiness blip — keep driving traffic
                }
            }
        }

        using var prom = new HttpClient { BaseAddress = promBase };

        // Poll for the metric to arrive (OTLP batch export + Prometheus scrape/ingest lag is a few
        // seconds; allow a generous bound). A *skipped* export or a broken pipeline leaves this 0.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
        double serverRequestCount = 0;
        string[] jobs = [];

        while (DateTime.UtcNow < deadline)
        {
            cts.Token.ThrowIfCancellationRequested();

            serverRequestCount = await QueryScalarAsync(
                prom, "sum(http_server_request_duration_seconds_count)", cts.Token);
            jobs = await QueryLabelValuesAsync(prom, "job", cts.Token);

            if (serverRequestCount > 0)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
        }

        _output.WriteLine($"http_server_request_duration_seconds_count = {serverRequestCount}");
        _output.WriteLine($"collector jobs = [{string.Join(", ", jobs)}]");

        // (1) The exact metric the CS12 dashboards query must be present and non-zero — proving
        //     server-side telemetry travelled service → OTLP → collector → Prometheus.
        Assert.True(
            serverRequestCount > 0,
            "http_server_request_duration_seconds_count must be > 0 after driving traffic — telemetry " +
            "did not reach the grafana/otel-lgtm collector, so the CS12 dashboards would be empty.");

        // (2) Multiple services must be delivering (not just the one we hammered), confirming the
        //     shared ServiceDefaults OTLP wiring works across the stack.
        var serviceJobs = jobs.Where(j =>
            j.Contains("bank", StringComparison.Ordinal) ||
            j.Contains("gateway", StringComparison.Ordinal) ||
            j.Contains("service", StringComparison.Ordinal) ||
            j.Contains("pdp", StringComparison.Ordinal)).ToArray();

        Assert.True(
            serviceJobs.Length >= 2,
            $"Expected telemetry from at least 2 project services in the collector, saw jobs: [{string.Join(", ", jobs)}].");
    }

    /// <summary>Runs an instant PromQL query and returns the first scalar/vector sample value, or 0.</summary>
    private static async Task<double> QueryScalarAsync(HttpClient prom, string query, CancellationToken ct)
    {
        try
        {
            using var response = await prom.GetAsync(
                $"/api/v1/query?query={Uri.EscapeDataString(query)}", ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return 0;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var result = json.RootElement.GetProperty("data").GetProperty("result");
            if (result.GetArrayLength() == 0)
            {
                return 0;
            }

            var value = result[0].GetProperty("value")[1].GetString();
            return double.TryParse(value, out var parsed) ? parsed : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return 0;
        }
    }

    /// <summary>Returns the distinct values of a Prometheus label (e.g. <c>job</c>), or empty.</summary>
    private static async Task<string[]> QueryLabelValuesAsync(HttpClient prom, string label, CancellationToken ct)
    {
        try
        {
            using var response = await prom.GetAsync($"/api/v1/label/{label}/values", ct);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return [];
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("data")
                .EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return [];
        }
    }
}
