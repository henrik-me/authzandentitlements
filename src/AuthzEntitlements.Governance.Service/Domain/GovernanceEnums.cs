namespace AuthzEntitlements.Governance.Service.Domain;

// Lifecycle status of an access-grant request. Persisted as a string via
// HasConversion<string>() so the database stores stable, human-readable values.
public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Revoked,
}

// Result of the segregation-of-duties (SoD) evaluation performed against the PDP when a
// request is approved. NotEvaluated is the pre-approval default; Unavailable records a
// fail-closed outcome (the PDP could not be reached) that leaves the request Pending.
public enum SodOutcome
{
    NotEvaluated,
    Permit,
    Deny,
    Unavailable,
}

// Lifecycle status of an access-review (recertification) campaign.
public enum CampaignStatus
{
    Open,
    Completed,
}

// Per-grant recertification decision. Pending until a reviewer certifies (keep) or
// revokes (remove) the underlying grant.
public enum ReviewDecision
{
    Pending,
    Certify,
    Revoke,
}
