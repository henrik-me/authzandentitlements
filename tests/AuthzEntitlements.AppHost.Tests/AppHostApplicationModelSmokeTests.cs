using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace AuthzEntitlements.AppHost.Tests;

/// <summary>
/// CS50 — CI guard for the AppHost application model. `dotnet build` compiles the
/// AppHost but never evaluates its <c>DistributedApplicationBuilder</c>, so wiring
/// defects (notably the CS48 duplicate-resource-name collision that only threw at
/// <c>aspire run</c>) passed CI undetected. Constructing the model here via
/// Aspire.Hosting.Testing — <c>CreateAsync</c> then <c>BuildAsync</c>, never
/// <c>StartAsync</c>, so the test stays Docker-free — makes those defects fail CI.
/// </summary>
public class AppHostApplicationModelSmokeTests
{
    [Fact]
    public async Task AppHost_application_model_builds_without_throwing()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        // Reaching here without an exception proves the app model + BuildAsync succeed.
        // NEVER call app.StartAsync() — that would require Docker and start containers.
    }

    [Fact]
    public async Task AppHost_resource_names_are_unique_case_insensitively()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        var names = appHost.Resources.Select(r => r.Name).ToList();

        var duplicates = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"Aspire resource names must be unique case-insensitively; duplicates: {string.Join(", ", duplicates)}");
    }

    /// <summary>
    /// CS56 — regression guard for the missing-http-endpoint defect. The .NET 10 GA + Aspire
    /// 13.4.6 bump stopped assigning an endpoint/<c>ASPNETCORE_URLS</c> to endpoint-less
    /// <c>AddProject</c> resources, so the five internal services fell back to Kestrel's default
    /// <c>:5000</c>, collided, and left the existing <c>.GetEndpoint("http")</c> references
    /// (edge-gateway → bank-api, authz-pdp → audit-service) unresolved. This asserts every project
    /// resource declares an <c>http</c>-scheme endpoint named <c>http</c> so the default
    /// <c>aspire run</c> can never silently regress to <c>:5000</c> again. The assertion
    /// intentionally covers <em>every</em> project resource, not only the five regressed
    /// services: bank-web and edge-gateway already expose <c>http</c> via
    /// <c>WithExternalHttpEndpoints()</c>, so all project resources must carry an <c>http</c> endpoint.
    /// </summary>
    [Fact]
    public async Task AppHost_every_project_resource_exposes_an_http_endpoint()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        var projects = appHost.Resources.OfType<ProjectResource>().ToList();

        Assert.NotEmpty(projects);

        var missing = new List<string>();
        foreach (var project in projects)
        {
            var httpEndpoint = project.Annotations
                .OfType<EndpointAnnotation>()
                .FirstOrDefault(e =>
                    string.Equals(e.Name, "http", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.UriScheme, "http", StringComparison.OrdinalIgnoreCase));

            if (httpEndpoint is null)
            {
                missing.Add(project.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "Every AddProject resource must declare an 'http'-scheme endpoint named 'http' " +
            "(CS56: otherwise Aspire falls back to Kestrel :5000 and internal services collide). " +
            $"Missing on: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// CS56 — regression guard for the Keycloak HTTPS-flip defect. Aspire.Hosting.Keycloak 13.4.6
    /// declares the fixed host endpoint as HTTP on 8088 → container 8080, but in run mode it also
    /// subscribes to a BeforeStart HTTPS-endpoint update that rewrites that endpoint to
    /// <c>https</c> / targetPort 8443 whenever a developer certificate is available — which is what
    /// broke <c>http://localhost:8088</c> OIDC discovery. <c>WithoutHttpsCertificate()</c> in
    /// AppHost.cs gates that update off. Two assertions guard the fix:
    /// (a) the declared <c>http</c> endpoint stays HTTP, host 8088 → container 8080; and
    /// (b) the <c>HttpsCertificateAnnotation</c> (UseDeveloperCertificate=false) that suppresses the
    /// run-mode flip is present. The flip itself fires only at StartAsync/BeforeStart (never during
    /// this Docker-free BuildAsync), so asserting (b) — the anti-flip annotation — is what actually
    /// catches removal of the fix; (a) additionally pins the fixed port + container target.
    /// </summary>
    [Fact]
    public async Task AppHost_keycloak_fixed_endpoint_is_http_8088_to_container_8080()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        var keycloak = Assert.Single(
            appHost.Resources,
            r => string.Equals(r.Name, "keycloak", StringComparison.OrdinalIgnoreCase));

        var httpEndpoint = keycloak.Annotations
            .OfType<EndpointAnnotation>()
            .FirstOrDefault(e => string.Equals(e.Name, "http", StringComparison.OrdinalIgnoreCase));

        Assert.True(httpEndpoint is not null, "Keycloak must declare an endpoint named 'http'.");
        Assert.Equal("http", httpEndpoint!.UriScheme);
        Assert.Equal(8088, httpEndpoint.Port);
        Assert.Equal(8080, httpEndpoint.TargetPort);

#pragma warning disable ASPIRECERTIFICATES001 // guard the anti-HTTPS-flip annotation set by WithoutHttpsCertificate()
        var httpsCerts = keycloak.Annotations
            .OfType<HttpsCertificateAnnotation>()
            .ToList();

        Assert.True(
            httpsCerts.Count > 0,
            "Keycloak must carry an HttpsCertificateAnnotation (from WithoutHttpsCertificate()) so the " +
            "run-mode HTTPS-endpoint update never flips the fixed 8088 endpoint to https/8443.");

        // Select by the property under test rather than annotation ordering (annotation order is not a
        // stable contract, so LastOrDefault() would be a brittle false-failure): the anti-flip
        // annotation is the one with UseDeveloperCertificate == false.
        var antiFlip = httpsCerts.FirstOrDefault(c => (c.UseDeveloperCertificate ?? true) == false);
        Assert.True(
            antiFlip is not null,
            "Keycloak's HttpsCertificateAnnotation must set UseDeveloperCertificate=false; otherwise the " +
            "BeforeStart update rewrites the 8088 endpoint to https/8443 and breaks http://localhost:8088 OIDC.");
        Assert.Null(antiFlip!.Certificate);
#pragma warning restore ASPIRECERTIFICATES001
    }

    /// <summary>
    /// CS61 — regression guard for the persistent telemetry disk. CS60 dropped
    /// <c>ContainerLifetime.Persistent</c> on the observability container (to end the collector
    /// split-brain) but kept the named <c>authz-observability-data</c> volume mounted at
    /// <c>/data</c> — which is what actually persists the otel-lgtm Prometheus/Loki/Tempo stores
    /// across container recreation (verified live: telemetry from one run survived into a fresh
    /// container reusing the volume). This asserts that named, writable <c>/data</c> volume mount is
    /// still declared, so a future edit cannot silently drop or swap the persistent telemetry disk.
    /// </summary>
    [Fact]
    public async Task AppHost_observability_declares_persistent_data_volume()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>();

        await using var app = await appHost.BuildAsync();

        var observability = Assert.Single(
            appHost.Resources,
            r => string.Equals(r.Name, "observability", StringComparison.OrdinalIgnoreCase));

        var dataMount = observability.Annotations
            .OfType<ContainerMountAnnotation>()
            .FirstOrDefault(m => m.Target == "/data");

        Assert.True(
            dataMount is not null,
            "The observability container must mount a volume at /data — it is the persistent telemetry " +
            "disk (Prometheus/Loki/Tempo) that survives container recreation now that CS60 dropped " +
            "ContainerLifetime.Persistent.");
        Assert.Equal(ContainerMountType.Volume, dataMount!.Type);
        Assert.Equal("authz-observability-data", dataMount.Source);
        Assert.False(
            dataMount.IsReadOnly,
            "The /data telemetry volume must be writable so the collector can persist telemetry.");
    }
}
