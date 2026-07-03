using System.Net;
using System.Net.Http.Json;
using AuthzEntitlements.Bank.Api.Domain;
using AuthzEntitlements.Bank.Api.Entitlements;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Enforcement tests for the commercial-entitlement gates. EntitlementsEnforcer is a pure
// decision service, so these drive it directly with a hand-written fake IEntitlementsClient
// (no web host, no DB). A second set drives the real EntitlementsClient through a stub
// HttpMessageHandler to prove its fail-closed transport behaviour.
public sealed class EntitlementsEnforcementTests
{
    private const string TenantCode = "CONTOSO";

    private static EntitlementsEnforcer Enforcer() => new(NullLogger<EntitlementsEnforcer>.Instance);

    // A configurable fake that records the order of gate calls so tests can assert the
    // module → feature → quota sequencing and short-circuit behaviour.
    private sealed class FakeEntitlementsClient : IEntitlementsClient
    {
        public ModuleCheckResult ModuleResult { get; set; } = new(true, "enterprise", "entitled");
        public FeatureCheckResult FeatureResult { get; set; } = new(true, "enterprise", "enabled");
        public QuotaDecisionResult QuotaResult { get; set; } = new(true, 100, 1, 99, "within quota");
        public SeatUsageResult SeatsResult { get; set; } = new("enterprise", 100, 1, 99);

        public List<string> Calls { get; } = [];

        public Task<ModuleCheckResult> CheckModuleAsync(string tenantCode, string moduleKey, CancellationToken ct)
        {
            Calls.Add("module");
            return Task.FromResult(ModuleResult);
        }

        public Task<FeatureCheckResult> CheckFeatureAsync(string tenantCode, string featureKey, CancellationToken ct)
        {
            Calls.Add("feature");
            return Task.FromResult(FeatureResult);
        }

        public Task<QuotaDecisionResult> ConsumeQuotaAsync(
            string tenantCode, string quotaKey, long amount, CancellationToken ct)
        {
            Calls.Add("quota");
            return Task.FromResult(QuotaResult);
        }

        public Task<SeatUsageResult> GetSeatsAsync(string tenantCode, CancellationToken ct)
        {
            Calls.Add("seats");
            return Task.FromResult(SeatsResult);
        }
    }

