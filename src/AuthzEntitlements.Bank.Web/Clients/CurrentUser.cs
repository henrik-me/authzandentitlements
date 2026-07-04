using System.Security.Claims;

namespace AuthzEntitlements.Bank.Web.Clients;

// Scoped view over the signed-in user's identity, plus the mapping from the OIDC
// identity to the Bank.Api domain user GUID (needed for Maker/Checker ids on write
// flows). Reads claims from the current HttpContext so the values are available during
// static-SSR rendering, where the token-forwarding bank client also runs.
public interface ICurrentUser
{
    string? Username { get; }

    string? TenantCode { get; }

    IReadOnlyList<string> Roles { get; }

    // Governance principal ids are "user-{username}" (mirrors the governance seed).
    string? GovernancePrincipalId { get; }

    bool IsInRole(string role);

    // Maps the OIDC preferred_username to the Bank.Api User.Id (case-insensitive), or
    // null when there is no matching domain user. Cached within the request scope.
    Task<Guid?> ResolveBankUserIdAsync(CancellationToken ct = default);
}

public sealed class CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    IBankApiClient bankApi) : ICurrentUser
{
    // The Keycloak claim names (MapInboundClaims=false keeps the literal claim types).
    private const string PreferredUsernameClaim = "preferred_username";
    private const string TenantClaim = "tenant";
    private const string RolesClaim = "roles";

    private bool _resolved;
    private Guid? _bankUserId;

    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public string? Username
    {
        get
        {
            var value = Principal?.FindFirst(PreferredUsernameClaim)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public string? TenantCode
    {
        get
        {
            var value = Principal?.FindFirst(TenantClaim)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(RolesClaim).Select(c => c.Value).ToList() ?? [];

    public string? GovernancePrincipalId =>
        Username is null ? null : $"user-{Username.ToLowerInvariant()}";

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.Ordinal);

    public async Task<Guid?> ResolveBankUserIdAsync(CancellationToken ct = default)
    {
        if (_resolved)
        {
            return _bankUserId;
        }

        var username = Username;
        if (username is not null)
        {
            var users = await bankApi.GetUsersAsync(ct);
            var match = users.FirstOrDefault(
                u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            _bankUserId = match?.Id;
        }

        _resolved = true;
        return _bankUserId;
    }
}
