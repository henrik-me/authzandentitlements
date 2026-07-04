using AuthzEntitlements.Authz.Pdp.Migration;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS29 (LRN-044) — the fail-closed hardening of the RBAC policy factory: beyond the existing
// dangling-reference checks (covered in RbacToRebacTranslatorTests), Create now rejects empty
// or duplicate roles/permissions so a mechanically-built policy with degenerate members fails
// at construction rather than emitting a silently-invalid policy.
public sealed class RbacPolicyCreateTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoGrants =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoAssignments =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    [Fact]
    public void Create_Throws_OnEmptyRoles()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: [],
            permissions: ["p1"],
            rolePermissions: NoGrants,
            userRoles: NoAssignments));

        Assert.Equal("roles", ex.ParamName);
    }

    [Fact]
    public void Create_Throws_OnEmptyPermissions()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["R"],
            permissions: [],
            rolePermissions: NoGrants,
            userRoles: NoAssignments));

        Assert.Equal("permissions", ex.ParamName);
    }

    [Fact]
    public void Create_Throws_OnDuplicateRole()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["Teller", "Teller"],
            permissions: ["p1"],
            rolePermissions: NoGrants,
            userRoles: NoAssignments));

        Assert.Equal("roles", ex.ParamName);
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_Throws_OnDuplicatePermission()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["Teller"],
            permissions: ["p1", "p1"],
            rolePermissions: NoGrants,
            userRoles: NoAssignments));

        Assert.Equal("permissions", ex.ParamName);
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_TreatsRolesAndPermissions_OrdinalCaseSensitive()
    {
        // "Teller" and "teller" are distinct Ordinal entries, so this is NOT a duplicate — the
        // factory succeeds, matching the Ordinal sets used for the cross-reference checks.
        var policy = RbacPolicy.Create(
            roles: ["Teller", "teller"],
            permissions: ["p1", "P1"],
            rolePermissions: NoGrants,
            userRoles: NoAssignments);

        Assert.Equal(2, policy.Roles.Count);
        Assert.Equal(2, policy.Permissions.Count);
    }

    [Fact]
    public void Create_Succeeds_OnValidDistinctNonEmptyPolicy()
    {
        var policy = RbacPolicy.Create(
            roles: ["Teller", "BranchManager"],
            permissions: ["bank.transaction.create", "bank.account.create"],
            rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["Teller"] = ["bank.transaction.create"],
                ["BranchManager"] = ["bank.account.create", "bank.transaction.create"],
            },
            userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["anna"] = ["Teller"],
            });

        Assert.Equal(new[] { "Teller", "BranchManager" }, policy.Roles);
        Assert.True(policy.IsAllowed("anna", "bank.transaction.create"));
        Assert.False(policy.IsAllowed("anna", "bank.account.create"));
    }
}
