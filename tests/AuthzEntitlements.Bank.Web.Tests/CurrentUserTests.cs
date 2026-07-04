using System.Security.Claims;
using AuthzEntitlements.Bank.Web.Clients;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class CurrentUserTests
{
    private static readonly Guid Teller1Id = new("40000000-0000-0000-0000-000000000001");

    [Fact]
    public void Reads_username_tenant_and_roles_from_claims()
    {
        var current = new CurrentUser(Accessor("teller1", "CONTOSO", "Teller", "BranchManager"), new FakeBankApi());

        Assert.Equal("teller1", current.Username);
        Assert.Equal("CONTOSO", current.TenantCode);
        Assert.Equal(new[] { "Teller", "BranchManager" }, current.Roles);
        Assert.True(current.IsInRole("Teller"));
        Assert.False(current.IsInRole("Auditor"));
    }

    [Fact]
    public void GovernancePrincipalId_prefixes_username()
    {
        var current = new CurrentUser(Accessor("teller1", "CONTOSO", "Teller"), new FakeBankApi());

        Assert.Equal("user-teller1", current.GovernancePrincipalId);
    }

    [Fact]
    public void GovernancePrincipalId_is_null_without_username()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) },
        };
        var current = new CurrentUser(accessor, new FakeBankApi());

        Assert.Null(current.Username);
        Assert.Null(current.GovernancePrincipalId);
    }

    [Fact]
    public async Task ResolveBankUserIdAsync_maps_username_to_guid_case_insensitively()
    {
        var api = new FakeBankApi(new UserDto(
            Teller1Id, Guid.NewGuid(), Guid.NewGuid(), "Teller1", "t@x", "Tara", ["Teller"]));
        var current = new CurrentUser(Accessor("teller1", "CONTOSO", "Teller"), api);

        var id = await current.ResolveBankUserIdAsync();

        Assert.Equal(Teller1Id, id);
    }

    [Fact]
    public async Task ResolveBankUserIdAsync_returns_null_when_unmatched()
    {
        var api = new FakeBankApi(new UserDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "someone-else", "e@x", "Else", []));
        var current = new CurrentUser(Accessor("teller1", "CONTOSO", "Teller"), api);

        var id = await current.ResolveBankUserIdAsync();

        Assert.Null(id);
    }

    [Fact]
    public async Task ResolveBankUserIdAsync_caches_within_scope()
    {
        var api = new FakeBankApi(new UserDto(
            Teller1Id, Guid.NewGuid(), Guid.NewGuid(), "teller1", "t@x", "Tara", ["Teller"]));
        var current = new CurrentUser(Accessor("teller1", "CONTOSO", "Teller"), api);

        _ = await current.ResolveBankUserIdAsync();
        _ = await current.ResolveBankUserIdAsync();

        Assert.Equal(1, api.GetUsersCallCount);
    }

    private static IHttpContextAccessor Accessor(string username, string tenant, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("preferred_username", username),
            new("tenant", tenant),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        var identity = new ClaimsIdentity(claims, "test", "preferred_username", "roles");
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private sealed class FakeBankApi(params UserDto[] users) : IBankApiClient
    {
        public int GetUsersCallCount { get; private set; }

        public Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default)
        {
            GetUsersCallCount++;
            return Task.FromResult<IReadOnlyList<UserDto>>(users);
        }

        public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TransactionDto?> GetTransactionAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ApiResult<TransactionDto>> CreateTransactionAsync(
            CreateTransactionRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ApiResult<TransactionDto>> ApproveTransactionAsync(
            Guid id, DecideRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ApiResult<TransactionDto>> RejectTransactionAsync(
            Guid id, DecideRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
