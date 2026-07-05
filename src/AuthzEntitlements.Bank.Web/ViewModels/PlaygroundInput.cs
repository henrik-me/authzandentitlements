using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Form model for the AuthZ Playground page. Kept pure and dependency-free so the field-to-request
// mapping (CSV role/scope splitting, blank -> null normalization) is unit-testable offline. The
// PDP fan-out is anonymous, so — unlike the maker-checker pages — the subject is authored freely
// here rather than bound from a token identity: the playground is a what-if surface, not an
// enforcement path.
public sealed class PlaygroundInput
{
    public string SubjectType { get; set; } = "user";

    public string SubjectId { get; set; } = string.Empty;

    // Comma-separated role list (e.g. "Teller, BranchManager"). Split, trimmed, blanks dropped.
    public string Roles { get; set; } = string.Empty;

    // Optional on-behalf-of (OBO) delegate acting FOR the subject: type ("agent"|"service"), id, and
    // the delegated capability scopes (comma-separated). All blank ⇒ a direct/human call (Actor null),
    // so the common non-OBO case leaves these empty. Reconstructed 1:1 from a replayed snapshot so an
    // OBO decision replays as the SAME request rather than a direct one.
    public string? ActorType { get; set; }

    public string? ActorId { get; set; }

    // Comma-separated delegated scope list for the OBO actor. Split, trimmed, blanks dropped.
    public string ActorScopes { get; set; } = string.Empty;

    // Subject tenant/branch. When the resource-specific fields below are blank, the resource inherits
    // these, so the common single-tenant case needs only these two.
    public string? Tenant { get; set; }

    public string? Branch { get; set; }

    public string Action { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string? ResourceId { get; set; }

    // Resource tenant. Set it different from the subject Tenant to model a cross-tenant request (the
    // reference engine denies with TenantMismatch); blank ⇒ falls back to the subject Tenant.
    public string? ResourceTenant { get; set; }

    // Resource branch, INDEPENDENT of the subject Branch. Set it different from the subject Branch to
    // model a cross-branch request; blank ⇒ falls back to the subject Branch (so the common
    // single-branch case, and every non-replay form, stays byte-identical to authoring one Branch).
    public string? ResourceBranch { get; set; }

    public decimal? Amount { get; set; }

    public string? MakerId { get; set; }

    public string? Status { get; set; }

    // Comma-separated context scope list. Split, trimmed, blanks dropped.
    public string Scopes { get; set; } = string.Empty;

    // Maps the flat form into the native AuthZEN AccessRequest DTO the PDP expects. CSV role/scope
    // fields become string arrays (trimmed, blanks dropped); blank optional fields become null so
    // the wire shape matches a hand-authored request rather than carrying empty-string noise.
    public PdpAccessRequestDto ToRequestDto() =>
        new(
            new PdpSubjectDto(
                string.IsNullOrWhiteSpace(SubjectType) ? "user" : SubjectType.Trim(),
                SubjectId?.Trim() ?? string.Empty,
                SplitCsv(Roles),
                NullIfBlank(Tenant),
                NullIfBlank(Branch),
                BuildActor()),
            new PdpActionDto(Action?.Trim() ?? string.Empty),
            new PdpResourceDto(
                ResourceType?.Trim() ?? string.Empty,
                NullIfBlank(ResourceId),
                NullIfBlank(ResourceTenant) ?? NullIfBlank(Tenant),
                NullIfBlank(ResourceBranch) ?? NullIfBlank(Branch),
                Amount,
                NullIfBlank(MakerId),
                NullIfBlank(Status)),
            new PdpContextDto(SplitCsv(Scopes)));

    // Builds the OBO actor from the form, or null unless a COMPLETE delegate (BOTH type and id) is
    // authored — a partial actor (only one field) is invalid at the PDP boundary (subject.actor.type
    // and .id are both required when the actor is present), so it degrades to a direct/human call
    // (null Actor), byte-identical to omitting it. When a complete delegate IS present, its
    // type/id/scopes reconstruct the recorded Actor exactly.
    private PdpActorDto? BuildActor()
    {
        var type = NullIfBlank(ActorType);
        var id = NullIfBlank(ActorId);
        if (type is null || id is null)
        {
            return null;
        }

        return new PdpActorDto(type, id, SplitCsv(ActorScopes));
    }

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
