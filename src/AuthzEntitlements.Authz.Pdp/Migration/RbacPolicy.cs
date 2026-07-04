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

    // Fail-closed factory: the roles/permissions lists must each be non-empty and free of
    // duplicates; every role referenced by a grant or an assignment must be a declared role,
    // and every granted permission must be a declared permission. A degenerate (empty/duplicate)
    // or dangling input is a policy authoring bug, so it throws here rather than silently
    // emitting an invalid policy that evaluates to a wrong decision.
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

        // A policy with no roles or no permissions grants nothing meaningful and is almost
        // certainly a construction bug (e.g. a mechanical build that dropped its members).
        // Fail closed at authoring time rather than emit an empty, always-deny policy.
        if (roles.Count == 0)
        {
            throw new ArgumentException("The policy must declare at least one role.", nameof(roles));
        }

        if (permissions.Count == 0)
        {
            throw new ArgumentException(
                "The policy must declare at least one permission.", nameof(permissions));
        }

        // Duplicate members would collapse into a single set entry and silently mask the
        // authoring mistake (e.g. a copy-paste or a merge that double-added a role). Reject
        // them so the declared lists match the effective sets one-to-one. Ordinal to match
        // the HashSet comparers used for the cross-reference checks below.
        var roleSet = new HashSet<string>(roles, StringComparer.Ordinal);
        if (roleSet.Count != roles.Count)
        {
            throw new ArgumentException("The roles list contains duplicate entries.", nameof(roles));
        }

        var permissionSet = new HashSet<string>(permissions, StringComparer.Ordinal);
        if (permissionSet.Count != permissions.Count)
        {
            throw new ArgumentException(
                "The permissions list contains duplicate entries.", nameof(permissions));
        }

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
