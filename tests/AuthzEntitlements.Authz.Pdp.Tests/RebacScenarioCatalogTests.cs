using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Self-consistency tests for the ReBAC scenario catalog: ids are unique, relations are the two
// relations the model computes, and every subject/object a scenario names is grounded in the seed graph.
// These are structural (no server) — they catch a catalog that drifts from the model/tuples.
public sealed class RebacScenarioCatalogTests
{
    private static readonly HashSet<string> ComputedRelations =
        new(StringComparer.Ordinal) { RebacRelations.CanView, RebacRelations.CanTransact };

    private static HashSet<string> GraphUsers() =>
        RebacSeedTuples.Tuples
            .Where(t => t.User.StartsWith("user:", StringComparison.Ordinal))
            .Select(t => t.User["user:".Length..])
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> GraphAccountIds() =>
        RebacSeedTuples.Tuples
            .Where(t => t.Object.StartsWith("account:", StringComparison.Ordinal))
            .Select(t => t.Object["account:".Length..])
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Catalog_CoversForwardAndBothReverseDirections()
    {
        Assert.NotEmpty(RebacScenarioCatalog.Forward);
        Assert.NotEmpty(RebacScenarioCatalog.WhoCanAccess);
        Assert.NotEmpty(RebacScenarioCatalog.WhatCanUserAccess);
    }

    [Fact]
    public void Forward_CoversBothAllowAndDeny()
    {
        Assert.Contains(RebacScenarioCatalog.Forward, s => s.ExpectAllowed);
        Assert.Contains(RebacScenarioCatalog.Forward, s => !s.ExpectAllowed);
    }

    [Fact]
    public void Forward_ExercisesBothComputedRelations()
    {
        Assert.Contains(RebacScenarioCatalog.Forward, s => s.Relation == RebacRelations.CanView);
        Assert.Contains(RebacScenarioCatalog.Forward, s => s.Relation == RebacRelations.CanTransact);
    }

    [Fact]
    public void ForwardScenarioIds_AreUnique()
    {
        var ids = RebacScenarioCatalog.Forward.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryForwardRelation_IsAModelComputedRelation()
    {
        Assert.All(RebacScenarioCatalog.Forward, s => Assert.Contains(s.Relation, ComputedRelations));
    }

    [Fact]
    public void EveryForwardScenario_ReferencesGraphUsersAndAccounts()
    {
        var users = GraphUsers();
        var accounts = GraphAccountIds();

        Assert.All(RebacScenarioCatalog.Forward, s =>
        {
            Assert.Contains(s.UserId, users);
            Assert.Equal(RebacTypes.Account, s.ObjectType);
            Assert.Contains(s.ObjectId, accounts);
        });
    }

    [Fact]
    public void WhoScenarios_ExpectGraphUsers_OnAccounts()
    {
        var users = GraphUsers();
        var accounts = GraphAccountIds();

        Assert.All(RebacScenarioCatalog.WhoCanAccess, s =>
        {
            Assert.Equal(RebacTypes.Account, s.ObjectType);
            Assert.Contains(s.ObjectId, accounts);
            Assert.NotEmpty(s.ExpectedUserIds);
            Assert.All(s.ExpectedUserIds, u => Assert.Contains(u, users));
        });
    }

    [Fact]
    public void WhatScenarios_ReferenceGraphUsersAndAccounts_WithDisjointExpectAndExclude()
    {
        var users = GraphUsers();
        var accounts = GraphAccountIds();

        Assert.All(RebacScenarioCatalog.WhatCanUserAccess, s =>
        {
            Assert.Contains(s.UserId, users);
            Assert.Equal(RebacTypes.Account, s.ObjectType);
            Assert.NotEmpty(s.ExpectedObjectIds);
            Assert.All(s.ExpectedObjectIds, o => Assert.Contains(o, accounts));
            Assert.All(s.ExcludedObjectIds, o => Assert.Contains(o, accounts));
            // An id cannot be both expected-present and expected-absent.
            Assert.Empty(s.ExpectedObjectIds.Intersect(s.ExcludedObjectIds, StringComparer.Ordinal));
        });
    }
}
