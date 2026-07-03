namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters;

// The single RBAC question an engine adapter delegates to its underlying engine: given a
// role-gated action and the subject's roles, is the subject eligible (role-wise) to perform
// it? The shared FintechRuleEvaluator owns the fintech decision *shape* — the per-action
// ordered pipeline plus the ABAC checks (scope presence, tenant isolation, subject-is-maker,
// pending status, segregation of duties) and the approval-threshold obligation — and calls
// this hook only at the role step of a role-gated action. Each CS06-CS09 adapter answers it
// with its engine (ASP.NET Core role policies, a Casbin RBAC model+policy, ...). Keeping the
// eligible-role *sets* inside the engine (an ASP.NET policy, a Casbin policy.csv) is what
// makes these genuine engine integrations rather than hard-coded role lists.
public interface IEngineRoleAuthorizer
{
    // True iff at least one of the subject's roles is authorized for the action by the engine.
    // Only called for role-gated actions (account.create, transaction.create/approve/reject);
    // bank.account.read has no role gate and never reaches this hook.
    bool IsRoleAuthorized(string action, IReadOnlyList<string> subjectRoles);
}
