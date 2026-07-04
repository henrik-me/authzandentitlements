using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Migration;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The RBAC->ReBAC translator (CS20 D2): the mechanical "roles as usersets" conversion plus the
// in-process resolver that proves the translated ReBAC graph decides identically to the source
// RBAC policy — with no live OpenFGA server. The headline assertion is PARITY: for the full
// user x permission grid, the emitted graph's Check matches the RBAC IsAllowed.
public sealed class RbacToRebacTranslatorTests
{
    // The fully-qualified resource object the translated graph writes permission grants against —
    // sourced from the graph's own ResourceObject property rather than hardcoding the "resource:" prefix.
    private static readonly string ResourceObject =
        RbacToRebacTranslator.Translate(FintechRbacPolicy.Policy).ResourceObject;

    private static readonly string[] ExpectedPermissions =
    [
        ActionNames.AccountCreate,
        ActionNames.TransactionCreate,
        ActionNames.TransactionApprove,
        ActionNames.TransactionReject,
    ];

    private static TranslatedRebacGraph Translate() =>
        RbacToRebacTranslator.Translate(FintechRbacPolicy.Policy);

    private static JsonElement FindType(JsonElement model, string type)
    {
        foreach (var definition in model.GetProperty("type_definitions").EnumerateArray())
        {
            if (definition.GetProperty("type").GetString() == type)
            {
                return definition;
            }
        }

        throw new Xunit.Sdk.XunitException($"type '{type}' not found in emitted model");
    }

    [Fact]
    public void ResourceObject_IsTheFullyQualifiedObjectString()
    {
        var graph = Translate();

        Assert.Equal("core", graph.ResourceObjectId);
        Assert.Equal("resource:core", graph.ResourceObject);
        Assert.Equal($"resource:{graph.ResourceObjectId}", graph.ResourceObject);
    }

    [Fact]
    public void Model_Parses_AndDefinesUserRoleResourceTypes()
    {
        using var document = JsonDocument.Parse(Translate().ModelJson);
        var root = document.RootElement;

        Assert.Equal("1.1", root.GetProperty("schema_version").GetString());
        var types = root.GetProperty("type_definitions").EnumerateArray()
            .Select(t => t.GetProperty("type").GetString())
            .ToArray();
        Assert.Contains("user", types);
        Assert.Contains("role", types);
        Assert.Contains("resource", types);
    }

    [Fact]
    public void Model_RoleType_HasAssigneeRelationDirectlyRelatedToUser()
    {
        using var document = JsonDocument.Parse(Translate().ModelJson);
        var role = FindType(document.RootElement, "role");

        Assert.True(role.GetProperty("relations").TryGetProperty("assignee", out _));
        var related = role.GetProperty("metadata").GetProperty("relations")
            .GetProperty("assignee").GetProperty("directly_related_user_types");
        var single = Assert.Single(related.EnumerateArray());
        Assert.Equal("user", single.GetProperty("type").GetString());
        Assert.False(single.TryGetProperty("relation", out _));
    }

    [Fact]
    public void Model_ResourceType_HasExactlyOneRelationPerPermission()
    {
        var graph = Translate();
        using var document = JsonDocument.Parse(graph.ModelJson);
        var resource = FindType(document.RootElement, "resource");

        var relations = resource.GetProperty("relations").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(ExpectedPermissions.Length, relations.Length);
        foreach (var permission in ExpectedPermissions)
        {
            Assert.Contains(graph.PermissionToRelation[permission], relations);
        }
    }

    [Fact]
    public void Model_EachPermissionRelation_IsDirectlyRelatedToRoleAssignee()
    {
        var graph = Translate();
        using var document = JsonDocument.Parse(graph.ModelJson);
        var resource = FindType(document.RootElement, "resource");
        var metadata = resource.GetProperty("metadata").GetProperty("relations");

        foreach (var permission in ExpectedPermissions)
        {
            var relation = graph.PermissionToRelation[permission];
            var related = metadata.GetProperty(relation).GetProperty("directly_related_user_types");
            var single = Assert.Single(related.EnumerateArray());
            Assert.Equal("role", single.GetProperty("type").GetString());
            Assert.Equal("assignee", single.GetProperty("relation").GetString());
        }
    }

