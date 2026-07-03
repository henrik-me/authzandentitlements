using AuthzEntitlements.Authz.Pdp.Contracts;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Fail-closed coverage for the evaluate boundary: a structurally-incomplete AccessRequest
// (the canonical "{}" body deserializes to null nested records) must be rejected with a
// clear error BEFORE evaluation, so the endpoint returns 400 rather than dereferencing null
// and surfacing a 500. Each invalid shape returns a non-null error; a complete request
// returns null.
public sealed class AccessRequestValidationTests
{
    private static AccessRequest Complete() => new(
        new Subject("user", "user-1", [RoleNames.Teller], PdpRequests.Contoso),
        new ActionRequest(ActionNames.AccountRead),
        new Resource("account", Tenant: PdpRequests.Contoso),
        new EvaluationContext([ScopeNames.Read]));

    [Fact]
    public void Validate_CompleteRequest_ReturnsNull() =>
        Assert.Null(AccessRequestValidation.Validate(Complete()));

    [Fact]
    public void Validate_NullRequest_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(null));

    [Fact]
    public void Validate_EmptyBody_AllNestedRecordsNull_ReturnsError()
    {
        // Mirrors what System.Text.Json produces for a "{}" evaluate body.
        var empty = new AccessRequest(null!, null!, null!, null!);
        Assert.NotNull(AccessRequestValidation.Validate(empty));
    }

    [Fact]
    public void Validate_NullSubject_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            null!,
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext([]))));

    [Fact]
    public void Validate_NullAction_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            null!,
            new Resource("account"),
            new EvaluationContext([]))));

    [Fact]
    public void Validate_NullResource_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ActionNames.AccountRead),
            null!,
            new EvaluationContext([]))));

    [Fact]
    public void Validate_NullContext_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            null!)));

    [Fact]
    public void Validate_NullRoles_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", null!),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext([]))));

    [Fact]
    public void Validate_NullScopes_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext(null!))));

    [Fact]
    public void Validate_EmptyActionName_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(""),
            new Resource("account"),
            new EvaluationContext([]))));

    [Fact]
    public void Validate_EmptySubjectId_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext([]))));

    [Fact]
    public void Validate_EmptyResourceType_ReturnsError() =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource(""),
            new EvaluationContext([]))));

    // Whitespace-only required string fields are malformed and must be rejected at the boundary
    // (IsNullOrWhiteSpace), not pass validation and reach evaluation.

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_WhitespaceSubjectType_ReturnsError(string ws) =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject(ws, "u", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext([]))));

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_WhitespaceSubjectId_ReturnsError(string ws) =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", ws, []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account"),
            new EvaluationContext([]))));

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Validate_WhitespaceActionName_ReturnsError(string ws) =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ws),
            new Resource("account"),
            new EvaluationContext([]))));

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Validate_WhitespaceResourceType_ReturnsError(string ws) =>
        Assert.NotNull(AccessRequestValidation.Validate(new AccessRequest(
            new Subject("user", "u", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource(ws),
            new EvaluationContext([]))));
}
