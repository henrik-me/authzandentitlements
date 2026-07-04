using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters;

// The single RBAC question an engine adapter delegates to its underlying engine: given a
// role-gated action and the subject's roles, is the subject eligible (role-wise) to perform
// it? The shared FintechRuleEvaluator owns the fintech decision *shape* — the per-action
// ordered pipeline plus the ABAC checks (scope presence, tenant isolation, subject-is-maker,
// pending status, segregation of duties) and the approval-threshold obligation — and calls
// this hook only at the role step of a role-gated action. Each CS06-CS09 adapter answers it
// with its engine (ASP.NET Core role policies, a Casbin RBAC model+policy, ...). Keeping the
// eligible-role *sets* inside the engine (an ASP.NET policy, a Casbin RBAC policy) is what
// makes these genuine engine integrations rather than hard-coded role lists.
public interface IEngineRoleAuthorizer
{
    // True iff at least one of the subject's roles is authorized for the action by the engine.
    // Only called for role-gated actions (account.create, transaction.create/approve/reject);
    // bank.account.read has no role gate and never reaches this hook.
    bool IsRoleAuthorized(string action, IReadOnlyList<string> subjectRoles);

    // The engine's config name ("casbin" / "aspnet"), used as DecisionExplanation.Engine so the
    // shared FintechRuleEvaluator can attach an engine-tagged explanation to every decision (CS16).
    string EngineName { get; }

    // The engine-native role artifact for the explanation: the matched policy line / requirement
    // (or, when no subject role matched, the required policy lines) surfaced as a PolicyReference
    // whose Kind is the engine's own kind (casbin-rule / aspnet-requirement). Called at the role
    // step of a role-gated action so the explanation names the engine's actual determining rule.
    PolicyReference DescribeRoleRule(string action, IReadOnlyList<string> subjectRoles);
}
