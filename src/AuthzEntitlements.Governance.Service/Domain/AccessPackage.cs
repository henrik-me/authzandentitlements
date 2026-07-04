namespace AuthzEntitlements.Governance.Service.Domain;

// A named bundle that grants a set of roles for a bounded time (the Entra "access
// package" pattern). A principal requests a package; approval issues a time-bound grant
// of the package's roles. Code is the stable, unique lookup key (e.g. "quarter-end-close").
public sealed class AccessPackage
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Default lifetime of a grant issued from this package, in minutes. A request may
    // override it with a shorter/longer RequestedDurationMinutes.
    public int DefaultDurationMinutes { get; set; }

    // Whether obtaining this package requires an approval step. Approval also runs the
    // SoD check; a package is never auto-granted when this is true.
    public bool RequiresApproval { get; set; } = true;

    public ICollection<AccessPackageRole> Roles { get; } = [];
}
