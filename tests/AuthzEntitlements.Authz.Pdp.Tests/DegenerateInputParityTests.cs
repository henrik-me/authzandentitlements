using System.Net;
using System.Text;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// LRN-033: the 22-scenario FintechScenarioCatalog uses only well-formed, non-blank attributes, so the
// fail-closed predicates (tenant, maker, status, scope) are never exercised on null/empty/whitespace
// input — a real fail-OPEN (an engine treating "" == "" as a tenant match) could stay green over the
// catalog. These tests feed degenerate values on EACH predicate and assert equivalence to the
// ReferenceDecisionProvider ORACLE (Decision + primary reason code) — not a hardcoded expectation —
// for every in-process engine (reference/aspnet/casbin/cedar). The out-of-process engines
// (openfga/opa), whose full decision needs a live server, are held to their pure/mapper-level
// fail-closed behaviour here. Kept as SEPARATE tests (not extra catalog rows) so the shared catalog —
// and the golden/shadow/portability suites that snapshot it — stay unchanged.
public sealed class DegenerateInputParityTests
{
    private const string Contoso = PdpRequests.Contoso;
    private const string Teller = "user-teller1";
    private const string Manager = "user-manager1";

    // Each degenerate case isolates ONE fail-closed predicate (every OTHER attribute is valid) so the
    // reference oracle's expected reason names exactly which predicate must fire fail-closed.
    private sealed record DegenerateCase(AccessRequest Request, string ExpectedReasonCode);

