namespace AuthzEntitlements.Bank.Api.Domain;

// A bank. The multi-tenant root: every other record hangs off a tenant so later
// tenant-isolation authz scenarios have a boundary to enforce.
public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public ICollection<Region> Regions { get; } = [];
    public ICollection<Branch> Branches { get; } = [];
    public ICollection<User> Users { get; } = [];
}
