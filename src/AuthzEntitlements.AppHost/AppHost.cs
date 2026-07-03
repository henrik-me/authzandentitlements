var builder = DistributedApplication.CreateBuilder(args);

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
    .WithEnvironment("Entitlements__Unleash__ApiToken", "*:development.unleash-insecure-client-token");

builder.AddProject<Projects.AuthzEntitlements_Bank_Api>("bank-api")
    .WithReference(bankDb)
    .WaitFor(bankDb)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(entitlementsService)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__Audience", "bank-api");

builder.AddProject<Projects.AuthzEntitlements_Bank_Web>("bank-web")
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Keycloak__Authority", keycloakAuthority)
    .WithEnvironment("Keycloak__ClientSecret", "bank-web-secret")
    .WithExternalHttpEndpoints();

builder.Build().Run();
