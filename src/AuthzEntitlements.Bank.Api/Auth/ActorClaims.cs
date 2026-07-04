using System.Security.Claims;

namespace AuthzEntitlements.Bank.Api.Auth;

// Reads the actor / on-behalf-of (OBO) claim contract that marks a non-human caller and, for a
// delegated call, the human it acts for. This is the OBO seam CS21 (break-glass / time-boxed
// delegation) reuses: a break-glass grant is an OBO delegation with an elevated, expiring scope
// set, and it binds to exactly the same claims read here.
//
// Binding is to the TOKEN (LRN-011) using the LITERAL Keycloak claim names — MapInboundClaims=false
// is set, so there is no ClaimTypes.* URI remapping (LRN-010). Every reader is FAIL-CLOSED: a
// missing or blank claim is never fabricated into a value or defaulted to a delegation. The
// human default (absent subject_type => "user") is the only default, and it is the safe one.
public static class ActorClaims
{
    // Marks the caller kind: "user" (human), "agent", or "service". ABSENT => treat as "user".
    public const string SubjectTypeClaim = "subject_type";

    // For an OBO call, the effective user id the agent acts for. Blank => NOT a delegation.
    public const string OnBehalfOfClaim = "on_behalf_of";

    // The token subject. For an agent OBO token this is the AGENT's own id.
    public const string SubjectClaim = "sub";

    // The recognized delegate kinds — the delegate `Actor.Type` domain ("agent" | "service")
    // enforced by the PDP. These are the ONLY subject_type values that may resolve a delegation.
    public const string AgentType = "agent";
    public const string ServiceType = "service";

    private const string UserType = "user";

    // The allow-list of subject_type values that may act as a delegate in an OBO call. ORDINAL:
    // the claim is a fixed lowercase machine token, so "AGENT"/"Service" must NOT match — an
    // unknown/typo/mis-cased subject_type can never silently grant delegation (fail-closed).
    private static readonly HashSet<string> DelegationCapableTypes =
        new(StringComparer.Ordinal) { AgentType, ServiceType };

    // Returns the caller's subject_type claim value, or "user" when the claim is absent/blank —
    // the human default. Read fail-closed everywhere else via IsNonHuman.
    public static string GetSubjectType(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(SubjectTypeClaim);
        return string.IsNullOrWhiteSpace(value) ? UserType : value;
    }

    // Returns true only when subject_type is present and is NOT "user" (ordinal-ignore-case) —
    // i.e. an agent or service. A missing/blank claim is a human, so this is false.
    public static bool IsNonHuman(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(SubjectTypeClaim);
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, UserType, StringComparison.OrdinalIgnoreCase);
    }

    // Returns the on_behalf_of user id, or null when the claim is absent/blank. Fail-closed: a
    // blank OBO claim is NOT a delegation, so callers can translate null into "no delegation".
    public static string? GetOnBehalfOf(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(OnBehalfOfClaim);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Resolves a constrained-delegation (OBO) call. Returns true ONLY when subject_type is a
    // RECOGNIZED delegate kind ("agent" or "service", matching the PDP Actor.Type domain, ORDINAL —
    // stricter than IsNonHuman, so an unknown/typo/mis-cased subject_type can never silently grant
    // delegation, fail-closed) AND the token carries a non-blank on_behalf_of; then actorId is the
    // token sub (the acting agent) and onBehalfOfUserId is the on_behalf_of value (the effective
    // user). Fail-closed: false for a human token, false for an unrecognized subject_type, and
    // false for a delegate acting AS ITSELF (no on_behalf_of). A whitespace on_behalf_of is absent.
    public static bool TryGetDelegation(
        this ClaimsPrincipal principal, out string actorId, out string onBehalfOfUserId)
    {
        actorId = string.Empty;
        onBehalfOfUserId = string.Empty;

        var subjectType = principal.FindFirstValue(SubjectTypeClaim);
        if (string.IsNullOrWhiteSpace(subjectType) || !DelegationCapableTypes.Contains(subjectType))
        {
            return false;
        }

        var onBehalfOf = principal.GetOnBehalfOf();
        if (onBehalfOf is null)
        {
            return false;
        }

        var sub = principal.FindFirstValue(SubjectClaim);
        if (string.IsNullOrWhiteSpace(sub))
        {
            return false;
        }

        actorId = sub;
        onBehalfOfUserId = onBehalfOf;
        return true;
    }
}
