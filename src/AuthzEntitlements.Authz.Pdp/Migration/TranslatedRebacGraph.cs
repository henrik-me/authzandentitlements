using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Migration;

// The output of RbacToRebacTranslator: the emitted OpenFGA schema-1.1 authorization model
// (as JSON), the relationship tuples, the permission<->relation name map, the resource object
// the permission relations hang off, and an in-process resolver that evaluates the
// "roles as usersets" semantics directly over the tuples. The resolver is what lets the parity
// tests prove the translation is faithful WITHOUT standing up a live OpenFGA server.
public sealed class TranslatedRebacGraph
{
    // The relation on the role type that binds users into it (role#assignee is the userset the
    // resource's permission relations are directly related to).
    internal const string AssigneeRelation = "assignee";

    private readonly HashSet<RebacTuple> _tuples;

    internal TranslatedRebacGraph(
        string modelJson,
        IReadOnlyList<RebacTuple> tuples,
        IReadOnlyDictionary<string, string> permissionToRelation,
        IReadOnlyDictionary<string, string> relationToPermission,
        string resourceObjectId)
    {
        ModelJson = modelJson;
        Tuples = tuples;
        PermissionToRelation = permissionToRelation;
        RelationToPermission = relationToPermission;
        ResourceObjectId = resourceObjectId;
        _tuples = new HashSet<RebacTuple>(tuples);
    }

    public string ModelJson { get; }

    public IReadOnlyList<RebacTuple> Tuples { get; }

    public IReadOnlyDictionary<string, string> PermissionToRelation { get; }

    public IReadOnlyDictionary<string, string> RelationToPermission { get; }

    // The bare object id (e.g. "core") the permission relations are written against; the full
    // OpenFGA object string is "resource:{ResourceObjectId}".
    public string ResourceObjectId { get; }

    // Evaluates the roles-as-usersets model directly over the emitted tuples, mirroring how an
    // OpenFGA Check would resolve it: the user has the permission relation on the resource iff
    // some role R has a "role:R#assignee -> permRelation -> resourceObject" grant AND the user has
    // a "userObject -> assignee -> role:R" assignment. Unknown permission, user, or resource all
    // resolve to false (fail closed) — never an implicit allow.
    public bool Check(string userObject, string permission, string resourceObject)
    {
        if (!PermissionToRelation.TryGetValue(permission, out var relation))
        {
            return false;
        }

        foreach (var grant in _tuples)
        {
            if (!string.Equals(grant.Relation, relation, StringComparison.Ordinal)
                || !string.Equals(grant.Object, resourceObject, StringComparison.Ordinal)
                || !grant.User.EndsWith($"#{AssigneeRelation}", StringComparison.Ordinal))
            {
                continue;
            }

            var roleObject = grant.User[..^($"#{AssigneeRelation}".Length)];
            if (_tuples.Contains(new RebacTuple(userObject, AssigneeRelation, roleObject)))
            {
                return true;
            }
        }

        return false;
    }
}
