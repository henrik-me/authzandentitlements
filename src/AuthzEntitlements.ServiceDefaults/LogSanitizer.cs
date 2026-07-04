namespace AuthzEntitlements.ServiceDefaults;

// Shared CWE-117 (log injection / log forging) barrier. Strips CR/LF from a caller- or
// engine-derived string before it is rendered into an ILogger line, so no untrusted value can
// smuggle a newline and forge a second, fake log entry. Every audit/log call site that renders a
// request- or engine-derived string routes it through Clean; only the human-readable log is
// sanitized — the audit-of-record keeps the raw value. Null/empty-safe: a null passes through as
// null so an absent optional field is never fabricated into an empty string.
public static class LogSanitizer
{
    public static string? Clean(string? value) =>
        value?.Replace('\r', ' ').Replace('\n', ' ');
}
