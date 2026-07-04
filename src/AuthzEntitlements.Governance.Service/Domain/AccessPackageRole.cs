namespace AuthzEntitlements.Governance.Service.Domain;

// One role an access package grants. Child of AccessPackage (cascade-deleted with it).
public sealed class AccessPackageRole
{
    public Guid Id { get; set; }
    public Guid AccessPackageId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}
