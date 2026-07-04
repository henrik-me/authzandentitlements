using System.Net;
using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers HttpGovernanceClient's fail-closed vs self-skip classification — the boundary that keeps a
// running-but-erroring governance service from being silently reported as "offline". A REACHED
// non-success status (or a malformed body) must fail closed (ComplianceDataException → non-zero
// exit); only a transport-level failure/timeout is an offline self-skip
// (GovernanceUnreachableException → collected=false).
public sealed class HttpGovernanceClientTests
{
    private static HttpClient StubClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://governance.test") };

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ReachedNonSuccessStatus_FailsClosed(HttpStatusCode status)
    {
        using var http = StubClient(new StubHttpMessageHandler(status, "{}"));
        var client = new HttpGovernanceClient(http);

        await Assert.ThrowsAsync<ComplianceDataException>(
            () => client.GetCampaignsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TransportFailure_SelfSkipsAsUnreachable()
    {
        using var http = StubClient(
            StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused")));
        var client = new HttpGovernanceClient(http);

        await Assert.ThrowsAsync<GovernanceUnreachableException>(
            () => client.GetAccessPackagesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_SelfSkipsAsUnreachable()
    {
        using var http = StubClient(
            StubHttpMessageHandler.Throwing(new TaskCanceledException("timed out")));
        var client = new HttpGovernanceClient(http);

        await Assert.ThrowsAsync<GovernanceUnreachableException>(
            () => client.GetPrincipalGrantsAsync("user-teller1", CancellationToken.None));
    }

    [Fact]
    public async Task ReachedMalformedBody_FailsClosed()
    {
        using var http = StubClient(new StubHttpMessageHandler(HttpStatusCode.OK, "not-json"));
        var client = new HttpGovernanceClient(http);

        await Assert.ThrowsAsync<ComplianceDataException>(
            () => client.GetCampaignsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Success_ReturnsParsedList()
    {
        const string body =
            "[ { \"id\": \"11111111-1111-1111-1111-111111111111\", \"name\": \"Q3 recert\", " +
            "\"tenantCode\": \"CONTOSO\", \"status\": \"Open\", " +
            "\"items\": [ { \"decision\": \"Certify\" } ] } ]";
        using var http = StubClient(new StubHttpMessageHandler(HttpStatusCode.OK, body));
        var client = new HttpGovernanceClient(http);

        var campaigns = await client.GetCampaignsAsync(CancellationToken.None);

        var campaign = Assert.Single(campaigns);
        Assert.Equal("Q3 recert", campaign.Name);
        Assert.Equal("CONTOSO", campaign.TenantCode);
    }

    [Fact]
    public async Task CallerCancellation_IsHonored_NotSelfSkipped()
    {
        using var http = StubClient(new StubHttpMessageHandler(System.Net.HttpStatusCode.OK, "[]"));
        var client = new HttpGovernanceClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A genuine caller cancellation must propagate as cancellation — NOT be swallowed as an
        // offline self-skip (GovernanceUnreachableException) or a fail-closed data error.
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetCampaignsAsync(cts.Token));
        Assert.IsNotType<GovernanceUnreachableException>(ex);
    }

    [Fact]
    public async Task ReporterDrivingRealClient_DoesNotSelfSkipOnReachedError()
    {
        // End-to-end: a reporter driving the REAL client against a reached-but-erroring service
        // must propagate the fail-closed error, NOT self-skip (which would hide the failure and
        // report all-clear evidence for a broken service).
        using var http = StubClient(new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "boom"));
        var client = new HttpGovernanceClient(http);

        await Assert.ThrowsAsync<ComplianceDataException>(
            () => CertificationReporter.CollectAsync(client, "repro", CancellationToken.None));
    }
}
