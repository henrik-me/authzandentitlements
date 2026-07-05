using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// SpiceDbProvider's permit/deny/fail-closed behaviour through the ISpiceDbCheckClient seam (LRN-038),
// asserted fully OFFLINE via a test double — the SpiceDB counterpart to OpenFgaProviderSeamTests.
// Also covers registration + selection through the CS05 seam (AddPdp), so the "spicedb" provider is
// factory-selectable by name, coexists with the reference engine, and never touches a live server.
public sealed class SpiceDbProviderTests
{
    private static SpiceDbProvider Provider(ISpiceDbCheckClient client) =>
        new(client, NullLogger<SpiceDbProvider>.Instance);

    private static AccessRequest AccountRead(string subjectId, string? resourceId) =>
        new(
            new Subject("user", subjectId, []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: resourceId),
            new EvaluationContext([]));

    [Fact]
    public void Evaluate_WhenCheckAllowed_Permits_WithRelationshipExplanation()
    {
        var decision = Provider(FakeSpiceDbCheckClient.Allowing())
            .Evaluate(AccountRead("teller1", "acme-checking"));

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("spicedb", explanation!.Engine);
        Assert.Equal(DeterminingRules.Relationship, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.RelationshipTuple, reference.Kind);
        Assert.Equal("user:teller1#can_view@account:acme-checking", reference.Reference);
    }

    [Fact]
    public void Evaluate_WhenCheckDenied_Denies_WithNoRelationshipReason_AndCheckedTupleReference()
    {
        var decision = Provider(FakeSpiceDbCheckClient.Denying())
            .Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.NoRelationship, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("spicedb", explanation!.Engine);
        Assert.Equal(DeterminingRules.Relationship, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.RelationshipTuple, reference.Kind);
        Assert.Equal("user:carol#can_view@account:personal-carol", reference.Reference);
    }

    [Fact]
    public void Evaluate_ForwardsTheMappedCheck_ToTheSeam()
    {
        // The provider must forward exactly the mapped (subjectId, permission, accountId) — the bare
        // ids and the action-derived permission — to the seam.
        var fake = FakeSpiceDbCheckClient.Allowing();

        Provider(fake).Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(1, fake.Calls);
        Assert.Equal("carol", fake.LastSubjectId);
        Assert.Equal(RebacRelations.CanView, fake.LastPermission);
        Assert.Equal("personal-carol", fake.LastAccountId);
    }

    [Fact]
    public void Evaluate_TransactionCreateOnAccount_ChecksCanTransact_AndPermits()
    {
        // transaction.create on an account-shaped resource maps to the can_transact permission (not
        // can_view); on an allow the surfaced tuple names that permission.
        var fake = FakeSpiceDbCheckClient.Allowing();
        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(RebacRelations.CanTransact, fake.LastPermission);
        var reference = Assert.Single(decision.Explanation!.PolicyReferences);
        Assert.Equal("user:carol#can_transact@account:personal-carol", reference.Reference);
    }

    [Fact]
    public void Evaluate_WhenSeamThrows_FailsClosed_Deny_EngineUnavailable()
    {
        // An unreachable/misbehaving engine surfaces as a thrown check; Evaluate must DENY
        // (EngineUnavailable) rather than throw a raw 500 through /api/authz/evaluate.
        var decision = Provider(FakeSpiceDbCheckClient.Throwing(new InvalidOperationException("engine down")))
            .Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("spicedb", explanation!.Engine);
        Assert.Equal(DeterminingRules.EngineUnavailable, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, reference.Reference);
    }

    [Fact]
    public void Evaluate_UnmappedAction_DeniesAtMapper_WithoutConsultingTheSeam()
    {
        // A fail-closed boundary deny (unknown action) must short-circuit BEFORE any check: the seam
        // is never consulted, so a mapper-level deny can never be turned into an accidental engine
        // permit — proven by an always-ALLOW double that records zero calls.
        var fake = FakeSpiceDbCheckClient.Allowing();
        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest("bank.account.delete"),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
        Assert.Equal(0, fake.Calls);
    }

    // --- Registration + selection through the CS05 seam ---------------------------------------------

    private static ServiceProvider Build(string provider, string endpoint = "", string presharedKey = "")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pdp:Provider"] = provider,
                ["Pdp:SpiceDb:Endpoint"] = endpoint,
                ["Pdp:SpiceDb:PresharedKey"] = presharedKey,
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddPdp(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void AddPdp_RegistersSpiceDbProvider_AmongProviders()
    {
        using var provider = Build("reference");

        var names = provider.GetServices<IAuthorizationDecisionProvider>().Select(p => p.Name).ToList();

        Assert.Contains("spicedb", names);
        Assert.Contains("reference", names);
    }

    [Fact]
    public void AddPdp_SelectsSpiceDb_WhenConfigured_CaseInsensitively()
    {
        using var provider = Build("SpiceDB");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        // CS45: spicedb does not declare ISupportsExtendedAuthorizationContext, so the factory wraps it
        // in the fail-closed ExtendedContextGuardProvider. Selection is unchanged (Name still "spicedb");
        // the resolved instance is the guard whose Inner is the concrete SpiceDB adapter.
        var active = factory.GetActiveProvider();
        Assert.Equal("spicedb", active.Name);
        var guard = Assert.IsType<ExtendedContextGuardProvider>(active);
        Assert.IsType<SpiceDbProvider>(guard.Inner);
    }

    [Fact]
    public void SpiceDbOptions_BindFromPdpSpiceDbSection()
    {
        using var provider = Build("spicedb", endpoint: "http://localhost:50051", presharedKey: "k");

        var options = provider.GetRequiredService<IOptions<SpiceDbOptions>>().Value;

        Assert.Equal("http://localhost:50051", options.Endpoint);
        Assert.Equal("k", options.PresharedKey);
        Assert.Equal("Pdp:SpiceDb", SpiceDbOptions.SectionName);
    }

    [Fact]
    public void Registration_DoesNotRequireServer_BlankEndpointIsFine()
    {
        // Building the container and resolving the provider must succeed with no SpiceDB server and an
        // empty endpoint — the live gRPC client is built lazily only on first actual check.
        using var provider = Build("spicedb", endpoint: "");

        var spicedb = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "spicedb");

        Assert.NotNull(spicedb);
        Assert.NotNull(provider.GetRequiredService<SpiceDbCheckService>());
    }

    [Fact]
    public void Evaluate_FailsClosed_WhenEngineUnavailable()
    {
        // With a blank endpoint the service throws when the provider issues its check; Evaluate must
        // DENY (fail closed) with a stable reason, never throw a 500 through /api/authz/evaluate.
        using var provider = Build("spicedb", endpoint: "");
        var spicedb = provider.GetServices<IAuthorizationDecisionProvider>().Single(p => p.Name == "spicedb");

        var decision = spicedb.Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, decision.Reasons[0].Code);
    }

    [Fact]
    public void UnknownProvider_StillFailsClosed_ListingSpiceDb()
    {
        using var provider = Build("does-not-exist");

        var factory = provider.GetRequiredService<AuthorizationDecisionProviderFactory>();

        var ex = Assert.Throws<InvalidOperationException>(() => factory.GetActiveProvider());
        Assert.Contains("does-not-exist", ex.Message);
        Assert.Contains("spicedb", ex.Message);
    }
}
