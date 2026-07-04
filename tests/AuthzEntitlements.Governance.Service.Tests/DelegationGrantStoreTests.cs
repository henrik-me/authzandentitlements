using AuthzEntitlements.Governance.Service.Delegation;
using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Read-time expiry (IsActive) plus explicit revocation are how the delegation store enforces a
// bounded manager->delegate grant without a background sweeper. A fixed clock
// (GovernanceTestData.Now) is injected as "now" so the window arithmetic is deterministic, and
// every mutation is proven fail-closed.
public sealed class DelegationGrantStoreTests
{
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;
    private const string Manager = "user-manager1";
    private const string Delegate = "user-teller1";
    private const string Tenant = GovernanceTestData.Contoso;

    private static readonly string[] Scopes = ["agent.bank.transaction.read", "agent.bank.transaction.create"];

    private static DelegationGrantStore NewStore() => new();

    private static DelegationGrant Create(DelegationGrantStore store, int durationMinutes = 60) =>
        store.Create(Manager, Delegate, Tenant, Scopes, durationMinutes, Now);

    [Fact]
    public void Create_SetsWindowAndFields()
    {
        var grant = Create(NewStore(), durationMinutes: 60);

        Assert.NotEqual(Guid.Empty, grant.Id);
        Assert.Equal(Manager, grant.ManagerId);
        Assert.Equal(Delegate, grant.DelegateId);
        Assert.Equal(Tenant, grant.TenantCode);
        Assert.Equal(Scopes, grant.Scopes);
        Assert.Equal(Now, grant.GrantedAt);
        Assert.Equal(Now.AddMinutes(60), grant.ExpiresAt);
        Assert.Null(grant.RevokedAt);
        Assert.Null(grant.RevokedBy);
    }

    [Fact]
    public void Create_ActiveWithinWindow_BoundaryExclusive()
    {
        var grant = Create(NewStore(), durationMinutes: 60);

        Assert.True(grant.IsActive(Now));
        Assert.True(grant.IsActive(grant.ExpiresAt.AddMinutes(-1)));
        Assert.False(grant.IsActive(grant.ExpiresAt));
        Assert.False(grant.IsActive(grant.ExpiresAt.AddMinutes(1)));
    }

    [Fact]
    public void Create_NormalizesScopes_TrimDedupeDropBlank()
    {
        var store = NewStore();

        var grant = store.Create(
            Manager, Delegate, Tenant,
            ["  agent.bank.a  ", "agent.bank.a", "", "   ", "agent.bank.b"],
            60, Now);

        Assert.Equal(["agent.bank.a", "agent.bank.b"], grant.Scopes);
    }

