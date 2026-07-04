using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using AuthzEntitlements.Authz.Pdp.Lifecycle.AuthZen;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// AuthZEN Authorization API 1.0 conformance (CS17): the PDP speaks the native AuthZEN wire shape.
// Covers inbound attribute extraction from the property bags (including number/string coercion and
// safe defaults), outbound decision -> boolean + explainability projection, the required boolean
// `decision` field on the response, and a full-catalog round-trip proving the AuthZEN surface
// yields the golden decision for every scenario.
public sealed class AuthZenConformanceTests
{
    // Match the ASP.NET Core minimal-API host serializer (camelCase + case-insensitive) so the
    // tests exercise the exact wire behaviour the endpoint produces.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AuthZenEvaluationRequest Parse(string json) =>
        JsonSerializer.Deserialize<AuthZenEvaluationRequest>(json, Web)!;

    [Fact]
    public void ToAccessRequest_ExtractsSubjectResourceContextAttributes()
    {
        var request = AuthZenMapper.ToAccessRequest(Parse("""
        {
          "subject": { "type": "user", "id": "user-teller1",
            "properties": { "roles": ["Teller"], "tenant": "CONTOSO", "branch": "BR-1" } },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction", "id": "txn-1",
            "properties": { "tenant": "CONTOSO", "amount": 15000, "maker_id": "user-teller1", "status": "Pending" } },
          "context": { "scopes": ["bank.transactions.write"] }
        }
        """));

        Assert.Equal("user", request.Subject.Type);
        Assert.Equal("user-teller1", request.Subject.Id);
        Assert.Equal([RoleNames.Teller], request.Subject.Roles);
        Assert.Equal("CONTOSO", request.Subject.Tenant);
        Assert.Equal("BR-1", request.Subject.Branch);
        Assert.Equal("bank.transaction.create", request.Action.Name);
        Assert.Equal("transaction", request.Resource.Type);
        Assert.Equal("txn-1", request.Resource.Id);
        Assert.Equal(15_000m, request.Resource.Amount);
        Assert.Equal("user-teller1", request.Resource.MakerId);
        Assert.Equal("Pending", request.Resource.Status);
        Assert.Equal([ScopeNames.TransactionsWrite], request.Context.Scopes);
    }

    [Fact]
    public void ToAccessRequest_CoercesAmount_FromStringOrNumber()
    {
        var fromString = AuthZenMapper.ToAccessRequest(Parse("""
        { "subject": { "type": "user", "id": "u", "properties": {} },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction", "properties": { "amount": "12345.67" } },
          "context": {} }
        """));
        var fromNumber = AuthZenMapper.ToAccessRequest(Parse("""
        { "subject": { "type": "user", "id": "u", "properties": {} },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction", "properties": { "amount": 12345.67 } },
          "context": {} }
        """));

        Assert.Equal(12_345.67m, fromString.Resource.Amount);
        Assert.Equal(12_345.67m, fromNumber.Resource.Amount);
    }

    [Fact]
    public void ToAccessRequest_MissingProperties_YieldsSafeDefaults()
    {
        var request = AuthZenMapper.ToAccessRequest(Parse("""
        { "subject": { "type": "user", "id": "u" },
          "action": { "name": "bank.account.read" },
          "resource": { "type": "account" },
          "context": {} }
        """));

        Assert.Empty(request.Subject.Roles);
        Assert.Null(request.Subject.Tenant);
        Assert.Null(request.Resource.Amount);
        Assert.Empty(request.Context.Scopes);
    }

    [Fact]
    public void ToResponse_Permit_MapsToTrueWithObligations()
    {
        var decision = new ReferenceDecisionProvider().Evaluate(LifecycleTestSupport.PermitLargeTxn());

        var response = AuthZenMapper.ToResponse(decision);

        Assert.True(response.Decision);
        Assert.Equal(ReasonCodes.Permit, response.Context!.ReasonCode);
        Assert.Contains(ObligationIds.RequireApproval, response.Context.Obligations);
    }

    [Fact]
    public void ToResponse_Deny_MapsToFalseWithReason()
    {
        var decision = new ReferenceDecisionProvider().Evaluate(LifecycleTestSupport.DenyRequest());

        var response = AuthZenMapper.ToResponse(decision);

        Assert.False(response.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, response.Context!.ReasonCode);
        Assert.Empty(response.Context.Obligations);
    }

