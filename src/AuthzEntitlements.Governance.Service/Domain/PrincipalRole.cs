namespace AuthzEntitlements.Governance.Service.Domain;

// One standing (baseline) role a principal holds. Child of Principal (cascade-deleted
// with it).
public sealed class PrincipalRole
{
    public Guid Id { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}
