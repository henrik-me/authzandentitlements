namespace AuthzEntitlements.Bank.Api.Domain;

// The RBAC assignment join: which user holds which role. Composite PK
// (UserId, RoleId) makes a duplicate assignment impossible.
public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
