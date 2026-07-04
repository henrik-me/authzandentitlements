using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters;
using Casbin;
using Casbin.Model;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Casbin;

// Engine adapter backed by a genuine Casbin.NET RBAC model + policy for the single
// role-eligibility question. The model text and the (role, action) policy pairs are embedded
// as strings and loaded programmatically — no external files, no container, no
// EmbeddedResource entries — so the adapter is self-contained in the container-free "lite"
// profile. The shared FintechRuleEvaluator composes the full fintech ABAC decision (scope,
// tenant isolation, subject-is-maker, pending, segregation of duties, the approval-threshold
// obligation, and the fail-closed unknown-action deny) on top of this engine-owned role gate,
// keeping the adapter in lock-step parity with the reference provider.
public sealed class CasbinDecisionProvider : IAuthorizationDecisionProvider, IEngineRoleAuthorizer
{
    // A minimal RBAC request/policy: a (role, action) request is allowed iff a policy line
    // (role, action) matches it exactly. Role -> action grants are the policy pairs below.
    private const string ModelText = """
        [request_definition]
        r = sub, act

        [policy_definition]
        p = sub, act

        [policy_effect]
        e = some(where (p.eft == allow))

        [matchers]
        m = r.sub == p.sub && r.act == p.act
        """;

    private readonly IEnforcer _enforcer;

    public CasbinDecisionProvider()
    {
        var model = DefaultModel.CreateFromText(ModelText);
        _enforcer = new Enforcer(model);
        foreach (var (role, action) in RolePolicies())
        {
            _enforcer.AddPolicy(role, action);
        }
    }

    public string Name => "casbin";

    public string EngineName => "casbin";

    public AccessDecision Evaluate(AccessRequest request) =>
        FintechRuleEvaluator.Evaluate(request, this);

    // The subject is role-eligible iff the Casbin enforcer permits at least one of its roles for
    // the action. Read is not role-gated (no policy pair), so it enforces to false and never
    // reaches here — the shared evaluator only calls this hook for role-gated actions.
    public bool IsRoleAuthorized(string action, IReadOnlyList<string> subjectRoles) =>
        subjectRoles.Any(role => _enforcer.Enforce(role, action));

    // The engine-native role artifact for CS16 explanations: the Casbin policy line the subject
    // matched ("p, <role>, <action>"), determined via the same _enforcer.Enforce loop the role
    // gate uses. When no subject role matched, it surfaces the policy lines the action requires so
    // the explanation still names the engine's actual determining rules.
    public PolicyReference DescribeRoleRule(string action, IReadOnlyList<string> subjectRoles)
    {
        var matched = subjectRoles.FirstOrDefault(role => _enforcer.Enforce(role, action));
        if (matched is not null)
        {
            return new PolicyReference(
                PolicyReferenceKinds.CasbinRule,
                $"p, {matched}, {action}",
                $"Casbin policy line matched by the subject's '{matched}' role.");
        }

        var required = RequiredRolesFor(action);
        if (required.Count == 0)
        {
            return new PolicyReference(
                PolicyReferenceKinds.CasbinRule,
                $"(no policy line grants '{action}')",
                $"No Casbin policy grants '{action}'.");
        }

        return new PolicyReference(
            PolicyReferenceKinds.CasbinRule,
            string.Join("; ", required.Select(role => $"p, {role}, {action}")),
            $"No subject role matched; action requires one of: {string.Join(", ", required)}.");
    }

    // The roles the RBAC policy grants for an action (the (role, action) policy pairs), used to
    // describe the required policy lines when no subject role matched.
    private static IReadOnlyList<string> RequiredRolesFor(string action) =>
        RolePolicies().Where(policy => policy.Action == action).Select(policy => policy.Role).ToList();

    // The (role, action) grants encoding the fintech RBAC baseline: BranchManager may create
    // accounts; Teller/BranchManager/ComplianceOfficer may originate transactions;
    // BranchManager/ComplianceOfficer may decide (approve/reject) approvals.
    private static IEnumerable<(string Role, string Action)> RolePolicies() =>
    [
        (RoleNames.BranchManager, ActionNames.AccountCreate),

        (RoleNames.Teller, ActionNames.TransactionCreate),
        (RoleNames.BranchManager, ActionNames.TransactionCreate),
        (RoleNames.ComplianceOfficer, ActionNames.TransactionCreate),

        (RoleNames.BranchManager, ActionNames.TransactionApprove),
        (RoleNames.ComplianceOfficer, ActionNames.TransactionApprove),

        (RoleNames.BranchManager, ActionNames.TransactionReject),
        (RoleNames.ComplianceOfficer, ActionNames.TransactionReject),
    ];
}
