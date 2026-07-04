namespace AuthzEntitlements.Governance.Service.Domain;

// A person who can request access. Id mirrors the Bank user ids (e.g. "user-teller1") so
// governance decisions line up with the CS02/CS03 seed. BaselineRoles are the principal's
// standing roles — the SoD check evaluates these UNION a requested package's roles.
public sealed class Principal
{
    public string Id { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public ICollection<PrincipalRole> BaselineRoles { get; } = [];
}
