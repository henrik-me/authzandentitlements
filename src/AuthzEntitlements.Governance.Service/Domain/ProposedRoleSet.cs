namespace AuthzEntitlements.Governance.Service.Domain;

// Pure computation of the role set a SoD check must evaluate: the principal's baseline
// (standing) roles UNION the roles the requested access package would grant. Deduplicated
// (ordinal) and ordered so the SoD wire payload is deterministic — the same request
// always produces the same proposed-roles array.
public static class ProposedRoleSet
{
    public static IReadOnlyList<string> Compute(
        IEnumerable<string> baselineRoles, IEnumerable<string> packageRoles) =>
        baselineRoles
            .Concat(packageRoles)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
}
