namespace AuthzEntitlements.Governance.Service.Domain;

// Pure factory for the AccessGrant an approved request issues. Snapshots the package's
// roles and computes the expiry from the effective JIT duration, so grant creation is
// deterministic and unit-testable without a database. The endpoint persists the returned
// graph; it adds no other state.
public static class AccessGrantFactory
{
    public static AccessGrant Create(
        AccessGrantRequest request,
        AccessPackage package,
        DateTimeOffset grantedAt)
    {
        var duration = GovernanceRules.EffectiveDurationMinutes(
            request.RequestedDurationMinutes, package.DefaultDurationMinutes);

        var grant = new AccessGrant
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            PrincipalId = request.PrincipalId,
            TenantCode = request.TenantCode,
            AccessPackageCode = package.Code,
            GrantedAt = grantedAt,
            ExpiresAt = GovernanceRules.ComputeExpiry(grantedAt, duration),
        };

        foreach (var role in package.Roles
                     .Select(r => r.RoleName)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
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
