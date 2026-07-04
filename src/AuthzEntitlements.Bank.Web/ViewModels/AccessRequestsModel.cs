using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the JIT ACCESS-REQUEST (governance) page. Kept out of
// the .razor so the pending filter, segregation-of-duties labelling, request-body builder,
// and outcome labelling are unit-testable offline (no server, Docker, or Keycloak). NONE
// of this is the security boundary: the Governance.Service independently enforces SoD, the
// checker-eligibility rule, and decide-once — this UI only surfaces the fail-closed
// outcomes the server returns.
public static class AccessRequestsModel
{
    private const string PendingStatus = "Pending";

    // The requests still awaiting a decision (Status == "Pending"). These are the only
    // rows a checker can approve or reject.
    public static IReadOnlyList<AccessRequestResponse> Pending(
        IEnumerable<AccessRequestResponse> reqs) =>
        reqs.Where(r => string.Equals(r.Status, PendingStatus, StringComparison.Ordinal)).ToList();

    // True when a request is still pending (drives the "highlight pending" UI).
    public static bool IsPending(AccessRequestResponse req) =>
        string.Equals(req.Status, PendingStatus, StringComparison.Ordinal);

    // Human-readable label for a server-reported SoD outcome. Mirrors the
    // Governance.Service SodOutcome enum wire values (Permit/Deny/Unavailable/NotEvaluated);
    // "Allowed"/"Denied" are accepted as synonyms so the label is robust to wording. An
    // unrecognised value falls back to the raw string rather than guessing.
    public static string SodOutcomeLabel(string sodOutcome) => sodOutcome switch
    {
        "Permit" or "Allowed" => "Allowed (segregation-of-duties check passed)",
        "Deny" or "Denied" => "Denied (segregation-of-duties conflict)",
        "Unavailable" => "Unavailable (PDP unreachable — fail-closed, request stays Pending)",
        "NotEvaluated" => "Not yet evaluated (pending a decision)",
        _ => string.IsNullOrWhiteSpace(sodOutcome) ? "—" : sodOutcome,
    };

    // Builds the create-request body from the form fields and the resolved principal id.
    // The principal is bound from the signed-in identity, never a form field — a caller may
    // not request access as another subject. Justification/code are trimmed; a whitespace
    // duration collapses to null so the package default applies.
    public static CreateAccessRequestBody BuildCreateBody(
        string principalId, RequestAccessInput input) =>
        new(
            principalId,
            (input.AccessPackageCode ?? string.Empty).Trim(),
            (input.Justification ?? string.Empty).Trim(),
            input.RequestedDurationMinutes is > 0 ? input.RequestedDurationMinutes : null);

    // Human-readable label for a create/approve/reject outcome status code, surfacing the
    // fail-closed / SoD / decide-once semantics the server enforces.
    public static string RequestOutcomeLabel(int statusCode) => statusCode switch
    {
        200 => "OK (decision recorded)",
        201 => "Created (request submitted)",
        400 => "400 Bad Request (missing justification or invalid input)",
        403 => "403 Forbidden (segregation of duties, or ineligible approver)",
        404 => "404 Not Found (unknown principal, package, or request)",
        409 => "409 Conflict (segregation of duties, or already decided)",
        503 => "503 Service Unavailable (PDP unreachable — fail-closed)",
        _ => $"{statusCode}",
    };
}

// Form model for the "request access" action. The PrincipalId is NEVER a form field — it
// is bound at submit time from the signed-in identity (fail-closed authz).
public sealed class RequestAccessInput
{
    public string? AccessPackageCode { get; set; }

    public string? Justification { get; set; }

    public int? RequestedDurationMinutes { get; set; }
}

// Form model for a checker's approve/reject decision. The ApproverId is NEVER a form field
// — it is bound at submit time from the signed-in identity so an approver cannot decide as
// another subject (the server enforces SoD independently).
public sealed class DecideRequestInput
{
    public Guid RequestId { get; set; }

    public string? Reason { get; set; }
}
