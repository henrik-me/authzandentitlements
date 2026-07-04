namespace AuthzEntitlements.Bank.Web.Clients;

// Outcome envelope for write calls (and any read that wants an explicit failure). The
// typed clients build this from the HttpResponseMessage WITHOUT throwing on non-2xx, so
// the UI can render coarse (401/403 at the gateway), fine (403 at Bank.Api), decide-once
// (409), entitlement (400/422), and transient (503) denials as visible outcomes rather
// than surfacing an unhandled exception. Fail-closed: a Failure is never mistaken for a
// success because IsSuccess is only ever set by the Success factory.
public sealed record ApiResult<T>(
    bool IsSuccess,
    int StatusCode,
    T? Value,
    string? Error)
{
    public static ApiResult<T> Success(T value, int statusCode) =>
        new(true, statusCode, value, null);

    public static ApiResult<T> Failure(int statusCode, string error) =>
        new(false, statusCode, default, error);
}
