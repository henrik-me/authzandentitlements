using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Pure, offline mapping-hygiene tests for CerbosRequestMapper: a blank/whitespace resource id or role
// normalizes to a stable placeholder so an effectively-empty value is never sent to Cerbos (which
// requires a non-empty resource id and non-empty role strings).
public sealed class CerbosRequestMapperTests
{
    private static AccessRequest RequestWith(Subject subject, string? resourceId) =>
        new(
            subject,
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: resourceId, Tenant: "CONTOSO"),
            new EvaluationContext([]));

    [Theory]
    [InlineData("acme-checking", "acme-checking")]
    [InlineData(null, CerbosRequestMapper.ResourceIdPlaceholder)]
    [InlineData("", CerbosRequestMapper.ResourceIdPlaceholder)]
    [InlineData("   ", CerbosRequestMapper.ResourceIdPlaceholder)]
    public void ResourceIdFor_NormalizesBlankToPlaceholder(string? id, string expected)
    {
        var request = RequestWith(new Subject("user", "u", ["Teller"], "CONTOSO"), id);

        Assert.Equal(expected, CerbosRequestMapper.ResourceIdFor(request));
    }

    [Fact]
    public void PrincipalRoles_DropsBlankRoles()
    {
        var subject = new Subject("user", "u", ["Teller", "  ", "", "Manager"], "CONTOSO");

        Assert.Equal(new[] { "Teller", "Manager" }, CerbosRequestMapper.PrincipalRoles(subject));
    }

    [Fact]
    public void PrincipalRoles_WhenAllBlankOrEmpty_UsesNonBlankPlaceholder()
    {
        var roles = CerbosRequestMapper.PrincipalRoles(new Subject("user", "u", ["", "   "], "CONTOSO"));

        Assert.Single(roles);
        Assert.False(string.IsNullOrWhiteSpace(roles[0]));
    }
}
