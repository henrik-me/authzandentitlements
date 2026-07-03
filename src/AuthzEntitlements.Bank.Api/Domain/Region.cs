namespace AuthzEntitlements.Bank.Api.Domain;

// A geographic grouping of branches within a tenant. Carried on the model now so
// later ABAC policies can scope access by region without a schema change.
public sealed class Region
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<Branch> Branches { get; } = [];
}
