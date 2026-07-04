namespace AuthzEntitlements.Authz.Pdp.Migration;

// A pure, DB-free RBAC policy: the classic role-based model of roles, permissions, the
// permissions each role grants, and the roles each user is assigned. It is the SOURCE the
// RbacToRebacTranslator mechanically converts into an OpenFGA "roles as usersets" ReBAC
// model, and the ground truth its parity tests check the translated graph against.
public sealed class RbacPolicy
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _rolePermissions;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _userRoles;

    private RbacPolicy(
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> rolePermissions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> userRoles)
    {
        Roles = roles;
        Permissions = permissions;
        _rolePermissions = rolePermissions;
        _userRoles = userRoles;
    }

    public IReadOnlyList<string> Roles { get; }

    public IReadOnlyList<string> Permissions { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions => _rolePermissions;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> UserRoles => _userRoles;

    // Fail-closed factory: every role referenced by a grant or an assignment must be a declared
    // role, and every granted permission must be a declared permission. A dangling reference is a
    // policy authoring bug, so it throws here rather than silently evaluating to a wrong decision.
    public static RbacPolicy Create(
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> rolePermissions,
        IReadOnlyDictionary<string, IReadOnlyList<string>> userRoles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(rolePermissions);
        ArgumentNullException.ThrowIfNull(userRoles);

        var roleSet = new HashSet<string>(roles, StringComparer.Ordinal);
        var permissionSet = new HashSet<string>(permissions, StringComparer.Ordinal);

        foreach (var (role, grants) in rolePermissions)
        {
            if (!roleSet.Contains(role))
            {
                throw new ArgumentException(
                    $"RolePermissions references role '{role}', which is not a declared role.",
                    nameof(rolePermissions));
            }

            foreach (var permission in grants)
            {
                if (!permissionSet.Contains(permission))
                {
                    throw new ArgumentException(
                        $"Role '{role}' grants permission '{permission}', which is not a declared permission.",
                        nameof(rolePermissions));
                }
            }
        }

        foreach (var (user, assigned) in userRoles)
        {
            foreach (var role in assigned)
            {
                if (!roleSet.Contains(role))
                {
                    throw new ArgumentException(
                        $"User '{user}' is assigned role '{role}', which is not a declared role.",
                        nameof(userRoles));
                }
            }
        }

        return new RbacPolicy(roles, permissions, rolePermissions, userRoles);
    }

    // Classic RBAC evaluation: the user is allowed the permission iff some role they hold grants
    // it. An unknown user or permission is denied (fail closed), never treated as a wildcard.
    public bool IsAllowed(string user, string permission)
    {
        if (!_userRoles.TryGetValue(user, out var assignedRoles))
        {
            return false;
        }

        foreach (var role in assignedRoles)
        {
            if (_rolePermissions.TryGetValue(role, out var grants)
                && grants.Contains(permission, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
