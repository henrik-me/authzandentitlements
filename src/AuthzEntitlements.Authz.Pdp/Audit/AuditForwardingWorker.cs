using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Audit;

// Background drain for the HTTP-forwarding audit sink: reads decision events off the bounded
// channel and POSTs each to the CS13 Audit.Service ingest endpoint, entirely off the decision
// hot path. It is deliberately resilient — a non-2xx response or a transport exception is
// logged and swallowed so the worker keeps draining; a transient or absent Audit.Service must
// never crash the PDP or stall the channel. Only cancellation (shutdown) ends the loop.
public sealed class AuditForwardingWorker : BackgroundService
{
    // Relative to the HttpClient BaseAddress (which is normalized to end with '/'), this
    // resolves to {ServiceUrl}/api/audit/decisions — the shared CS13 ingest contract.
    private const string IngestPath = "api/audit/decisions";

    // Web defaults => camelCase on the wire, matching the Audit.Service ingest expectation.
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly ChannelReader<PdpDecisionAuditEvent> _reader;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuditForwardingWorker> _logger;

    public AuditForwardingWorker(
        ChannelReader<PdpDecisionAuditEvent> reader,
        IHttpClientFactory httpClientFactory,
        IOptions<AuditForwardingOptions> options,
        ILogger<AuditForwardingWorker> logger)
    {
        _reader = reader;
        _httpClientFactory = httpClientFactory;
        _ = options; // Options are applied to the named HttpClient at registration; kept for parity/DI.
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var decisionEvent in _reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var client = _httpClientFactory.CreateClient(
                    HttpForwardingPdpDecisionAuditSink.HttpClientName);

                using var response = await client
                    .PostAsJsonAsync(IngestPath, decisionEvent, WireJson, stoppingToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Audit forwarding got non-success status {StatusCode} for decision trace={TraceId}; dropping event.",
                        (int)response.StatusCode,
                        decisionEvent.TraceId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown — stop draining quietly.
                break;
            }
            catch (Exception ex)
            {
                // Transient/absent Audit.Service: log and keep draining. The worker must never
                // crash on a delivery failure.
                _logger.LogWarning(
                    ex,
                    "Audit forwarding failed for decision trace={TraceId}; dropping event and continuing.",
                    decisionEvent.TraceId);
            }
        }
    }
}
