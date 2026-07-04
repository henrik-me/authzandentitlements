using AuthzEntitlements.Governance.Service.Domain;

namespace AuthzEntitlements.Governance.Service.Tests;

// Small builders for the pure-domain tests: they assemble the same entity object graphs the
// seeder and endpoints wire, so decision logic can be exercised without a database.
internal static class GovernanceTestData
{
    public const string Contoso = "CONTOSO";

    // A fixed instant so expiry arithmetic in the tests is deterministic.
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static Principal Principal(string id, string tenant, params string[] baselineRoles)
    {
        var principal = new Principal { Id = id, TenantCode = tenant, DisplayName = id };
        foreach (var role in baselineRoles)
        {
            principal.BaselineRoles.Add(new PrincipalRole
            {
                Id = Guid.NewGuid(),
                PrincipalId = id,
                RoleName = role,
            });
        }

        return principal;
    }

    public static AccessPackage Package(string code, int defaultMinutes, params string[] roles)
    {
        var package = new AccessPackage
        {
            Id = Guid.NewGuid(),
            Code = code,
            DisplayName = code,
            Description = code,
            DefaultDurationMinutes = defaultMinutes,
            RequiresApproval = true,
        };
        foreach (var role in roles)
        {
            package.Roles.Add(new AccessPackageRole
            {
                Id = Guid.NewGuid(),
                AccessPackageId = package.Id,
                RoleName = role,
            });
        }

        return package;
    }

    public static AccessGrantRequest Request(
        string principalId, string tenant, string packageCode, int? requestedMinutes = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            PrincipalId = principalId,
            TenantCode = tenant,
            AccessPackageCode = packageCode,
            Justification = "quarter-end close support",
            RequestedDurationMinutes = requestedMinutes,
            Status = RequestStatus.Pending,
            SodOutcome = SodOutcome.NotEvaluated,
            RequestedAt = Now,
        };

    public static AccessGrant Grant(
        string principalId,
        string tenant,
        string packageCode,
        DateTimeOffset grantedAt,
        DateTimeOffset expiresAt,
        string[] roles,
        DateTimeOffset? revokedAt = null)
    {
        var grant = new AccessGrant
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid(),
            PrincipalId = principalId,
            TenantCode = tenant,
            AccessPackageCode = packageCode,
            GrantedAt = grantedAt,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
        };
        foreach (var role in roles)
        {
            grant.Roles.Add(new AccessGrantRole
            {
                Id = Guid.NewGuid(),
                AccessGrantId = grant.Id,
                RoleName = role,
            });
        }

        return grant;
    }
}
