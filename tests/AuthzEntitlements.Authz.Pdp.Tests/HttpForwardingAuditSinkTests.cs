using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using AuthzEntitlements.Authz.Pdp.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Covers the CS13 HTTP-forwarding audit producer: config-gated registration (default logging
// sink preserved, http sink opt-in, fail-closed on blank ServiceUrl), the non-blocking + drop-
// counting sink, and the resilient background forwarder (correct POST + camelCase wire shape,
// keeps draining across a failing delivery). All tests are fully offline — the Audit.Service is
// stubbed via an HttpMessageHandler, exactly as the Bank.Api tests stub their downstream.
public sealed class HttpForwardingAuditSinkTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static PdpDecisionAuditEvent SampleEvent(string traceId = "trace-1") =>
        new(
            TimestampUtc: new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            TraceId: traceId,
            Provider: "reference",
            SubjectId: "user-teller1",
            Action: "account.read",
            ResourceType: "account",
            ResourceId: "acct-1",
            Decision: "Permit",
            Reason: "permit",
            Tenant: "contoso");

    // Records every request it sees (method/uri/body) and signals once an expected count arrives,
    // so a test can await delivery without arbitrary sleeps.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;
        private readonly int _expected;
        private readonly TaskCompletionSource _done =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public RecordingHandler(int expected, Func<int, HttpResponseMessage> responder)
        {
            _expected = expected;
            _responder = responder;
        }

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        public Task Completed => _done.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _count);
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Enqueue(new CapturedRequest(request.Method, request.RequestUri, body));
            var response = _responder(n);
            if (Requests.Count >= _expected)
            {
                _done.TrySetResult();
            }

            return response;
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? RequestUri, string? Body);

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // ---- Registration: default (logging) sink is preserved ----

    [Fact]
    public void AddPdpDecisionAuditSink_WithNoAuditSection_ResolvesLoggingSink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdpDecisionAuditSink(Config());

        using var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<IPdpDecisionAuditSink>();

        Assert.IsType<LoggingPdpDecisionAuditSink>(sink);
    }

    [Fact]
    public void AddPdpDecisionAuditSink_WithSinkLogging_ResolvesLoggingSink()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdpDecisionAuditSink(Config(("Audit:Sink", "logging")));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LoggingPdpDecisionAuditSink>(
            provider.GetRequiredService<IPdpDecisionAuditSink>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    // ---- Registration: http sink opt-in ----

    [Fact]
    public void AddPdpDecisionAuditSink_WithHttpSink_ResolvesHttpSinkAndRegistersWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdpDecisionAuditSink(Config(
            ("Audit:Sink", "http"),
            ("Audit:ServiceUrl", "http://audit-service")));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HttpForwardingPdpDecisionAuditSink>(
            provider.GetRequiredService<IPdpDecisionAuditSink>());
        Assert.Single(
            provider.GetServices<IHostedService>().OfType<AuditForwardingWorker>());
    }

    [Fact]
    public void AddPdpDecisionAuditSink_WithHttpSink_UppercaseIsCaseInsensitive()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdpDecisionAuditSink(Config(
            ("Audit:Sink", "HTTP"),
            ("Audit:ServiceUrl", "http://audit-service")));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HttpForwardingPdpDecisionAuditSink>(
            provider.GetRequiredService<IPdpDecisionAuditSink>());
    }

    [Fact]
    public void AddPdpDecisionAuditSink_WithHttpSink_SharesOneChannelBetweenSinkAndWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPdpDecisionAuditSink(Config(
            ("Audit:Sink", "http"),
            ("Audit:ServiceUrl", "http://audit-service")));

        using var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<ChannelWriter<PdpDecisionAuditEvent>>();
        var reader = provider.GetRequiredService<ChannelReader<PdpDecisionAuditEvent>>();

        // The writer accepts and the reader observes the SAME item => one shared channel.
        Assert.True(writer.TryWrite(SampleEvent()));
        Assert.True(reader.TryRead(out var read));
        Assert.Equal("trace-1", read!.TraceId);
    }

    // ---- Registration: fail-closed on explicit misconfiguration ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPdpDecisionAuditSink_WithHttpSinkAndBlankServiceUrl_Throws(string? serviceUrl)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = serviceUrl is null
            ? Config(("Audit:Sink", "http"))
            : Config(("Audit:Sink", "http"), ("Audit:ServiceUrl", serviceUrl));

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPdpDecisionAuditSink(config));
        Assert.Contains("ServiceUrl", ex.Message);
    }

    // ---- Sink behavior ----

    [Fact]
    public void Record_WhenChannelHasCapacity_EnqueuesWithoutDropping()
    {
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.Wait });
        var sink = new HttpForwardingPdpDecisionAuditSink(
            channel.Writer, NullLogger<HttpForwardingPdpDecisionAuditSink>.Instance);

        sink.Record(SampleEvent());

        Assert.Equal(0, sink.Dropped);
        Assert.True(channel.Reader.TryRead(out _));
    }

    [Fact]
    public void Record_WhenChannelFull_DropsAndCountsWithoutThrowing()
    {
        // Capacity-1 channel with no reader draining: the first write fills it, the second is
        // shed. The sink must count exactly one drop and never throw on the decision path.
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        var sink = new HttpForwardingPdpDecisionAuditSink(
            channel.Writer, NullLogger<HttpForwardingPdpDecisionAuditSink>.Instance);

        var ex = Record.Exception(() =>
        {
            sink.Record(SampleEvent("first"));
            sink.Record(SampleEvent("second"));
        });

        Assert.Null(ex);
        Assert.Equal(1, sink.Dropped);
    }

    // ---- End-to-end: sink -> channel -> worker -> HTTP ----

    [Fact]
    public async Task Worker_ForwardsEnqueuedEvent_AsPostToIngestPath_WithCamelCaseBody()
    {
        var handler = new RecordingHandler(expected: 1, _ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://audit-service/") };
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });
        var sink = new HttpForwardingPdpDecisionAuditSink(
            channel.Writer, NullLogger<HttpForwardingPdpDecisionAuditSink>.Instance);
        var worker = new AuditForwardingWorker(
            channel.Reader,
            new StubHttpClientFactory(httpClient),
            Options.Create(new AuditForwardingOptions()),
            NullLogger<AuditForwardingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            sink.Record(SampleEvent("trace-e2e"));
            await handler.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(handler.Requests.TryDequeue(out var captured));
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/api/audit/decisions", captured.RequestUri!.AbsolutePath);
        Assert.Equal("http://audit-service/api/audit/decisions", captured.RequestUri.ToString());

        Assert.NotNull(captured.Body);
        Assert.Contains("\"traceId\":", captured.Body); // camelCase on the wire
        Assert.Contains("\"timestampUtc\":", captured.Body);
        var roundTripped = JsonSerializer.Deserialize<PdpDecisionAuditEvent>(captured.Body!, WebJson);
        Assert.NotNull(roundTripped);
        Assert.Equal("trace-e2e", roundTripped!.TraceId);
        Assert.Equal("reference", roundTripped.Provider);
        Assert.Equal("account", roundTripped.ResourceType);
        Assert.Equal("Permit", roundTripped.Decision);
        Assert.Equal("contoso", roundTripped.Tenant);
    }

    [Fact]
    public async Task Worker_WhenFirstDeliveryFails_ContinuesDrainingAndForwardsNext()
    {
        // First POST => 500, second => 200. The worker must not crash on the failure; it keeps
        // draining and delivers the second event. Assert both requests were seen.
        var handler = new RecordingHandler(
            expected: 2,
            n => new HttpResponseMessage(
                n == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://audit-service/") };
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });
        var sink = new HttpForwardingPdpDecisionAuditSink(
            channel.Writer, NullLogger<HttpForwardingPdpDecisionAuditSink>.Instance);
        var worker = new AuditForwardingWorker(
            channel.Reader,
            new StubHttpClientFactory(httpClient),
            Options.Create(new AuditForwardingOptions()),
            NullLogger<AuditForwardingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            sink.Record(SampleEvent("first"));
            sink.Record(SampleEvent("second"));
            await handler.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, handler.Requests.Count);
        var traces = handler.Requests
            .Select(r => JsonSerializer.Deserialize<PdpDecisionAuditEvent>(r.Body!, WebJson)!.TraceId)
            .ToList();
        Assert.Contains("first", traces);
        Assert.Contains("second", traces);
    }

    [Fact]
    public async Task Worker_WhenDeliveryThrows_ContinuesDrainingAndForwardsNext()
    {
        // First POST throws (transport failure), second succeeds. The worker swallows the
        // exception and keeps draining.
        var handler = new RecordingHandler(
            expected: 2,
            n => n == 1
                ? throw new HttpRequestException("audit service unreachable")
                : new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://audit-service/") };
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });
        var sink = new HttpForwardingPdpDecisionAuditSink(
            channel.Writer, NullLogger<HttpForwardingPdpDecisionAuditSink>.Instance);
        var worker = new AuditForwardingWorker(
            channel.Reader,
            new StubHttpClientFactory(httpClient),
            Options.Create(new AuditForwardingOptions()),
            NullLogger<AuditForwardingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            sink.Record(SampleEvent("first"));
            sink.Record(SampleEvent("second"));
            await handler.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, handler.Requests.Count);
    }
}
