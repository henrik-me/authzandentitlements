using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Structural tests for the seed relationship graph: every tuple is well-formed, all four
// relationship categories are represented, and the graph is self-consistent with the scenario
// catalog (every id a scenario references exists in the tuples). No server required.
public sealed class RebacSeedTuplesTests
{
    private static IReadOnlyList<RebacTuple> Tuples => RebacSeedTuples.Tuples;

    [Fact]
    public void EveryTuple_IsWellFormed()
    {
        foreach (var t in Tuples)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.User), $"tuple has blank user: {t}");
            Assert.False(string.IsNullOrWhiteSpace(t.Relation), $"tuple has blank relation: {t}");
            Assert.False(string.IsNullOrWhiteSpace(t.Object), $"tuple has blank object: {t}");
            // user and object are "type:id" (object always; user always for these fixtures).
            Assert.Contains(':', t.User);
            Assert.Contains(':', t.Object);
        }
    }

    [Fact]
    public void Tuples_AreUnique()
    {
        var keys = Tuples.Select(t => $"{t.User}|{t.Relation}|{t.Object}").ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Covers_Ownership_Relation()
    {
        Assert.Contains(Tuples, t => t.Relation == RebacRelations.Owner);
    }

    [Fact]
    public void Covers_RelationshipManager_Relation()
    {
        Assert.Contains(Tuples, t => t.Relation == RebacRelations.RelationshipManager);
    }

    [Fact]
    public void Covers_BranchAndRegion_Hierarchy()
    {
        // region -> branch link and a region manager together encode the hierarchy.
        Assert.Contains(Tuples, t => t.Relation == RebacRelations.Region && t.Object.StartsWith("branch:", StringComparison.Ordinal));
        Assert.Contains(Tuples, t => t.Relation == RebacRelations.Manager && t.Object.StartsWith("region:", StringComparison.Ordinal));
    }

    [Fact]
    public void Covers_Delegation_Relation()
    {
        Assert.Contains(Tuples, t => t.Relation == RebacRelations.Delegate);
    }

    [Fact]
    public void ForwardScenarioIds_ExistInSeedGraph()
    {
        var users = UserIds();
        var objects = ObjectIds();

        foreach (var s in RebacScenarioCatalog.Forward)
        {
            Assert.Contains($"{RebacTypes.User}:{s.UserId}", users);
            Assert.Contains($"{s.ObjectType}:{s.ObjectId}", objects);
        }
    }

    [Fact]
    public void WhoScenarioObjects_ExistInSeedGraph()
    {
        var objects = ObjectIds();
        foreach (var s in RebacScenarioCatalog.WhoCanAccess)
        {
            Assert.Contains($"{s.ObjectType}:{s.ObjectId}", objects);
        }
    }

    [Fact]
    public void WhatScenarioUsers_ExistInSeedGraph()
    {
        var users = UserIds();
        foreach (var s in RebacScenarioCatalog.WhatCanUserAccess)
        {
            Assert.Contains($"{RebacTypes.User}:{s.UserId}", users);
        }
    }

    // Every "user:..." principal appearing as a tuple user (subject side) in the seed graph;
    // users never appear on the object side (objects are always account:/customer:/branch:/region:).
    private static HashSet<string> UserIds()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Tuples)
        {
            if (t.User.StartsWith("user:", StringComparison.Ordinal)) { set.Add(t.User); }
        }

        return set;
    }

    // Every object ("type:id") that is the target of a tuple.
    private static HashSet<string> ObjectIds() =>
        Tuples.Select(t => t.Object).ToHashSet(StringComparer.Ordinal);
}
