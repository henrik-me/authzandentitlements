namespace AuthzEntitlements.Governance.Service.Domain;

// One role captured in an issued grant's role snapshot. Child of AccessGrant
// (cascade-deleted with it).
public sealed class AccessGrantRole
{
    public Guid Id { get; set; }
    public Guid AccessGrantId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}