    [Fact]
    public void RelationNames_AreSanitizedAndBidirectional()
    {
        var graph = Translate();

        Assert.Equal("bank_account_create", graph.PermissionToRelation[ActionNames.AccountCreate]);
        Assert.Equal("bank_transaction_approve", graph.PermissionToRelation[ActionNames.TransactionApprove]);
        foreach (var (permission, relation) in graph.PermissionToRelation)
        {
            Assert.Matches("^[a-z][a-z0-9_]{0,62}$", relation);
            Assert.Equal(permission, graph.RelationToPermission[relation]);
        }
    }

    [Fact]
    public void Translate_CollapsesEveryNonRelationChar_ToUnderscore()
    {
        var graph = RbacToRebacTranslator.Translate(SingleUserPolicy("read/write-Thing"));

        var relation = graph.PermissionToRelation["read/write-Thing"];
        Assert.Equal("read_write_thing", relation);
        Assert.Matches("^[a-z][a-z0-9_]{0,62}$", relation);
        Assert.True(graph.Check("user:u", "read/write-Thing", ResourceObject));
    }

    [Fact]
    public void Translate_Throws_OnEmptyOrWhitespacePermissionName()
    {
        Assert.Throws<ArgumentException>(() => RbacToRebacTranslator.Translate(DeclaredPermissionsPolicy(" ")));
    }

    [Fact]
    public void Translate_Throws_OnPermissionExceedingRelationLengthLimit()
    {
        // 64 characters — one past OpenFGA's 63-character relation-name limit.
        var tooLong = new string('a', 64);

        var ex = Assert.Throws<ArgumentException>(
            () => RbacToRebacTranslator.Translate(DeclaredPermissionsPolicy(tooLong)));
        Assert.Contains("63", ex.Message);
    }

    [Theory]
    [InlineData("9lives")]  // sanitizes to "9lives" — starts with a digit
    [InlineData("-hidden")] // sanitizes to "_hidden" — starts with '_'
    public void Translate_Throws_WhenSanitizedRelationHasInvalidLeadingChar(string permission)
    {
        // OpenFGA relation identifiers must start with a lowercase letter; a name that sanitizes to
        // a leading digit/underscore is rejected fail-closed rather than emitted as an invalid model.
        Assert.Throws<ArgumentException>(
            () => RbacToRebacTranslator.Translate(DeclaredPermissionsPolicy(permission)));
    }

    [Fact]
    public void Translate_Throws_OnDistinctPermissionsCollidingToSameRelation()
    {
        // "a.b" and "a:b" both sanitize to "a_b"; emitting both would silently merge their grants.
        var ex = Assert.Throws<ArgumentException>(
            () => RbacToRebacTranslator.Translate(DeclaredPermissionsPolicy("a.b", "a:b")));
        Assert.Contains("a_b", ex.Message);
    }

