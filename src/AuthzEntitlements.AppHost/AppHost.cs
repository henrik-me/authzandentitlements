var builder = DistributedApplication.CreateBuilder(args);

// CS12 — Persistent observability stack. `grafana/otel-lgtm` bundles the OpenTelemetry
// Collector + Prometheus (metrics) + Tempo (traces) + Loki (logs) + Grafana into a single
// container: the standard Aspire persistent-observability backend, going beyond the
// ephemeral dev-time Aspire dashboard. The instrumented services fan their OTLP telemetry
// here (ServiceDefaults already gates its OTLP exporter on OTEL_EXPORTER_OTLP_ENDPOINT), the
// bundled collector routes each signal to its backend, and Grafana visualizes them with the
// baseline dashboards provisioned from infra/observability. A persistent container lifetime and
// a /data volume let collected telemetry survive `aspire run` restarts. The tag is pinned for
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
var openfgaDb = postgres.AddDatabase("openfga");
var entitlementsDb = postgres.AddDatabase("entitlements");
var governanceDb = postgres.AddDatabase("governance");
var auditDb = postgres.AddDatabase("audit");

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

// CS08 — OPA/Rego adapter engine. OPA runs as an out-of-process REST decision server,
// bind-mounting the Rego bundle from infra/opa/policy. Kept OFF the default `aspire run`
// critical path with .WithExplicitStart() and NO WaitFor, tag pinned for determinism —
// the in-process reference provider stays the default so build/test/aspire-run never need
// Docker or OPA. To exercise OPA: start this container and set Pdp__Provider=opa on authz-pdp.
var opa = builder.AddContainer("opa", "openpolicyagent/opa", "1.18.2-static")
    .WithHttpEndpoint(targetPort: 8181, name: "http")
    .WithBindMount("../../infra/opa/policy", "/policy", isReadOnly: true)
    .WithArgs("run", "--server", "--addr=0.0.0.0:8181", "--log-level=error", "/policy")
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
var edgeGateway = builder.AddProject<Projects.AuthzEntitlements_Edge_Gateway>("edge-gateway")
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

// CS07 — OpenFGA (ReBAC / Zanzibar) engine backing the config-gated "openfga" PDP provider.
// Kept OFF the default critical path exactly like Unleash: both containers use
// .WithExplicitStart() so `aspire run` and the deterministic reference PDP never start or block
// on OpenFGA, and authz-pdp is given the endpoint coordinates WITHOUT a hard WaitFor. OpenFGA
// stores its ReBAC tuples in its own `openfga` database on the shared postgres server; the
// datastore URI is assembled from the postgres endpoint host/port and password parameter (the
// same pattern the Unleash wiring uses). The image tag is pinned for determinism.
const string openfgaImage = "openfga/openfga";
const string openfgaImageTag = "v1.18.1";

// postgres://postgres:{password}@{host}:{port}/openfga?sslmode=disable — the DSN OpenFGA's
// postgres datastore expects, built as a ReferenceExpression so the runtime host/port/password
// are resolved by Aspire (not captured at build time).
var openfgaDatastoreUri = ReferenceExpression.Create(
    $"postgres://postgres:{postgres.Resource.PasswordParameter}@{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host)}:{postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Port)}/openfga?sslmode=disable");

// One-shot schema migration for OpenFGA's postgres datastore. Explicit-start so it never runs on
// a plain `aspire run`; the server below waits for it to complete before serving.
var openfgaMigrate = builder.AddContainer("openfga-migrate", openfgaImage, openfgaImageTag)
    .WithArgs("migrate")
    .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "postgres")
    .WithEnvironment("OPENFGA_DATASTORE_URI", openfgaDatastoreUri)
    .WaitFor(openfgaDb)
    .WithExplicitStart();

