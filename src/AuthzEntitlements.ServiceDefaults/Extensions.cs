using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Primary OTLP target: the Aspire dashboard. Aspire auto-injects OTEL_EXPORTER_OTLP_ENDPOINT
        // (+ OTEL_EXPORTER_OTLP_HEADERS) into every project, so a config-free AddOtlpExporter() reads
        // them and telemetry shows in the dashboard's Structured logs / Traces / Metrics tabs.
        var hasDashboard = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        // CS60 dual-export: also export to the persistent grafana/otel-lgtm collector, injected by the
        // AppHost as LGTM_OTLP_ENDPOINT. The .NET OTLP exporter targets a single endpoint per exporter,
        // so the second collector is a second, explicitly-configured OTLP exporter per signal. Note:
        // UseOtlpExporter() is NOT used — it cannot be combined with signal-specific AddOtlpExporter().
        var lgtmEndpoint = builder.Configuration["LGTM_OTLP_ENDPOINT"];
        // Parse defensively: a malformed LGTM_OTLP_ENDPOINT (e.g. missing scheme) simply disables
        // the lgtm export leg rather than throwing UriFormatException and crashing service startup.
        var lgtmUri = Uri.TryCreate(lgtmEndpoint, UriKind.Absolute, out var parsedLgtmUri) ? parsedLgtmUri : null;

        if (hasDashboard || lgtmUri is not null)
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    if (hasDashboard)
                    {
                        metrics.AddOtlpExporter();
                    }

                    if (lgtmUri is not null)
                    {
                        metrics.AddOtlpExporter((exporter, _) => exporter.Endpoint = lgtmUri);
                    }
                })
                .WithTracing(tracing =>
                {
                    if (hasDashboard)
                    {
                        tracing.AddOtlpExporter();
                    }

                    if (lgtmUri is not null)
                    {
                        tracing.AddOtlpExporter(exporter => exporter.Endpoint = lgtmUri);
                    }
                });

            builder.Logging.AddOpenTelemetry(logging =>
            {
                if (hasDashboard)
                {
                    logging.AddOtlpExporter();
                }

                if (lgtmUri is not null)
                {
                    logging.AddOtlpExporter(exporter => exporter.Endpoint = lgtmUri);
                }
            });
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}