    // An RBAC policy declaring the given permissions (no grants/assignments) — enough to drive the
    // translator's relation-name sanitization/validation without any user or role wiring.
    private static RbacPolicy DeclaredPermissionsPolicy(params string[] permissions) => RbacPolicy.Create(
        roles: ["R"],
        permissions: permissions,
        rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
        userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    // An RBAC policy where user "u" holds role "R" which grants the single given permission — so the
    // emitted graph's Check can be exercised for that permission.
    private static RbacPolicy SingleUserPolicy(string permission) => RbacPolicy.Create(
        roles: ["R"],
        permissions: [permission],
        rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["R"] = new[] { permission },
        },
        userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["u"] = new[] { "R" },
        });

    [Fact]
    public void Tuples_HaveExpectedCount_AndUsersetForms()
    {
        var graph = Translate();

        var userRolePairs = FintechRbacPolicy.Policy.UserRoles.Sum(kv => kv.Value.Count);
        var rolePermissionPairs = FintechRbacPolicy.Policy.RolePermissions.Sum(kv => kv.Value.Count);
        Assert.Equal(userRolePairs + rolePermissionPairs, graph.Tuples.Count);

        Assert.Contains(graph.Tuples,
            t => t is { User: "user:teller-anna", Relation: "assignee", Object: "role:Teller" });
        Assert.Contains(graph.Tuples,
            t => t is { User: "role:Teller#assignee", Relation: "bank_transaction_create", Object: "resource:core" });
    }

    [Fact]
    public void Translate_IsDeterministic_AcrossCalls()
    {
        var first = Translate();
        var second = Translate();

        Assert.Equal(first.ModelJson, second.ModelJson);
        Assert.Equal(first.Tuples, second.Tuples);
    }

    [Fact]
    public void Check_MatchesRbac_AcrossFullUserPermissionGrid()
    {
        var policy = FintechRbacPolicy.Policy;
        var graph = RbacToRebacTranslator.Translate(policy);

        foreach (var user in policy.UserRoles.Keys)
        {
            foreach (var permission in policy.Permissions)
            {
                var expected = policy.IsAllowed(user, permission);
                var actual = graph.Check($"user:{user}", permission, ResourceObject);
                Assert.True(expected == actual,
                    $"parity mismatch for user '{user}', permission '{permission}': " +
                    $"RBAC={expected}, ReBAC={actual}");
            }
        }
    }

    [Fact]
    public void Check_Permits_TellerOnlyForTransactionCreate()
    {
        var graph = Translate();

        Assert.True(graph.Check("user:teller-anna", ActionNames.TransactionCreate, ResourceObject));
        Assert.False(graph.Check("user:teller-anna", ActionNames.AccountCreate, ResourceObject));
        Assert.False(graph.Check("user:teller-anna", ActionNames.TransactionApprove, ResourceObject));
    }

    [Fact]
    public void Check_Denies_AuditorForEveryPermission()
    {
        var graph = Translate();

        foreach (var permission in FintechRbacPolicy.Policy.Permissions)
        {
            Assert.False(graph.Check("user:auditor-dan", permission, ResourceObject));
        }
    }

    [Fact]
    public void Check_MultiRoleUser_GetsUnionOfRoleGrants()
    {
        var graph = Translate();

        // manager-and-compliance holds BranchManager + ComplianceOfficer, so the union covers
        // every fintech permission, including the BranchManager-only account.create.
        foreach (var permission in FintechRbacPolicy.Policy.Permissions)
        {
            Assert.True(graph.Check("user:manager-and-compliance", permission, ResourceObject));
        }
    }

    [Fact]
    public void Check_FailsClosed_OnUnknownPermissionUserAndResource()
    {
        var graph = Translate();

        Assert.False(graph.Check("user:branch-mgr-ben", "bank.account.delete", ResourceObject));
        Assert.False(graph.Check("user:ghost", ActionNames.TransactionCreate, ResourceObject));
        Assert.False(graph.Check("user:branch-mgr-ben", ActionNames.TransactionCreate, "resource:other"));
    }

    [Fact]
    public void RbacPolicy_Create_Throws_OnUndeclaredRoleInGrants()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["Teller"],
            permissions: ["p1"],
            rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["Ghost"] = new[] { "p1" },
            },
            userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)));

        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void RbacPolicy_Create_Throws_OnUndeclaredPermissionInGrants()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["Teller"],
            permissions: ["p1"],
            rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["Teller"] = new[] { "p2" },
            },
            userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)));

        Assert.Contains("p2", ex.Message);
    }

    [Fact]
    public void RbacPolicy_Create_Throws_OnUndeclaredRoleInUserAssignment()
    {
        var ex = Assert.Throws<ArgumentException>(() => RbacPolicy.Create(
            roles: ["Teller"],
            permissions: ["p1"],
            rolePermissions: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            userRoles: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["anna"] = new[] { "Ghost" },
            }));

        Assert.Contains("Ghost", ex.Message);
    }

    [Fact]
    public void RbacPolicy_IsAllowed_FailsClosed_OnUnknownUserOrPermission()
    {
        var policy = FintechRbacPolicy.Policy;

        Assert.False(policy.IsAllowed("nobody", ActionNames.TransactionCreate));
        Assert.False(policy.IsAllowed("teller-anna", "bank.account.delete"));
    }
}
