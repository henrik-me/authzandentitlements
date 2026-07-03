namespace AuthzEntitlements.Bank.Api.Domain;

// A back-office staff member. Belongs to a tenant and a home branch; both are
// subject attributes later authz layers evaluate. Username is unique per tenant.
public sealed class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public ICollection<UserRole> UserRoles { get; } = [];

    public bool IsInRole(string roleName) =>
        UserRoles.Any(ur => ur.Role is not null &&
                            string.Equals(ur.Role.Name, roleName, StringComparison.Ordinal));
}
