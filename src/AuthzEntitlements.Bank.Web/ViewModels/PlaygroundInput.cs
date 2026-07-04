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
                NullIfBlank(Branch)),
            new PdpActionDto(Action?.Trim() ?? string.Empty),
            new PdpResourceDto(
                ResourceType?.Trim() ?? string.Empty,
                NullIfBlank(ResourceId),
                NullIfBlank(ResourceTenant) ?? NullIfBlank(Tenant),
                NullIfBlank(Branch),
                Amount,
                NullIfBlank(MakerId),
                NullIfBlank(Status)),
            new PdpContextDto(SplitCsv(Scopes)));

    private static IReadOnlyList<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
