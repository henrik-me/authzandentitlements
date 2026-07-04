using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Migration;

// Mechanically converts an RBAC policy into an OpenFGA "roles as usersets" ReBAC model — the
// textbook RBAC->ReBAC migration pattern. It is pure and deterministic: no DB, no server, and
// the same policy always yields byte-identical model JSON and a stably ordered tuple list.
//
// The emitted model has three types:
//   * user     — the principals.
//   * role     — one relation, assignee: [user], so "role:R#assignee" is the set of R's members.
//   * resource — one relation per RBAC permission, each directly related from role#assignee, so
//                granting a role a permission is a single tuple against the shared resource object.
public static class RbacToRebacTranslator
{
    private const string UserType = "user";
    private const string RoleType = "role";
    private const string ResourceType = "resource";
    private const string ResourceObjectId = "core";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static TranslatedRebacGraph Translate(RbacPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var roles = policy.Roles.OrderBy(r => r, StringComparer.Ordinal).ToList();
        var permissions = policy.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList();

        var (permissionToRelation, relationToPermission) = BuildRelationMap(permissions);
        var modelJson = BuildModelJson(permissions, permissionToRelation);
        var tuples = BuildTuples(policy, roles, permissionToRelation);

        return new TranslatedRebacGraph(
            modelJson, tuples, permissionToRelation, relationToPermission, ResourceObjectId);
    }

    // Maps each permission to a valid OpenFGA relation name (lowercase; '.', ':', '#', '@' and
    // whitespace collapse to '_'). The map is bidirectional; a name collision between two distinct
    // permissions is a fail-closed error because it would silently merge their grants.
    private static (IReadOnlyDictionary<string, string> ToRelation, IReadOnlyDictionary<string, string> ToPermission)
        BuildRelationMap(IReadOnlyList<string> permissions)
    {
        var toRelation = new Dictionary<string, string>(StringComparer.Ordinal);
        var toPermission = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            var relation = Sanitize(permission);
            if (toPermission.TryGetValue(relation, out var existing))
            {
                throw new ArgumentException(
                    $"Permissions '{existing}' and '{permission}' both sanitize to the OpenFGA " +
                    $"relation '{relation}'; permission names must translate to distinct relations.",
                    nameof(permissions));
            }

            toRelation[permission] = relation;
            toPermission[relation] = permission;
        }

        return (toRelation, toPermission);
    }

    private static string Sanitize(string permission)
    {
        var builder = new StringBuilder(permission.Length);
        foreach (var ch in permission)
        {
            builder.Append(ch switch
            {
                '.' or ':' or '#' or '@' => '_',
                _ when char.IsWhiteSpace(ch) => '_',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return builder.ToString();
    }

    private static string BuildModelJson(
        IReadOnlyList<string> permissions,
        IReadOnlyDictionary<string, string> permissionToRelation)
    {
        var resourceRelations = new JsonObject();
        var resourceMetadata = new JsonObject();
        foreach (var permission in permissions)
        {
            var relation = permissionToRelation[permission];
            resourceRelations[relation] = new JsonObject { ["this"] = new JsonObject() };
            resourceMetadata[relation] = new JsonObject
            {
                ["directly_related_user_types"] = new JsonArray(
                    new JsonObject { ["type"] = RoleType, ["relation"] = TranslatedRebacGraph.AssigneeRelation }),
            };
        }

        var model = new JsonObject
        {
            ["schema_version"] = "1.1",
            ["type_definitions"] = new JsonArray(
                new JsonObject { ["type"] = UserType },
                new JsonObject
                {
                    ["type"] = RoleType,
                    ["relations"] = new JsonObject
                    {
                        [TranslatedRebacGraph.AssigneeRelation] = new JsonObject { ["this"] = new JsonObject() },
                    },
                    ["metadata"] = new JsonObject
                    {
                        ["relations"] = new JsonObject
                        {
                            [TranslatedRebacGraph.AssigneeRelation] = new JsonObject
                            {
                                ["directly_related_user_types"] = new JsonArray(
                                    new JsonObject { ["type"] = UserType }),
                            },
                        },
                    },
                },
                new JsonObject
                {
                    ["type"] = ResourceType,
                    ["relations"] = resourceRelations,
                    ["metadata"] = new JsonObject { ["relations"] = resourceMetadata },
                }),
        };

        return model.ToJsonString(SerializerOptions);
    }

    private static IReadOnlyList<RebacTuple> BuildTuples(
        RbacPolicy policy,
        IReadOnlyList<string> roles,
        IReadOnlyDictionary<string, string> permissionToRelation)
    {
        var tuples = new List<RebacTuple>();
        var resourceObject = $"{ResourceType}:{ResourceObjectId}";

        // User -> role assignments: user:{u} is an assignee of role:{r}.
        foreach (var user in policy.UserRoles.Keys.OrderBy(u => u, StringComparer.Ordinal))
        {
            foreach (var role in policy.UserRoles[user].OrderBy(r => r, StringComparer.Ordinal))
            {
                tuples.Add(new RebacTuple(
                    $"{UserType}:{user}", TranslatedRebacGraph.AssigneeRelation, $"{RoleType}:{role}"));
            }
        }

        // Role -> permission grants: the role#assignee userset holds the permission relation on the
        // shared resource object.
        foreach (var role in roles)
        {
            if (!policy.RolePermissions.TryGetValue(role, out var grants))
            {
                continue;
            }

            foreach (var permission in grants.OrderBy(p => p, StringComparer.Ordinal))
            {
                tuples.Add(new RebacTuple(
                    $"{RoleType}:{role}#{TranslatedRebacGraph.AssigneeRelation}",
                    permissionToRelation[permission],
                    resourceObject));
            }
        }

        return tuples;
    }
}