    [Fact]
    public async Task Transfer_WhenWireModuleNotEntitled_Denies402_AndDoesNotConsumeQuota()
    {
        var client = new FakeEntitlementsClient
        {
            ModuleResult = new ModuleCheckResult(false, "starter", "not licensed"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer, 1_000m, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(StatusCodes.Status402PaymentRequired, decision.StatusCode);
        Assert.Contains("wire module is not licensed", decision.Reason);
        // Module gate short-circuits before feature/quota are ever consulted.
        Assert.Equal(["module"], client.Calls);
    }

    [Fact]
    public async Task Transfer_WhenWireEntitled_AndUnderThresholds_Allows()
    {
        var client = new FakeEntitlementsClient();

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer, 1_000m, CancellationToken.None);

        Assert.True(decision.Allowed);
        // Under the high-value threshold the feature gate is skipped; quota still consumed.
        Assert.Equal(["module", "quota"], client.Calls);
    }

    [Fact]
    public async Task HighValue_WhenFeatureDisabled_Denies403()
    {
        var client = new FakeEntitlementsClient
        {
            FeatureResult = new FeatureCheckResult(false, "professional", "flag off"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer,
            EntitlementsCatalog.HighValueTransferThreshold, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(StatusCodes.Status403Forbidden, decision.StatusCode);
        Assert.Contains("high-value-transfers feature", decision.Reason);
        // Feature deny happens after the module gate but before quota consumption.
        Assert.Equal(["module", "feature"], client.Calls);
    }

    [Fact]
    public async Task HighValue_WhenFeatureEnabled_AndQuotaOk_Allows()
    {
        var client = new FakeEntitlementsClient();

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer,
            EntitlementsCatalog.HighValueTransferThreshold + 1m, CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(["module", "feature", "quota"], client.Calls);
    }

    [Fact]
    public async Task WhenQuotaExceeded_Denies429()
    {
        var client = new FakeEntitlementsClient
        {
            QuotaResult = new QuotaDecisionResult(false, 50, 50, 0, "exhausted"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Credit, 100m, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(StatusCodes.Status429TooManyRequests, decision.StatusCode);
        Assert.Contains("monthly transaction quota exceeded (50/50)", decision.Reason);
        Assert.Equal(["quota"], client.Calls);
    }

    [Fact]
    public async Task WhenModuleUnavailable_FailsClosed_Denies503()
    {
        var client = new FakeEntitlementsClient
        {
            ModuleResult = ModuleCheckResult.Unavailable("connection refused"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer, 100m, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, decision.StatusCode);
        Assert.Equal("entitlements service unavailable", decision.Reason);
        Assert.Equal(["module"], client.Calls);
    }

    [Fact]
    public async Task WhenQuotaUnavailable_FailsClosed_Denies503()
    {
        var client = new FakeEntitlementsClient
        {
            QuotaResult = QuotaDecisionResult.Unavailable("timeout"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Credit, 100m, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, decision.StatusCode);
        Assert.Equal("entitlements service unavailable", decision.Reason);
    }

    [Fact]
    public async Task NonTransfer_BelowThreshold_OnlyConsumesQuota()
    {
        var client = new FakeEntitlementsClient();

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Debit, 500m, CancellationToken.None);

        Assert.True(decision.Allowed);
        // Non-transfer skips the module gate; below-threshold skips the feature gate.
        Assert.Equal(["quota"], client.Calls);
    }

    [Fact]
    public async Task HighValueTransfer_WhenWireNotEntitled_ModuleGateWinsOverFeature()
    {
        var client = new FakeEntitlementsClient
        {
            ModuleResult = new ModuleCheckResult(false, "starter", "not licensed"),
            FeatureResult = new FeatureCheckResult(false, "starter", "flag off"),
        };

        var decision = await Enforcer().EvaluateCreateAsync(
            client, TenantCode, TransactionType.Transfer,
            EntitlementsCatalog.HighValueTransferThreshold, CancellationToken.None);

        // Module deny (402) short-circuits even though the feature would also deny.
        Assert.Equal(StatusCodes.Status402PaymentRequired, decision.StatusCode);
        Assert.Equal(["module"], client.Calls);
    }

    // ---- EntitlementsClient transport fail-closed behaviour ----

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private static EntitlementsClient ClientWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new StubHandler(responder)) { BaseAddress = new Uri("http://entitlements-service") });

    [Fact]
    public async Task Client_WhenServiceReturns500_ReturnsUnavailable()
    {
        var client = ClientWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await client.CheckModuleAsync(TenantCode, EntitlementsCatalog.WireModuleKey, CancellationToken.None);

        Assert.True(result.IsUnavailable);
        Assert.False(result.Entitled);
    }

    [Fact]
    public async Task Client_WhenTransportThrows_ReturnsUnavailable()
    {
        var client = ClientWith(_ => throw new HttpRequestException("connection refused"));

        var result = await client.CheckModuleAsync(TenantCode, EntitlementsCatalog.WireModuleKey, CancellationToken.None);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Client_WhenServiceReturnsPayload_MapsCamelCaseFields()
    {
        var client = ClientWith(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { entitled = true, planTier = "enterprise", reason = "licensed" }),
        });

        var result = await client.CheckModuleAsync(TenantCode, EntitlementsCatalog.WireModuleKey, CancellationToken.None);

        Assert.False(result.IsUnavailable);
        Assert.True(result.Entitled);
        Assert.Equal("enterprise", result.PlanTier);
        Assert.Equal("licensed", result.Reason);
    }
}
