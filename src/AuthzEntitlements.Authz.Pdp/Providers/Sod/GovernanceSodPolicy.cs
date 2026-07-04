using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.Sod;

// The governance segregation-of-duties (SoD) rule as a pure, engine-agnostic helper: given a
// principal's PROPOSED resulting role set, decide whether it contains a toxic combination. It
// mirrors the identical rule encoded in the OPA/Rego policy (infra/opa/policy/authz.rego) so the
// in-process reference engine and the out-of-process OPA engine return the same verdict for
// governance.access.request.
//
// A proposed role set conflicts when it contains BOTH members of any incompatible (unordered)
// pair. Rationale: a Teller (maker) must not also hold an approval/oversight role
// (BranchManager/ComplianceOfficer), and an Auditor must stay independent of every
// operational/approval role. Two oversight roles together (BranchManager + ComplianceOfficer)
// are allowed. An empty or single-role set never conflicts; roles outside the listed pairs are
// ignored. Role strings are matched exactly and case-sensitively against RoleNames.
public static class GovernanceSodPolicy
{
    // The stable, machine-matchable reason code a conflict denies with. It is the shared
    // ReasonCodes.SodConflict (single source of truth) so the reference engine, the OPA adapter's
    // accepted-reason allow-list, and audit all agree on the governance SoD verdict.
    public const string SodConflictReasonCode = ReasonCodes.SodConflict;

    // The incompatible (unordered) role pairs. A proposed role set that holds BOTH members of any
    // pair is a segregation-of-duties conflict. Kept in a fixed order so the reported pair (used in
    // the Deny message) is deterministic. Mirrors the sod_incompatible_pairs set in authz.rego.
    private static readonly (string First, string Second)[] IncompatiblePairs =
    [
        (RoleNames.Teller, RoleNames.BranchManager),
        (RoleNames.Teller, RoleNames.ComplianceOfficer),
        (RoleNames.Auditor, RoleNames.Teller),
        (RoleNames.Auditor, RoleNames.BranchManager),
        (RoleNames.Auditor, RoleNames.ComplianceOfficer),
    ];

    // Returns the first incompatible pair (in the declared order above) both present in the
    // proposed role set, or null when the set is conflict-free — including an empty or single-role
    // set. Deterministic: the same input always yields the same reported pair.
    public static IncompatibleRolePair? FindConflict(IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        // A set for O(1) membership; exact, case-sensitive matching against RoleNames.
        var held = new HashSet<string>(roles, StringComparer.Ordinal);

        foreach (var (first, second) in IncompatiblePairs)
        {
            if (held.Contains(first) && held.Contains(second))
            {
                return new IncompatibleRolePair(first, second);
            }
        }

        return null;
    }

    // Convenience predicate for callers that only need the yes/no answer.
    public static bool HasConflict(IReadOnlyList<string> roles) => FindConflict(roles) is not null;
}

// An incompatible role pair detected in a proposed role set. First/Second are the exact RoleNames
// constants in the policy's declared pair order, so the pair reads deterministically in a Deny
// message regardless of the order the roles arrived in.
public readonly record struct IncompatibleRolePair(string First, string Second);
