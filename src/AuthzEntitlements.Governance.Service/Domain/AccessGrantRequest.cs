namespace AuthzEntitlements.Governance.Service.Domain;

// A request to obtain an access package (just-in-time elevation). Created Pending; an
// approval runs maker-checker + SoD and, on permit, issues a time-bound AccessGrant.
// The xmin shadow rowversion makes approve/reject decide-once: a concurrent second
// decision on the same request loses with a DbUpdateConcurrencyException (surfaced as a
// 409) instead of last-writer-wins.
public sealed class AccessGrantRequest
{
    public Guid Id { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string AccessPackageCode { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;

    // The JIT time-bound duration the requester asked for. Null means "use the package
    // default" (AccessPackage.DefaultDurationMinutes).
    public int? RequestedDurationMinutes { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public SodOutcome SodOutcome { get; set; } = SodOutcome.NotEvaluated;
    public string? SodReason { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}
