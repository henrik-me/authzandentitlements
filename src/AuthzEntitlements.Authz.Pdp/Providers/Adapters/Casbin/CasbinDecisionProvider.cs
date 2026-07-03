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

    public AccessDecision Evaluate(AccessRequest request) =>
        FintechRuleEvaluator.Evaluate(request, this);

    // The subject is role-eligible iff the Casbin enforcer permits at least one of its roles for
    // the action. Read is not role-gated (no policy pair), so it enforces to false and never
    // reaches here — the shared evaluator only calls this hook for role-gated actions.
    public bool IsRoleAuthorized(string action, IReadOnlyList<string> subjectRoles) =>
        subjectRoles.Any(role => _enforcer.Enforce(role, action));

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
