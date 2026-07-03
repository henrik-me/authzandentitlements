using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Auth;

// Resolves the caller's tenant claim (a Code such as CONTOSO or FABRIKAM) to the
// tenant's primary key so read paths can be tenant-scoped and write paths can be
// checked against the token's tenant. Fails closed: an absent or unknown tenant
// claim resolves to null, which callers translate into 403 semantics.
public static class TenantScope
{
    public static async Task<Guid?> ResolveCallerTenantIdAsync(
        this ClaimsPrincipal user, BankDbContext db, CancellationToken ct)
    {
        var code = user.GetTenant();
        if (code is null)
        {
            return null;
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == code, ct);
        return tenant?.Id;
    }
}
