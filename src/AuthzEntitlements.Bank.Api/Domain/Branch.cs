namespace AuthzEntitlements.Bank.Api.Domain;

// A physical branch. The branch attribute is a first-class ABAC dimension in later
// clickstops (e.g. "a teller may only act within their home branch").
public sealed class Branch
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RegionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public Tenant Tenant { get; set; } = null!;
    public Region Region { get; set; } = null!;
}
