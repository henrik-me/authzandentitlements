using AuthzEntitlements.Authz.Pdp.Contracts;
using MonoCloud.Cedar.Model.Policy;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cedar;

// The embedded Cedar authorization model for the "cedar" adapter. There are NO external files,
// schemas, or containers — the policies are C# string constants and the PolicySet is built once
// from Policy objects carrying STABLE, semantic ids (container-free "lite" profile, same posture
// as the embedded Casbin model).
//
// Strategy for exact reason-code parity with the reference engine (the crux of CS09): for every
// action Cedar owns a BROAD permit plus one annotated forbid per deny reason. Cedar's forbid-over-
// permit semantics mean a request is denied iff at least one forbid matches, and the authorization
// diagnostics return the set of DETERMINING forbid ids. Each forbid is constructed with an explicit
// Policy id (Policy(src, id)) so that determining set is a set of our stable ids — verified against
// the native engine, which echoes Policy.GetID() in the response reason (MonoCloud.Cedar assigns
// sequential policyN ids when parsing raw text, so ids are set explicitly rather than via @id).
//
// When a combined-failure input trips MULTIPLE forbids, the adapter maps the determining set to the
// reason that is FIRST-FAILING in the reference's per-action order — the one with the lowest
// Precedence VALUE below (the adapter orders ascending by Precedence and takes the first). This
// guarantees primary-reason-code parity for ANY input, not just the isolated-failure catalog rows
// (LRN-021: combined failures must return the reference's FIRST-failing reason).
internal static class CedarPolicyModel
{
    // Cedar action clauses. Approve and reject share one ordered rule family (same reference order),
    // so their forbids/permit target both actions via `action in [..]`.
    private const string ReadAction = $"action == Action::\"{ActionNames.AccountRead}\"";
    private const string AccountCreateAction = $"action == Action::\"{ActionNames.AccountCreate}\"";
    private const string TransactionCreateAction = $"action == Action::\"{ActionNames.TransactionCreate}\"";
    private const string ApprovalAction =
        $"action in [Action::\"{ActionNames.TransactionApprove}\", Action::\"{ActionNames.TransactionReject}\"]";

