using System.Threading.Channels;

namespace AuthzEntitlements.Authz.Pdp.Audit;

// Config-gated registration for the PDP decision audit sink (CS13 producer half). This is the
// single seam PdpServiceCollectionExtensions calls: it preserves the CS05 default (the offline
// LoggingPdpDecisionAuditSink) unless "Audit:Sink" is explicitly "http", so every existing PDP
// test that calls AddPdp with no Audit config keeps resolving the logging sink and stays
// deterministic/offline. When "http" is selected it wires the bounded-channel + background
// forwarder that ships events to the Audit.Service without ever blocking the decision path.
public static class PdpAuditSinkServiceCollectionExtensions
{
    public static IServiceCollection AddPdpDecisionAuditSink(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(AuditForwardingOptions.SectionName);
        services.Configure<AuditForwardingOptions>(section);

        var options = section.Get<AuditForwardingOptions>() ?? new AuditForwardingOptions();

        if (!string.Equals(options.Sink, "http", StringComparison.OrdinalIgnoreCase))
        {
            // Default / "logging" / unset: preserve the exact CS05 behavior.
            services.AddSingleton<IPdpDecisionAuditSink, LoggingPdpDecisionAuditSink>();
            return services;
        }

        // Fail closed on explicit misconfiguration: "http" was asked for but no destination given.
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            throw new InvalidOperationException(
                $"'{AuditForwardingOptions.SectionName}:Sink' is 'http' but "
                + $"'{AuditForwardingOptions.SectionName}:ServiceUrl' is missing or blank. "
                + "Set a non-blank Audit:ServiceUrl or use the default 'logging' sink.");
        }

        var capacity = options.ChannelCapacity > 0 ? options.ChannelCapacity : 2048;
        var timeoutSeconds = options.HttpTimeoutSeconds > 0 ? options.HttpTimeoutSeconds : 10;

        // One bounded channel shared by the sink (writer) and the worker (reader). FullMode=Wait
        // so that the sink's non-blocking TryWrite returns false when the buffer is full — the
        // sink then sheds (drops) and COUNTS that event. (TryWrite never blocks regardless of
        // FullMode, so the decision hot path stays non-blocking; DropWrite would instead make
        // TryWrite always return true and drop silently inside the channel, which the sink could
        // not observe or count — see the drop-count contract on HttpForwardingPdpDecisionAuditSink.)
        var channel = Channel.CreateBounded<PdpDecisionAuditEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });

        services.AddSingleton(channel);
        services.AddSingleton(channel.Reader);
        services.AddSingleton(channel.Writer);

        // Normalize BaseAddress to end with '/' so the worker's relative "api/audit/decisions"
        // resolves to {ServiceUrl}/api/audit/decisions rather than replacing the last segment.
        var baseAddress = new Uri(EnsureTrailingSlash(options.ServiceUrl!), UriKind.Absolute);

        services.AddHttpClient(HttpForwardingPdpDecisionAuditSink.HttpClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        services.AddSingleton<IPdpDecisionAuditSink, HttpForwardingPdpDecisionAuditSink>();
        services.AddHostedService<AuditForwardingWorker>();

        return services;
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
