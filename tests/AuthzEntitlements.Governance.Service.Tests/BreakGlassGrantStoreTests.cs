using AuthzEntitlements.Governance.Service.BreakGlass;
using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Read-time expiry (IsActive) plus the mandatory post-review gate (RequiresReview) are how the
// break-glass store enforces a bounded emergency elevation without a background sweeper. A
// fixed clock (GovernanceTestData.Now) is injected as "now" so the window arithmetic and the
// review gate are deterministic, and every mutation is proven fail-closed.
public sealed class BreakGlassGrantStoreTests
{
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;
    private const string Principal = "user-teller1";
    private const string Tenant = GovernanceTestData.Contoso;
    private const string Action = "bank.transaction.create";
    private const string Justification = "core banking outage — manual settlement";

    private static BreakGlassGrantStore NewStore() => new();

    private static BreakGlassGrant Issue(BreakGlassGrantStore store, int durationMinutes = 30) =>
        store.Issue(Principal, Tenant, Action, Justification, durationMinutes, Now);

    [Fact]
    public void Issue_SetsWindowAndFields()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        Assert.NotEqual(Guid.Empty, grant.Id);
        Assert.Equal(Principal, grant.PrincipalId);
        Assert.Equal(Tenant, grant.TenantCode);
        Assert.Equal(Action, grant.Action);
        Assert.Equal(Justification, grant.Justification);
        Assert.Equal(Now, grant.GrantedAt);
        Assert.Equal(Now.AddMinutes(30), grant.ExpiresAt);
        Assert.Null(grant.ReviewedAt);
        Assert.Null(grant.ReviewedBy);
        Assert.Null(grant.ReviewOutcome);
    }

    [Fact]
    public void Issue_ActiveBeforeExpiry()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        Assert.True(grant.IsActive(Now));
        Assert.True(grant.IsActive(grant.ExpiresAt.AddMinutes(-1)));
    }

    [Fact]
    public void Issue_InactiveAtExpiryInstant_BoundaryExclusive()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        // now == ExpiresAt is already expired (exclusive boundary, matching AccessGrant).
        Assert.False(grant.IsActive(grant.ExpiresAt));
    }

    [Fact]
    public void Issue_InactiveAfterExpiry()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        Assert.False(grant.IsActive(grant.ExpiresAt.AddMinutes(1)));
    }

    [Theory]
    [InlineData("", Tenant, Action, Justification)]
    [InlineData("   ", Tenant, Action, Justification)]
    [InlineData(Principal, "", Action, Justification)]
    [InlineData(Principal, Tenant, "", Justification)]
    [InlineData(Principal, Tenant, Action, "")]
    [InlineData(Principal, Tenant, Action, "   ")]
    public void Issue_FailsClosed_OnBlankInput(string principal, string tenant, string action, string justification)
    {
        var store = NewStore();

        Assert.Throws<ArgumentException>(() => store.Issue(principal, tenant, action, justification, 30, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-60)]
    public void Issue_FailsClosed_OnNonPositiveDuration(int minutes)
    {
        var store = NewStore();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.Issue(Principal, Tenant, Action, Justification, minutes, Now));
    }

    [Fact]
    public void ListActive_FiltersExpired()
    {
        var store = NewStore();
        var shortGrant = store.Issue(Principal, Tenant, Action, Justification, 10, Now);
        var longGrant = store.Issue(Principal, Tenant, Action, Justification, 120, Now);

        // Twenty minutes on: the 10-minute grant has expired, the 120-minute one has not.
        var active = store.ListActive(Now.AddMinutes(20));

        Assert.Contains(active, g => g.Id == longGrant.Id);
        Assert.DoesNotContain(active, g => g.Id == shortGrant.Id);
    }

    [Fact]
    public void ListAll_ReturnsEveryGrant_IncludingExpired()
    {
        var store = NewStore();
        var a = store.Issue(Principal, Tenant, Action, Justification, 10, Now);
        var b = store.Issue(Principal, Tenant, Action, Justification, 120, Now);

        var all = store.ListAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, g => g.Id == a.Id);
        Assert.Contains(all, g => g.Id == b.Id);
    }

    [Fact]
    public void RequiresReview_FalseWhileActive()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        Assert.False(grant.RequiresReview(Now));
        Assert.False(grant.RequiresReview(grant.ExpiresAt.AddMinutes(-1)));
    }

    [Fact]
    public void RequiresReview_TrueOnceExpiredAndUnreviewed()
    {
        var grant = Issue(NewStore(), durationMinutes: 30);

        Assert.True(grant.RequiresReview(grant.ExpiresAt));
        Assert.True(grant.RequiresReview(grant.ExpiresAt.AddMinutes(5)));
    }

    [Fact]
    public void RequiresReview_FalseAfterReview()
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);

        // Review post-expiry (the store rejects reviewing an active grant); observe the returned
        // reviewed grant, not the earlier issued copy, which the review mutation does not touch.
        var reviewed = store.Review(grant.Id, "user-compliance1", "approved", grant.ExpiresAt.AddMinutes(5));

        Assert.False(reviewed.RequiresReview(grant.ExpiresAt.AddMinutes(10)));
    }

    [Fact]
    public void ListRequiringReview_ReturnsExpiredUnreviewed_Only()
    {
        var store = NewStore();
        var expired = store.Issue(Principal, Tenant, Action, Justification, 10, Now);
        var active = store.Issue(Principal, Tenant, Action, Justification, 120, Now);
        var reviewed = store.Issue(Principal, Tenant, Action, Justification, 10, Now);

        var at = Now.AddMinutes(30);
        store.Review(reviewed.Id, "user-compliance1", "approved", at);

        var pending = store.ListRequiringReview(at);

        Assert.Contains(pending, g => g.Id == expired.Id);
        Assert.DoesNotContain(pending, g => g.Id == active.Id);
        Assert.DoesNotContain(pending, g => g.Id == reviewed.Id);
    }

    [Fact]
    public void Review_RecordsOutcomeReviewerAndTimestamp()
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);
        var reviewedAt = grant.ExpiresAt.AddMinutes(5);

        var reviewed = store.Review(grant.Id, "user-compliance1", "approved", reviewedAt);

        Assert.Equal(reviewedAt, reviewed.ReviewedAt);
        Assert.Equal("user-compliance1", reviewed.ReviewedBy);
        Assert.Equal("approved", reviewed.ReviewOutcome);
    }

    [Fact]
    public void Review_FailsClosed_OnUnknownId()
    {
        var store = NewStore();

        Assert.Throws<KeyNotFoundException>(
            () => store.Review(Guid.NewGuid(), "user-compliance1", "approved", Now));
    }

    [Theory]
    [InlineData("", "approved")]
    [InlineData("   ", "approved")]
    [InlineData("user-compliance1", "")]
    [InlineData("user-compliance1", "   ")]
    public void Review_FailsClosed_OnBlankReviewerOrOutcome(string reviewedBy, string outcome)
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);

        Assert.Throws<ArgumentException>(() => store.Review(grant.Id, reviewedBy, outcome, Now));
    }

    [Fact]
    public void Review_FailsClosed_WhileStillActive()
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);

        // The mandatory review is POST-expiry only: reviewing a grant that is still within its
        // window is rejected, so a caller cannot pre-emptively mark it reviewed and skip the queue.
        var ex = Assert.Throws<InvalidOperationException>(
            () => store.Review(grant.Id, "user-compliance1", "approved", grant.ExpiresAt.AddMinutes(-1)));
        Assert.Contains("before it expires", ex.Message);

        // No review was recorded: once expired the grant is still unreviewed and enters the queue.
        var stored = store.Get(grant.Id);
        Assert.NotNull(stored);
        Assert.Null(stored.ReviewedAt);
        Assert.True(stored.RequiresReview(grant.ExpiresAt));
        Assert.Contains(store.ListRequiringReview(grant.ExpiresAt), g => g.Id == grant.Id);
    }

    [Fact]
    public void Review_FailsClosed_OnDoubleReview()
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);
        store.Review(grant.Id, "user-compliance1", "approved", grant.ExpiresAt.AddMinutes(5));

        var ex = Assert.Throws<InvalidOperationException>(
            () => store.Review(grant.Id, "user-auditor1", "rejected", grant.ExpiresAt.AddMinutes(10)));
        Assert.Contains("already reviewed", ex.Message);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownId()
    {
        var store = NewStore();

        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Get_ReturnsIssuedGrant()
    {
        var store = NewStore();
        var grant = Issue(store, durationMinutes: 30);

        var fetched = store.Get(grant.Id);

        Assert.NotNull(fetched);
        // Defensive copy: value-equal to the issued grant but a distinct instance, so a caller
        // cannot mutate store state through the returned object.
        Assert.NotSame(grant, fetched);
        Assert.Equal(grant.Id, fetched.Id);
        Assert.Equal(grant.PrincipalId, fetched.PrincipalId);
        Assert.Equal(grant.Action, fetched.Action);
        Assert.Equal(grant.ExpiresAt, fetched.ExpiresAt);
    }

    [Fact]
    public void Issue_BoundsStoreToMaxGrants()
    {
        var store = new BreakGlassGrantStore(maxGrants: 3);

        // Issue well over the cap (all long-lived, so all still-active); the store must stay
        // bounded by evicting the oldest on every over-cap write — no unbounded growth.
        for (var i = 0; i < 10; i++)
        {
            store.Issue(Principal, Tenant, Action, Justification, 120, Now.AddMinutes(i));
        }

        Assert.Equal(3, store.ListAll().Count);
    }

    [Fact]
    public void Issue_OverCap_EvictsTerminalBeforeActive()
    {
        var store = new BreakGlassGrantStore(maxGrants: 3);
        var expired = store.Issue(Principal, Tenant, Action, Justification, 10, Now);   // expires Now+10
        var activeB = store.Issue(Principal, Tenant, Action, Justification, 120, Now);
        var activeC = store.Issue(Principal, Tenant, Action, Justification, 120, Now);

        // Fourth issue 30 minutes on pushes the store over the cap. At that instant only the
        // 10-minute grant is terminal (expired), so it is evicted before any still-active grant.
        var activeD = store.Issue(Principal, Tenant, Action, Justification, 120, Now.AddMinutes(30));

        Assert.Equal(3, store.ListAll().Count);
        Assert.Null(store.Get(expired.Id));
        Assert.NotNull(store.Get(activeB.Id));
        Assert.NotNull(store.Get(activeC.Id));
        Assert.NotNull(store.Get(activeD.Id));
    }

    [Fact]
    public void Constructor_FailsClosed_OnNonPositiveCap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakGlassGrantStore(maxGrants: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakGlassGrantStore(maxGrants: -1));
    }
}