    private static readonly IReadOnlyDictionary<string, DegenerateCase> Cases =
        new Dictionary<string, DegenerateCase>(StringComparer.Ordinal)
        {
            // tenant — read: a blank tenant on either/both sides is a mismatch (fail-closed), never "" == "".
            ["read-both-tenants-null"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, null, RoleNames.Teller),
                    ActionNames.AccountRead, new Resource("account", Tenant: null), ScopeNames.Read),
                ReasonCodes.TenantMismatch),
            ["read-subject-tenant-whitespace"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, "   ", RoleNames.Teller),
                    ActionNames.AccountRead, new Resource("account", Tenant: Contoso), ScopeNames.Read),
                ReasonCodes.TenantMismatch),
            ["read-resource-tenant-empty"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, Contoso, RoleNames.Teller),
                    ActionNames.AccountRead, new Resource("account", Tenant: ""), ScopeNames.Read),
                ReasonCodes.TenantMismatch),

            // tenant — across the pipeline (account.create / transaction.create / approval): role, scope,
            // and maker are all satisfied so the tenant forbid is the first (and only) one that fails.
            ["account-create-both-tenants-blank"] = new(
                PdpRequests.For(
                    PdpRequests.User(Manager, null, RoleNames.BranchManager),
                    ActionNames.AccountCreate, new Resource("account", Tenant: "   "), ScopeNames.Read),
                ReasonCodes.TenantMismatch),
            ["txn-create-both-tenants-blank"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, null, RoleNames.Teller),
                    ActionNames.TransactionCreate,
                    new Resource("transaction", Tenant: null, Amount: 250m, MakerId: Teller),
                    ScopeNames.TransactionsWrite),
                ReasonCodes.TenantMismatch),
            ["approve-both-tenants-blank"] = new(
                PdpRequests.For(
                    PdpRequests.User(Manager, null, RoleNames.BranchManager),
                    ActionNames.TransactionApprove,
                    new Resource("transaction", Tenant: null, MakerId: Teller, Status: "Pending"),
                    ScopeNames.ApprovalsWrite),
                ReasonCodes.TenantMismatch),

            // scope — the coarse scope gate denies first on a blank/absent scope set.
            ["read-no-scope"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, Contoso, RoleNames.Teller),
                    ActionNames.AccountRead, new Resource("account", Tenant: Contoso)),
                ReasonCodes.MissingScope),
            ["txn-create-no-scope"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, Contoso, RoleNames.Teller),
                    ActionNames.TransactionCreate,
                    new Resource("transaction", Tenant: Contoso, Amount: 250m, MakerId: Teller)),
                ReasonCodes.MissingScope),
            ["approve-no-scope"] = new(
                PdpRequests.For(
                    PdpRequests.User(Manager, Contoso, RoleNames.BranchManager),
                    ActionNames.TransactionApprove,
                    new Resource("transaction", Tenant: Contoso, MakerId: Teller, Status: "Pending")),
                ReasonCodes.MissingScope),

            // maker — subject-is-maker fails closed when the maker attribute is blank (null/whitespace),
            // never treating a blank maker as "the subject".
            ["txn-create-null-maker"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, Contoso, RoleNames.Teller),
                    ActionNames.TransactionCreate,
                    new Resource("transaction", Tenant: Contoso, Amount: 250m, MakerId: null),
                    ScopeNames.TransactionsWrite),
                ReasonCodes.SubjectNotMaker),
            ["txn-create-whitespace-maker"] = new(
                PdpRequests.For(
                    PdpRequests.User(Teller, Contoso, RoleNames.Teller),
                    ActionNames.TransactionCreate,
                    new Resource("transaction", Tenant: Contoso, Amount: 250m, MakerId: "   "),
                    ScopeNames.TransactionsWrite),
                ReasonCodes.SubjectNotMaker),

            // status — pending fails closed when the status attribute is blank (null/whitespace), never
            // treating a blank status as pending.
            ["approve-null-status"] = new(
                PdpRequests.For(
                    PdpRequests.User(Manager, Contoso, RoleNames.BranchManager),
                    ActionNames.TransactionApprove,
                    new Resource("transaction", Tenant: Contoso, MakerId: Teller, Status: null),
                    ScopeNames.ApprovalsWrite),
                ReasonCodes.NotPending),
            ["approve-whitespace-status"] = new(
                PdpRequests.For(
                    PdpRequests.User(Manager, Contoso, RoleNames.BranchManager),
                    ActionNames.TransactionApprove,
                    new Resource("transaction", Tenant: Contoso, MakerId: Teller, Status: "   "),
                    ScopeNames.ApprovalsWrite),
                ReasonCodes.NotPending),
        };

    public static IEnumerable<object[]> CaseIds() =>
        Cases.Keys.Select(id => new object[] { id });

    public static IEnumerable<object[]> EngineTimesCase() =>
        from engine in LifecycleTestSupport.RbacEngineNames
        from id in Cases.Keys
        select new object[] { engine, id };

    // The oracle guard (LRN-033): each degenerate case must be a fail-closed DENY with a SPECIFIC
    // reason. If the reference ever fell OPEN on a blank attribute this fails at the source — before
    // the parity theory below could mask it by every engine agreeing on a wrong permit.
    [Theory]
    [MemberData(nameof(CaseIds))]
    public void ReferenceOracle_DeniesDegenerateInput_FailClosed(string caseId)
    {
        var @case = Cases[caseId];

        var decision = new ReferenceDecisionProvider().Evaluate(@case.Request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(@case.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    // Every in-process engine (reference/aspnet/casbin/cedar) must match the reference oracle exactly
    // (Decision + primary reason code) on the degenerate input — the parity the realistic catalog can
    // never assert. Compared against the LIVE oracle, not a hardcoded value, so any divergence surfaces
    // as a real fail-open/fail-closed discrepancy rather than a stale expectation.
    [Theory]
    [MemberData(nameof(EngineTimesCase))]
    public void InProcessEngine_MatchesReferenceOracle_OnDegenerateInput(string engine, string caseId)
    {
        var request = Cases[caseId].Request;

        var oracle = new ReferenceDecisionProvider().Evaluate(request);
        var actual = LifecycleTestSupport.ProviderByName(engine).Evaluate(request);

        Assert.Equal(oracle.Decision, actual.Decision);
        Assert.Equal(oracle.Reasons[0].Code, actual.Reasons[0].Code);
    }

    // --- Out-of-process engines: pure/mapper-level fail-closed (full decision needs a live server) ---

    // openfga — the pure OpenFgaRequestMapper fails closed on a degenerate (null/empty/whitespace)
    // resource id: a blank object never yields a ReBAC Check, so it can never become an accidental
    // permit. Proven end-to-end through the provider with an always-ALLOW seam double — the deny is the
    // mapper's, and the engine is never consulted (zero calls).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OpenFga_DegenerateResourceId_FailsClosedAtMapper_NeverReachesEngine(string? resourceId)
    {
        var alwaysAllow = FakeOpenFgaCheckClient.Allowing();
        var provider = new OpenFgaProvider(alwaysAllow, NullLogger<OpenFgaProvider>.Instance);
        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: resourceId),
            new EvaluationContext([]));

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.MissingResourceId, decision.Reasons[0].Code);
        Assert.Equal(0, alwaysAllow.Calls);
    }

    // opa — OPA owns the ABAC decision out-of-process (Rego), so its degenerate-input parity is covered
    // by infra/opa/policy/authz_test.rego. The in-process contract THIS suite can assert is fail-closed:
    // when OPA returns no usable decision (an undefined "{}" result — the shape a policy gives for input
    // it does not resolve) the adapter DENIES (ProviderUnavailable) and never falls through to a permit.
    [Fact]
    public void Opa_UndefinedResult_FailsClosed_NeverPermits()
    {
        var request = PdpRequests.For(
            PdpRequests.User(Teller, null, RoleNames.Teller),
            ActionNames.AccountRead, new Resource("account", Tenant: null), ScopeNames.Read);

        var provider = new OpaDecisionProvider(
            new UndefinedResultHttpClientFactory(),
            Options.Create(new OpaOptions()),
            NullLogger<OpaDecisionProvider>.Instance);

        var decision = provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
    }

    // Returns OPA's "policy undefined for the input" body ("{}") so the adapter fails closed, with no
    // live OPA. (The full StubHandler lives in OpaDecisionProviderTests; this is the minimal shape the
    // single degenerate fail-closed assertion needs.)
    private sealed class UndefinedResultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new UndefinedResultHandler(), disposeHandler: false)
            {
                BaseAddress = new Uri("http://localhost:8181"),
            };
    }

    private sealed class UndefinedResultHandler : HttpMessageHandler
    {
        // The provider calls the synchronous HttpClient.Send, so override Send; SendAsync delegates
        // to it for completeness.
        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