// The OpenFGA server. HTTP API on 8080 (gRPC 8081, playground 3000 are not exposed). Runs the
// migration first, then serves; explicit-start keeps it off the default path.
var openfga = builder.AddContainer("openfga", openfgaImage, openfgaImageTag)
    .WithArgs("run")
    .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "postgres")
    .WithEnvironment("OPENFGA_DATASTORE_URI", openfgaDatastoreUri)
    .WithHttpEndpoint(targetPort: 8080, name: "http")
    .WaitForCompletion(openfgaMigrate)
    .WithExplicitStart();

// CS13 — Tamper-evident audit log pipeline. The Audit.Service owns the `audit` database and
// appends every authz/entitlement decision as a hash-chained, append-only row (prev-hash +
// payload -> row-hash), exposing a chain-verification endpoint + a query API. Postgres already
// runs for entitlements, so — unlike the opt-in engines — the audit service is a first-class part
// of the default `aspire run` stack: it starts by default and the PDP forwards decisions to it
// (below). CS12 fans its OTLP telemetry to the persistent observability collector.
var auditService = builder.AddProject<Projects.AuthzEntitlements_Audit_Service>("audit-service")
    .WithReference(auditDb)
    .WaitFor(auditDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability);

// CS05 — unified AuthZEN-aligned fine-grained PDP. A standalone in-process reference host
// (no database, no WithReference): it answers the shared decision contract with the
// deterministic reference engine. Wiring Bank.Api to call it is deliberately out of CS05
// scope; the adapter engines (CS06-CS09) register behind its config-driven provider seam.
// CS07 injects OpenFGA's endpoint (no hard WaitFor — matching Unleash) so switching
// Pdp__Provider=openfga works once the explicit-start container is running. CS12 fans the PDP's
// own PDP-decision OTLP telemetry out to the persistent observability collector like the other services.
var authzPdp = builder.AddProject<Projects.AuthzEntitlements_Authz_Pdp>("authz-pdp")
    .WithEnvironment("Pdp__OpenFga__ApiUrl", openfga.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    // CS08 — OPA coordinates for the config-gated `opa` provider. Injected unconditionally (like the
    // Unleash coordinates) so Pdp__Provider=opa works without further wiring; no WaitFor(opa) keeps
    // the deterministic reference provider off OPA's critical path.
    .WithEnvironment("Opa__BaseUrl", opa.GetEndpoint("http"))
    // CS13 — forward every PDP decision to the Audit.Service hash-chained store. The sink is
    // config-gated (default "logging"); selecting "http" + injecting the service URL turns on the
    // non-blocking background forwarder. WithReference wires service discovery, but there is NO hard
    // WaitFor(auditService): the forwarder buffers and is resilient, so the decision path never
    // blocks on the audit store being up.
    .WithReference(auditService)
    .WithEnvironment("Audit__Sink", "http")
    .WithEnvironment("Audit__ServiceUrl", auditService.GetEndpoint("http"))
    .WaitFor(observability);

// CS11 — Access-governance service. Owns the `governance` database (access packages, JIT grant
// requests, time-bound grants, access-review campaigns). Its JIT approvals run a segregation-of-
// duties check through the PDP (service-discovered `authz-pdp`, POST /api/authz/evaluate, action
// governance.access.request) — the SoD verdict is identical whether the PDP runs the deterministic
// reference engine (default) or the opt-in OPA container (Pdp__Provider=opa). The SoD call is
// fail-closed, so no hard WaitFor(authz-pdp) is needed to keep the default `aspire run` deterministic.
var governanceService = builder.AddProject<Projects.AuthzEntitlements_Governance_Service>("governance-service")
    .WithReference(governanceDb)
    .WaitFor(governanceDb)
    .WithReference(authzPdp)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability);

// CS14 — Bank.Web calls edge-gateway (coarse), entitlements, governance, and authz-pdp via service discovery.
builder.AddProject<Projects.AuthzEntitlements_Bank_Web>("bank-web")
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(edgeGateway)
    .WithReference(entitlementsService)
    .WithReference(governanceService)
    .WithReference(authzPdp)
    .WithReference(auditService)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__ClientSecret", "bank-web-secret")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observability)
    .WithExternalHttpEndpoints();

builder.Build().Run();
