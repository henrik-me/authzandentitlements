namespace AuthzEntitlements.Governance.Service.Sod;

// Typed client for the PDP's segregation-of-duties check. Registered as an Aspire
// service-discovered, resilience-wrapped HttpClient. Every call fails closed: a transport
// fault, timeout, non-success status, or missing/malformed body yields SodCheckResult
// .Unavailable rather than throwing — so the approval workflow denies (503) instead of
// granting on an unknown SoD state.
public interface IPdpSodClient
{
    // Evaluates whether granting the proposed role set to the principal is permitted by
    // SoD policy. proposedRoles is the principal's baseline roles UNION the requested
    // access package's roles (deduplicated).
    Task<SodCheckResult> EvaluateAsync(
        string principalId,
        string tenantCode,
        IReadOnlyCollection<string> proposedRoles,
        string accessPackageCode,
        CancellationToken ct);
}
