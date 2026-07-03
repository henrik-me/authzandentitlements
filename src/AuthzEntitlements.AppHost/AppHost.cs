var builder = DistributedApplication.CreateBuilder(args);

// CS12 — Persistent observability stack. `grafana/otel-lgtm` bundles the OpenTelemetry
// Collector + Prometheus (metrics) + Tempo (traces) + Loki (logs) + Grafana into a single
// container: the standard Aspire persistent-observability backend, going beyond the
// ephemeral dev-time Aspire dashboard. The instrumented services fan their OTLP telemetry
// here (ServiceDefaults already gates its OTLP exporter on OTEL_EXPORTER_OTLP_ENDPOINT), the
// bundled collector routes each signal to its backend, and Grafana visualizes them with the
// baseline dashboards provisioned from infra/observability. A persistent container lifetime +
// a /data volume keep telemetry across `aspire run` restarts. The tag is pinned for
// determinism; this is a dev-loop backend (not a production deployment).
var observability = builder.AddContainer("observability", "grafana/otel-lgtm", "0.28.0")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithVolume("authz-observability-data", "/data")
    .WithBindMount(
        "../../infra/observability/grafana/dashboards",
        "/otel-lgtm/grafana/conf/provisioning/dashboards/custom",
        isReadOnly: true)
    .WithBindMount(
        "../../infra/observability/grafana/dashboards-provisioning.yaml",
        "/otel-lgtm/grafana/conf/provisioning/dashboards/custom.yaml",
        isReadOnly: true)
    // Anonymous Editor kiosk: the lab's Grafana opens with no login and Explore (Loki/Tempo)
    // works, but the image's default admin/admin cannot be used to escalate — BOTH the UI login
    // form and HTTP Basic Auth are disabled, so every visitor is a capped anonymous Editor (no
    // user/datasource/settings admin) with no interactive OR programmatic path to admin.
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Editor")
    .WithEnvironment("GF_AUTH_DISABLE_LOGIN_FORM", "true")
    .WithEnvironment("GF_AUTH_BASIC_ENABLED", "false")
    .WithHttpEndpoint(targetPort: 3000, name: "grafana")
    // OTLP ingest stays internal: model 4317/4318 as tcp (not http) endpoints so
    // WithExternalHttpEndpoints() marks ONLY the Grafana UI external — the OTLP ports are
    // reachable by the host-run services but not exposed off-box (no telemetry-injection surface).
    .WithEndpoint(targetPort: 4317, name: "otlp-grpc")
    .WithEndpoint(targetPort: 4318, name: "otlp-http")
    .WithExternalHttpEndpoints();

// The OTLP/gRPC endpoint every instrumented service exports to. The endpoint is a tcp resource
// (kept internal), so build the http:// exporter URL the .NET OTLP exporter expects explicitly.
var otlpGrpc = observability.GetEndpoint("otlp-grpc");
var otlpEndpoint = ReferenceExpression.Create(
    $"http://{otlpGrpc.Property(EndpointProperty.Host)}:{otlpGrpc.Property(EndpointProperty.Port)}");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var bankDb = postgres.AddDatabase("bank");
postgres.AddDatabase("openfga");
var entitlementsDb = postgres.AddDatabase("entitlements");
postgres.AddDatabase("governance");
postgres.AddDatabase("audit");

// CS10 — Unleash backs the optional config-gated feature-flag provider. It is kept OFF
// the app's critical path: it uses .WithExplicitStart() so `aspire run` never auto-starts
// or blocks on it, and neither bank-api nor entitlements-service WaitFor it (the default
// entitlements provider is the deterministic in-memory catalog). The container talks to
// its own `unleash` database on the shared postgres server.
var unleashDb = postgres.AddDatabase("unleash");

var unleash = builder.AddContainer("unleash", "unleashorg/unleash-server", "6")
    .WithHttpEndpoint(targetPort: 4242, name: "http")
    .WithEnvironment("DATABASE_HOST", $"{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}")
    .WithEnvironment("DATABASE_PORT", $"{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}")
    .WithEnvironment("DATABASE_NAME", "unleash")
    .WithEnvironment("DATABASE_USERNAME", "postgres")
    .WithEnvironment("DATABASE_PASSWORD", $"{postgres.Resource.PasswordParameter}")
    .WithEnvironment("DATABASE_SSL", "false")
    .WithEnvironment("INIT_ADMIN_API_TOKENS", "*:*.unleash-insecure-admin-token")
    .WithEnvironment("INIT_CLIENT_API_TOKENS", "*:development.unleash-insecure-client-token")
    .WaitFor(unleashDb)
    .WithExplicitStart();

// CS03 — AuthN via Keycloak OIDC. Keycloak is pinned to a fixed host port so the
// OIDC issuer is stable and identical across the browser (bank-web login), bank-web,
// and bank-api. A dynamic/proxied endpoint makes Keycloak stamp a different issuer
// per access path, which fails JWT issuer validation. The realm ("authz-bank") is
// imported fresh on every start (no data volume) so the lab is deterministic.
const string realm = "authz-bank";
const int keycloakPort = 8088;
var keycloakAuthority = $"http://localhost:{keycloakPort}/realms/{realm}";

var keycloak = builder.AddKeycloak("keycloak", port: keycloakPort)
    .WithRealmImport("../../infra/keycloak")
    // Keycloak 26's "organization" feature (default-on) throws "Session not bound
    // to a realm" while wiring the bank-workload service account during realm
    // import. The lab does not use Keycloak organizations, so disable the feature.
    .WithEnvironment("KC_FEATURES_DISABLED", "organization");

// CS10 — Commercial entitlements service. Owns the `entitlements` database and is
// service-discovered by bank-api. The in-memory feature provider is the default so the
// service is deterministic and never needs the Unleash container; the Unleash
// coordinates are still injected so switching Entitlements__FeatureProvider=Unleash
// works without further wiring.
var entitlementsService = builder.AddProject<Projects.AuthzEntitlements_Entitlements_Service>("entitlements-service")
    .WithReference(entitlementsDb)
    .WaitFor(entitlementsDb)
    .WithEnvironment("Entitlements__FeatureProvider", "InMemory")
    .WithEnvironment("Entitlements__Unleash__Url", unleash.GetEndpoint("http"))
    .WithEnvironment("Entitlements__Unleash__ApiToken", "*:development.unleash-insecure-client-token")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability);

var bankApi = builder.AddProject<Projects.AuthzEntitlements_Bank_Api>("bank-api")
    .WithReference(bankDb)
    .WaitFor(bankDb)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(entitlementsService)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__Audience", "bank-api")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability);

// CS04 — coarse-grained edge gateway (YARP). Fronts Bank.Api and enforces coarse
// token/audience/scope/tenant checks before routing. Shares the same stable Keycloak
// authority/audience as Bank.Api; the bank-api destination address is injected into the
// YARP cluster config at runtime so the proxy target tracks Aspire's assigned endpoint.
builder.AddProject<Projects.AuthzEntitlements_Edge_Gateway>("edge-gateway")
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(bankApi)
    .WaitFor(bankApi)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__Audience", "bank-api")
    .WithEnvironment(
        "ReverseProxy__Clusters__bank-api__Destinations__bank-api__Address",
        bankApi.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.AuthzEntitlements_Bank_Web>("bank-web")
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__ClientSecret", "bank-web-secret")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability)
    .WithExternalHttpEndpoints();

builder.Build().Run();
