using System.Reflection;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// CS62 — the per-page data-flow overviews are pure data, so their AuthN/AuthZ story is asserted
// offline. Guards that every page's diagram is well-formed and that the security-relevant flows
// name the right participants and aspects.
public class DataFlowDiagramsTests
{
    public static IEnumerable<object[]> AllFlows() =>
        typeof(DataFlowDiagrams)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(DataFlow))
            .Select(p => new object[] { p.Name, (DataFlow)p.GetValue(null)! });

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Every_flow_is_wellformed(string name, DataFlow flow)
    {
        Assert.False(string.IsNullOrWhiteSpace(flow.Caption), $"{name}: caption");
        Assert.True(flow.Nodes.Count >= 2, $"{name}: expected >= 2 nodes");

        // The origin (browser) has no incoming call; every later hop describes its call.
        Assert.Null(flow.Nodes[0].Call);
        Assert.All(flow.Nodes.Skip(1), n => Assert.False(string.IsNullOrWhiteSpace(n.Call), $"{name}: hop '{n.Name}' needs a call"));
        Assert.All(flow.Nodes, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n.Name), $"{name}: node name");
            Assert.False(string.IsNullOrWhiteSpace(n.Role), $"{name}: node role");
            Assert.All(n.Notes, note => Assert.False(string.IsNullOrWhiteSpace(note.Text), $"{name}: note text"));
        });

        // Authentication is part of every page's story (at minimum how the identity is established).
        Assert.Contains(Notes(flow), note => note.Aspect == FlowAspect.AuthN);
    }

    [Fact]
    public void Bank_read_path_names_the_gateway_and_api_and_marks_authz()
    {
        var flow = DataFlowDiagrams.Accounts;
        Assert.Contains(flow.Nodes, n => n.Name == "Edge Gateway");
        Assert.Contains(flow.Nodes, n => n.Name == "Bank.Api");
        Assert.Contains(Notes(flow), note => note.Aspect == FlowAspect.AuthZ);
        // The token-forwarding + WWW-Authenticate story is surfaced.
        Assert.Contains(Notes(flow), note =>
            note.Aspect == FlowAspect.AuthN && note.Text.Contains("WWW-Authenticate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void New_transaction_flow_includes_the_commercial_license_layer()
    {
        Assert.Contains(Notes(DataFlowDiagrams.NewTransaction), note => note.Aspect == FlowAspect.License);
    }

    [Fact]
    public void Playground_flow_marks_the_anonymous_pdp_that_forwards_no_token()
    {
        var flow = DataFlowDiagrams.Playground;
        Assert.Contains(flow.Nodes, n => n.Name == "Authz PDP");
        Assert.Contains(Notes(flow), note =>
            note.Aspect == FlowAspect.AuthN && note.Text.Contains("no token", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<FlowNote> Notes(DataFlow flow) => flow.Nodes.SelectMany(n => n.Notes);
}