    // Each forbid: stable id, the reason code it maps to, its precedence (lower = first-failing in the
    // reference order), and the Cedar policy source. The permit ids are separate (not deny reasons).
    private static readonly ForbidPolicy[] Forbids =
    [
        // read: MissingScope(read) -> TenantMismatch
        new("read.MissingScope", ReasonCodes.MissingScope, 0,
            $"forbid(principal, {ReadAction}, resource) unless {{ context.scopes.contains(\"{ScopeNames.Read}\") }};"),
        new("read.TenantMismatch", ReasonCodes.TenantMismatch, 1,
            $"forbid(principal, {ReadAction}, resource) unless {{ principal.tenant != \"\" && resource.tenant != \"\" && principal.tenant == resource.tenant }};"),

        // account.create: RoleNotAuthorized(BranchManager) -> TenantMismatch
        new("account.create.RoleNotAuthorized", ReasonCodes.RoleNotAuthorized, 0,
            $"forbid(principal, {AccountCreateAction}, resource) unless {{ principal.roles.contains(\"{RoleNames.BranchManager}\") }};"),
        new("account.create.TenantMismatch", ReasonCodes.TenantMismatch, 1,
            $"forbid(principal, {AccountCreateAction}, resource) unless {{ principal.tenant != \"\" && resource.tenant != \"\" && principal.tenant == resource.tenant }};"),

        // transaction.create: MissingScope -> RoleNotAuthorized -> SubjectNotMaker -> TenantMismatch
        new("transaction.create.MissingScope", ReasonCodes.MissingScope, 0,
            $"forbid(principal, {TransactionCreateAction}, resource) unless {{ context.scopes.contains(\"{ScopeNames.TransactionsWrite}\") }};"),
        new("transaction.create.RoleNotAuthorized", ReasonCodes.RoleNotAuthorized, 1,
            $"forbid(principal, {TransactionCreateAction}, resource) unless {{ " +
            $"principal.roles.contains(\"{RoleNames.Teller}\") || " +
            $"principal.roles.contains(\"{RoleNames.BranchManager}\") || " +
            $"principal.roles.contains(\"{RoleNames.ComplianceOfficer}\") }};"),
        new("transaction.create.SubjectNotMaker", ReasonCodes.SubjectNotMaker, 2,
            $"forbid(principal, {TransactionCreateAction}, resource) unless {{ resource.makerId != \"\" && principal.subjectId == resource.makerId }};"),
        new("transaction.create.TenantMismatch", ReasonCodes.TenantMismatch, 3,
            $"forbid(principal, {TransactionCreateAction}, resource) unless {{ principal.tenant != \"\" && resource.tenant != \"\" && principal.tenant == resource.tenant }};"),

        // approve/reject: MissingScope -> RoleNotAuthorized -> TenantMismatch -> NotPending -> MakerEqualsChecker.
        // Pending is checked BEFORE segregation of duties so a self-approval of an already-decided
        // transaction denies NotPending, not MakerEqualsChecker (mirrors the reference / Bank.Api).
        new("approval.MissingScope", ReasonCodes.MissingScope, 0,
            $"forbid(principal, {ApprovalAction}, resource) unless {{ context.scopes.contains(\"{ScopeNames.ApprovalsWrite}\") }};"),
        new("approval.RoleNotAuthorized", ReasonCodes.RoleNotAuthorized, 1,
            $"forbid(principal, {ApprovalAction}, resource) unless {{ " +
            $"principal.roles.contains(\"{RoleNames.BranchManager}\") || " +
            $"principal.roles.contains(\"{RoleNames.ComplianceOfficer}\") }};"),
        new("approval.TenantMismatch", ReasonCodes.TenantMismatch, 2,
            $"forbid(principal, {ApprovalAction}, resource) unless {{ principal.tenant != \"\" && resource.tenant != \"\" && principal.tenant == resource.tenant }};"),
        new("approval.NotPending", ReasonCodes.NotPending, 3,
            $"forbid(principal, {ApprovalAction}, resource) unless {{ resource.status == \"Pending\" }};"),
        new("approval.MakerEqualsChecker", ReasonCodes.MakerEqualsChecker, 4,
            $"forbid(principal, {ApprovalAction}, resource) when {{ resource.makerId != \"\" && principal.subjectId == resource.makerId }};"),
    ];

    // The broad per-action permits. A request is permitted iff no forbid for its action matches.
    private static readonly (string Id, string Source)[] Permits =
    [
        ("read.Permit", $"permit(principal, {ReadAction}, resource);"),
        ("account.create.Permit", $"permit(principal, {AccountCreateAction}, resource);"),
        ("transaction.create.Permit", $"permit(principal, {TransactionCreateAction}, resource);"),
        ("approval.Permit", $"permit(principal, {ApprovalAction}, resource);"),
    ];

    // id -> (reason code, precedence) for every forbid, so the adapter can map a determining forbid
    // set to the first-failing reason code (the lowest Precedence value).
    public static IReadOnlyDictionary<string, ForbidReason> ForbidReasons { get; } =
        Forbids.ToDictionary(f => f.Id, f => new ForbidReason(f.ReasonCode, f.Precedence), StringComparer.Ordinal);

    // Builds the PolicySet from explicit Policy(source, id) objects so the native engine echoes our
    // stable ids in the authorization-response reason set (parsed once by the provider, never per call).
    public static PolicySet Build()
    {
        var policies = new HashSet<Policy>();
        foreach (var forbid in Forbids)
        {
            policies.Add(new Policy(forbid.Source, forbid.Id));
        }

        foreach (var (id, source) in Permits)
        {
            policies.Add(new Policy(source, id));
        }

        return new PolicySet(policies);
    }

    private sealed record ForbidPolicy(string Id, string ReasonCode, int Precedence, string Source);
}

// The reason code a determining forbid maps to, plus its precedence in the reference's per-action
// order (lower = first-failing, so it wins when several forbids are determining for one input).
internal sealed record ForbidReason(string ReasonCode, int Precedence);
