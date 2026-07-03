namespace AuthzEntitlements.Bank.Api.Domain;

// An RBAC role. Global (not tenant-scoped) in CS02: the four fintech back-office
// roles are shared vocabulary across tenants. Assignments live on UserRole.
public sealed class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<UserRole> UserRoles { get; } = [];
}