    [Fact]
    public void Response_SerializesWithRequiredBooleanDecisionField()
    {
        var response = new AuthZenEvaluationResponse(true, new AuthZenDecisionContext("Permit", ["ok"], []));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, Web));

        Assert.True(document.RootElement.TryGetProperty("decision", out var decision));
        Assert.Equal(JsonValueKind.True, decision.ValueKind);
    }

    [Fact]
    public void AuthZenRoundTrip_YieldsGoldenDecision_ForEveryCatalogScenario()
    {
        var reference = new ReferenceDecisionProvider();
        var golden = GoldenDecisionSnapshot.Golden.ToDictionary(g => g.ScenarioId, StringComparer.Ordinal);

        foreach (var scenario in FintechScenarioCatalog.Scenarios)
        {
            var wire = AuthZenMapper.ToWireRequest(scenario.Request);
            var json = JsonSerializer.Serialize(wire, Web);
            var reconstructed = AuthZenMapper.ToAccessRequest(Parse(json));

            var response = AuthZenMapper.ToResponse(reference.Evaluate(reconstructed));
            var expected = golden[scenario.Id];

            Assert.Equal(expected.Decision == Decision.Permit, response.Decision);
            Assert.Equal(expected.ReasonCode, response.Context!.ReasonCode);
            Assert.Equal(expected.ObligationIds, response.Context.Obligations);
        }
    }

    [Fact]
    public void Validate_EveryCatalogScenario_PassesValidation()
    {
        foreach (var scenario in FintechScenarioCatalog.Scenarios)
        {
            var parsed = Parse(JsonSerializer.Serialize(AuthZenMapper.ToWireRequest(scenario.Request), Web));
            Assert.Null(AuthZenRequestValidation.Validate(parsed));
        }
    }

    [Fact]
    public void Validate_MalformedShape_IsRejected()
    {
        Assert.NotNull(AuthZenRequestValidation.Validate(Parse("{}")));
    }

    [Fact]
    public void Validate_UnparseableAmount_IsRejected_NotSilentlyZero()
    {
        var request = Parse("""
        { "subject": { "type": "user", "id": "user-teller1", "properties": { "roles": ["Teller"], "tenant": "CONTOSO" } },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction",
            "properties": { "tenant": "CONTOSO", "amount": "not-a-number", "maker_id": "user-teller1" } },
          "context": { "scopes": ["bank.transactions.write"] } }
        """);

        var error = AuthZenRequestValidation.Validate(request);

        Assert.NotNull(error);
        Assert.Contains("amount", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TransactionCreate_MissingAmount_IsRejected()
    {
        var request = Parse("""
        { "subject": { "type": "user", "id": "user-teller1", "properties": {} },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction", "properties": { "maker_id": "user-teller1" } },
          "context": {} }
        """);

        Assert.Contains("amount", AuthZenRequestValidation.Validate(request)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TransactionCreate_MissingMaker_IsRejected()
    {
        var request = Parse("""
        { "subject": { "type": "user", "id": "user-teller1", "properties": {} },
          "action": { "name": "bank.transaction.create" },
          "resource": { "type": "transaction", "properties": { "amount": 15000 } },
          "context": {} }
        """);

        Assert.Contains("maker_id", AuthZenRequestValidation.Validate(request)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bank.transaction.approve")]
    [InlineData("bank.transaction.reject")]
    public void Validate_Approval_MissingMaker_IsRejected(string action)
    {
        var request = Parse($$"""
        { "subject": { "type": "user", "id": "user-manager1", "properties": {} },
          "action": { "name": "{{action}}" },
          "resource": { "type": "transaction", "properties": { "status": "Pending" } },
          "context": {} }
        """);

        Assert.Contains("maker_id", AuthZenRequestValidation.Validate(request)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bank.transaction.approve")]
    [InlineData("bank.transaction.reject")]
    public void Validate_Approval_MissingStatus_IsRejected(string action)
    {
        var request = Parse($$"""
        { "subject": { "type": "user", "id": "user-manager1", "properties": {} },
          "action": { "name": "{{action}}" },
          "resource": { "type": "transaction", "properties": { "maker_id": "user-teller1" } },
          "context": {} }
        """);

        Assert.Contains("status", AuthZenRequestValidation.Validate(request)!, StringComparison.Ordinal);
    }
}
