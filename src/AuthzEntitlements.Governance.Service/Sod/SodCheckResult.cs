namespace AuthzEntitlements.Governance.Service.Sod;

// The outcome of a segregation-of-duties check performed against the PDP. It is a local
// value the client constructs from the PDP's decision — it is never deserialized from the
// wire — so the fail-closed Unavailable state cannot be injected by a payload; only the
// Unavailable factory produces it.
public enum SodStatus
{
    Permit,
    Deny,
    Unavailable,
}

public sealed record SodCheckResult
{
    // Stable reason code recorded when the PDP could not be consulted. Distinct from any
    // business deny code the PDP returns (e.g. "SodConflict").
    public const string UnavailableCode = "SodUnavailable";

    private SodCheckResult(SodStatus status, string? reasonCode, string? reasonMessage)
    {
        Status = status;
        ReasonCode = reasonCode;
        ReasonMessage = reasonMessage;
    }

    public SodStatus Status { get; }
    public string? ReasonCode { get; }
    public string? ReasonMessage { get; }

    public bool IsPermit => Status == SodStatus.Permit;
    public bool IsDeny => Status == SodStatus.Deny;
    public bool IsUnavailable => Status == SodStatus.Unavailable;

    // The PDP permitted the proposed role set — no SoD conflict.
    public static readonly SodCheckResult Permit = new(SodStatus.Permit, null, null);

    // The PDP denied the proposed role set: a genuine SoD business decision, carrying the
    // PDP's primary reason code (e.g. "SodConflict") and message.
    public static SodCheckResult Deny(string reasonCode, string reasonMessage) =>
        new(SodStatus.Deny, reasonCode, reasonMessage);

    // The PDP could not be consulted (transport error, timeout, non-success status, or a
    // missing/malformed body). Fail closed: this must never be treated as a permit.
    public static SodCheckResult Unavailable(string reason) =>
        new(SodStatus.Unavailable, UnavailableCode, reason);
}
