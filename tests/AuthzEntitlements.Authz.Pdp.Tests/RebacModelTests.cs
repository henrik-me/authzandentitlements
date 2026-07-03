using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using OpenFga.Sdk.Client.Model;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Structural tests for the CS07 ReBAC authorization model. They parse the embedded JSON with no
// running server, asserting the four relationship types and the composed can_view/can_transact
// relations are present and internally consistent (schema 1.1, referenced types exist).
public sealed class RebacModelTests
{
    private static JsonElement Model() => JsonDocument.Parse(RebacModel.Json).RootElement;

    private static JsonElement TypeDef(string type) =>
        Model().GetProperty("type_definitions").EnumerateArray()
            .Single(t => t.GetProperty("type").GetString() == type);

    private static IReadOnlyList<string> RelationNames(string type) =>
        TypeDef(type).TryGetProperty("relations", out var rels)
            ? rels.EnumerateObject().Select(p => p.Name).ToList()
            : [];

    [Fact]
    public void Model_UsesSchemaVersion_1_1()
    {
        Assert.Equal("1.1", Model().GetProperty("schema_version").GetString());
        Assert.Equal("1.1", RebacModel.SchemaVersion);
    }

    [Fact]
    public void Model_DeserializesIntoSdkRequest()
    {
        var request = JsonSerializer.Deserialize<ClientWriteAuthorizationModelRequest>(RebacModel.Json);

        Assert.NotNull(request);
        Assert.Equal("1.1", request!.SchemaVersion);
        Assert.NotNull(request.TypeDefinitions);
        Assert.Equal(5, request.TypeDefinitions!.Count);
    }

    [Theory]
    [InlineData(RebacTypes.User)]
    [InlineData(RebacTypes.Region)]
    [InlineData(RebacTypes.Branch)]
    [InlineData(RebacTypes.Customer)]
    [InlineData(RebacTypes.Account)]
    public void Model_DefinesType(string type)
    {
        Assert.Equal(type, TypeDef(type).GetProperty("type").GetString());
    }

    [Fact]
    public void Region_HasManagerRelation()
    {
        Assert.Contains(RebacRelations.Manager, RelationNames(RebacTypes.Region));
    }

    [Fact]
    public void Branch_InheritsRegionManager_Hierarchy()
    {
        var manager = TypeDef(RebacTypes.Branch).GetProperty("relations").GetProperty("manager");
        var children = manager.GetProperty("union").GetProperty("child").EnumerateArray().ToList();

        // A tupleToUserset over the branch's region -> the region's manager encodes the hierarchy.
        Assert.Contains(children, c =>
            c.TryGetProperty("tupleToUserset", out var ttu)
            && ttu.GetProperty("tupleset").GetProperty("relation").GetString() == RebacRelations.Region
            && ttu.GetProperty("computedUserset").GetProperty("relation").GetString() == RebacRelations.Manager);
    }

    [Fact]
    public void Account_DefinesAllStructuralRelations()
    {
        var relations = RelationNames(RebacTypes.Account);

        Assert.Contains(RebacRelations.Owner, relations);
        Assert.Contains(RebacRelations.Customer, relations);
        Assert.Contains(RebacRelations.Branch, relations);
        Assert.Contains(RebacRelations.Delegate, relations);
        Assert.Contains(RebacRelations.CanView, relations);
        Assert.Contains(RebacRelations.CanTransact, relations);
    }

    [Fact]
    public void Account_CanView_ComposesOwnerDelegateCustomerAndBranchManager()
    {
        var canView = TypeDef(RebacTypes.Account).GetProperty("relations").GetProperty("can_view");
        var children = canView.GetProperty("union").GetProperty("child").EnumerateArray().ToList();

        // owner + delegate via computedUserset.
        Assert.Contains(children, c =>
            c.TryGetProperty("computedUserset", out var cu)
            && cu.GetProperty("relation").GetString() == RebacRelations.Owner);
        Assert.Contains(children, c =>
            c.TryGetProperty("computedUserset", out var cu)
            && cu.GetProperty("relation").GetString() == RebacRelations.Delegate);

        // can_view-from-customer (RM indirection) + manager-from-branch (hierarchy) via tupleToUserset.
        Assert.Contains(children, c =>
            c.TryGetProperty("tupleToUserset", out var ttu)
            && ttu.GetProperty("tupleset").GetProperty("relation").GetString() == RebacRelations.Customer
            && ttu.GetProperty("computedUserset").GetProperty("relation").GetString() == RebacRelations.CanView);
        Assert.Contains(children, c =>
            c.TryGetProperty("tupleToUserset", out var ttu)
            && ttu.GetProperty("tupleset").GetProperty("relation").GetString() == RebacRelations.Branch
            && ttu.GetProperty("computedUserset").GetProperty("relation").GetString() == RebacRelations.Manager);
    }

    [Fact]
    public void AllRelationReferencedTypes_AreDefined()
    {
        var definedTypes = Model().GetProperty("type_definitions").EnumerateArray()
            .Select(t => t.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var typeDef in Model().GetProperty("type_definitions").EnumerateArray())
        {
            if (!typeDef.TryGetProperty("metadata", out var metadata))
            {
                continue;
            }

            foreach (var relation in metadata.GetProperty("relations").EnumerateObject())
            {
                foreach (var userType in relation.Value.GetProperty("directly_related_user_types").EnumerateArray())
                {
                    Assert.Contains(userType.GetProperty("type").GetString()!, definedTypes);
                }
            }
        }
    }
}
