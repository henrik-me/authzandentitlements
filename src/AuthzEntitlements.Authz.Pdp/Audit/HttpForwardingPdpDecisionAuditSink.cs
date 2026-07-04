using System.Threading.Channels;

namespace AuthzEntitlements.Authz.Pdp.Audit;

// HTTP-forwarding audit sink: the producer half of the CS13 pipeline. Record() runs on the
// PDP decision hot path (once per evaluation), so it MUST never block and never throw — a
// down or slow Audit.Service must not fail authorization. It only hands the event to a
// bounded channel (non-blocking TryWrite); the AuditForwardingWorker drains + ships it.
// On a full channel the write is dropped (availability over completeness) and counted so a
// test — and operators via the warning — can observe backpressure.
public sealed class HttpForwardingPdpDecisionAuditSink : IPdpDecisionAuditSink
{
    // Well-known name of the forwarding HttpClient, shared by the DI registration and worker.
    public const string HttpClientName = "pdp-audit-forwarder";

    private readonly ChannelWriter<PdpDecisionAuditEvent> _writer;
    private readonly ILogger<HttpForwardingPdpDecisionAuditSink> _logger;
    private long _dropped;

    public HttpForwardingPdpDecisionAuditSink(
        ChannelWriter<PdpDecisionAuditEvent> writer,
        ILogger<HttpForwardingPdpDecisionAuditSink> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    // Count of events dropped because the channel was full. Read-only for tests/diagnostics.
    public long Dropped => Volatile.Read(ref _dropped);

    public void Record(PdpDecisionAuditEvent decisionEvent)
    {
        if (_writer.TryWrite(decisionEvent))
        {
            return;
        }

        // Full channel (FullMode=Wait makes TryWrite return false rather than block): shed the
        // event rather than block the decision. Log at a throttled cadence so a sustained outage
        // does not flood the log with one line per drop.
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 1000 == 0)
        {
            _logger.LogWarning(
                "Audit forwarding channel full; dropped decision audit event (total dropped={Dropped}).",
                dropped);
        }
    }
}