    [Theory]
    [InlineData("", Delegate, Tenant)]
    [InlineData("   ", Delegate, Tenant)]
    [InlineData(Manager, "", Tenant)]
    [InlineData(Manager, "   ", Tenant)]
    [InlineData(Manager, Delegate, "")]
    [InlineData(Manager, Delegate, "   ")]
    public void Create_FailsClosed_OnBlankInput(string manager, string @delegate, string tenant)
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.Create(manager, @delegate, tenant, Scopes, 60, Now));
    }

    [Fact]
    public void Create_FailsClosed_OnSelfDelegation()
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.Create(Manager, Manager, Tenant, Scopes, 60, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Create_FailsClosed_OnNonPositiveDuration(int minutes)
    {
        var store = NewStore();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.Create(Manager, Delegate, Tenant, Scopes, minutes, Now));
    }

    [Fact]
    public void Create_FailsClosed_OnNullScopes()
    {
        var store = NewStore();

        Assert.Throws<ArgumentNullException>(
            () => store.Create(Manager, Delegate, Tenant, null!, 60, Now));
    }

    [Fact]
    public void Create_FailsClosed_OnEmptyScopes()
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(
            () => store.Create(Manager, Delegate, Tenant, [], 60, Now));
    }

    [Fact]
    public void Create_FailsClosed_OnAllBlankScopes()
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(
            () => store.Create(Manager, Delegate, Tenant, ["", "   "], 60, Now));
    }

    [Fact]
    public void Revoke_MakesInactiveEvenBeforeExpiry()
    {
        var store = NewStore();
        var grant = Create(store, durationMinutes: 60);
        var revokedAt = Now.AddMinutes(10);

        var revoked = store.Revoke(grant.Id, "user-manager1", revokedAt);

        Assert.Equal(revokedAt, revoked.RevokedAt);
        Assert.Equal("user-manager1", revoked.RevokedBy);
        Assert.False(revoked.IsActive(Now.AddMinutes(20)));
    }

    [Fact]
    public void Revoke_FailsClosed_OnUnknownId()
    {
        var store = NewStore();

        Assert.Throws<KeyNotFoundException>(() => store.Revoke(Guid.NewGuid(), "user-manager1", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Revoke_FailsClosed_OnBlankRevoker(string revokedBy)
    {
        var store = NewStore();
        var grant = Create(store, durationMinutes: 60);

        Assert.Throws<ArgumentException>(() => store.Revoke(grant.Id, revokedBy, Now));
    }

    [Fact]
    public void Revoke_FailsClosed_OnDoubleRevoke()
    {
        var store = NewStore();
        var grant = Create(store, durationMinutes: 60);
        store.Revoke(grant.Id, "user-manager1", Now.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(
            () => store.Revoke(grant.Id, "user-manager1", Now.AddMinutes(20)));
    }

    [Fact]
    public void ListActive_FiltersExpiredAndRevoked()
    {
        var store = NewStore();
        var active = store.Create(Manager, Delegate, Tenant, Scopes, 120, Now);
        var expired = store.Create(Manager, "user-auditor1", Tenant, Scopes, 10, Now);
        var revoked = store.Create(Manager, "user-compliance1", Tenant, Scopes, 120, Now);
        store.Revoke(revoked.Id, "user-manager1", Now.AddMinutes(5));

        var list = store.ListActive(Now.AddMinutes(20));

        Assert.Contains(list, g => g.Id == active.Id);
        Assert.DoesNotContain(list, g => g.Id == expired.Id);
        Assert.DoesNotContain(list, g => g.Id == revoked.Id);
    }

    [Fact]
    public void ListAll_ReturnsEveryGrant_IncludingRevoked()
    {
        var store = NewStore();
        var a = store.Create(Manager, Delegate, Tenant, Scopes, 120, Now);
        var b = store.Create(Manager, "user-auditor1", Tenant, Scopes, 120, Now);
        store.Revoke(b.Id, "user-manager1", Now.AddMinutes(5));

        var all = store.ListAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, g => g.Id == a.Id);
        Assert.Contains(all, g => g.Id == b.Id);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownId()
    {
        var store = NewStore();

        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Create_BoundsStoreToMaxGrants()
    {
        var store = new DelegationGrantStore(maxGrants: 3);

        // Create well over the cap (all long-lived, so all still-active); the store must stay
        // bounded by evicting the oldest on every over-cap write — no unbounded growth.
        for (var i = 0; i < 10; i++)
        {
            store.Create(Manager, $"user-delegate{i}", Tenant, Scopes, 120, Now.AddMinutes(i));
        }

        Assert.Equal(3, store.ListAll().Count);
    }

    [Fact]
    public void Create_OverCap_EvictsExpiredBeforeActive()
    {
        var store = new DelegationGrantStore(maxGrants: 3);
        var expired = store.Create(Manager, "user-delegate1", Tenant, Scopes, 10, Now);   // expires Now+10
        var activeB = store.Create(Manager, "user-delegate2", Tenant, Scopes, 120, Now);
        var activeC = store.Create(Manager, "user-delegate3", Tenant, Scopes, 120, Now);

        // Fourth create 30 minutes on pushes the store over the cap. At that instant only the
        // 10-minute grant is terminal (expired), so it is evicted before any still-active grant.
        var activeD = store.Create(Manager, "user-delegate4", Tenant, Scopes, 120, Now.AddMinutes(30));

        Assert.Equal(3, store.ListAll().Count);
        Assert.Null(store.Get(expired.Id));
        Assert.NotNull(store.Get(activeB.Id));
        Assert.NotNull(store.Get(activeC.Id));
        Assert.NotNull(store.Get(activeD.Id));
    }

    [Fact]
    public void Create_OverCap_EvictsRevokedBeforeActive()
    {
        var store = new DelegationGrantStore(maxGrants: 2);
        var revoked = store.Create(Manager, "user-delegate1", Tenant, Scopes, 120, Now);
        var active = store.Create(Manager, "user-delegate2", Tenant, Scopes, 120, Now);
        store.Revoke(revoked.Id, "user-manager1", Now.AddMinutes(5));   // now terminal (revoked)

        // The next create pushes the store over the cap; the revoked (terminal) grant is the
        // least-valuable candidate and is evicted before the still-active grant.
        var activeC = store.Create(Manager, "user-delegate3", Tenant, Scopes, 120, Now.AddMinutes(10));

        Assert.Equal(2, store.ListAll().Count);
        Assert.Null(store.Get(revoked.Id));
        Assert.NotNull(store.Get(active.Id));
        Assert.NotNull(store.Get(activeC.Id));
    }

    [Fact]
    public void Constructor_FailsClosed_OnNonPositiveCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelegationGrantStore(maxGrants: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DelegationGrantStore(maxGrants: -1));
    }
}
