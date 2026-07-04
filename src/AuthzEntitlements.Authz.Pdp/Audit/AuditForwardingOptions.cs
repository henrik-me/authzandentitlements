namespace AuthzEntitlements.Authz.Pdp.Audit;

// Config for the CS13 audit pipeline's PDP producer half. Bound from the "Audit" section.
// The default (Sink="logging") preserves CS05's deterministic, offline logging sink — the
// HTTP-forwarding sink is strictly opt-in ("http") so builds/tests/`aspire run` never need a
// live Audit.Service. ServiceUrl is required only when Sink="http" (fail closed there).
public sealed class AuditForwardingOptions
{
    public const string SectionName = "Audit";

    // Which sink backs IPdpDecisionAuditSink: "logging" (default) or "http".
    public string Sink { get; set; } = "logging";

    // Base URL of the CS13 Audit.Service. Required (non-blank) only when Sink="http".
    public string? ServiceUrl { get; set; }

    // Bounded in-memory buffer between the decision hot path and the forwarding worker. The channel
    // uses FullMode=Wait, but the sink writes with the non-blocking TryWrite (which returns false
    // when the buffer is full and then drops+counts the event), so a slow or absent Audit.Service
    // can never block a decision.
    public int ChannelCapacity { get; set; } = 2048;

    // Per-request timeout for the forwarding HttpClient.
    public int HttpTimeoutSeconds { get; set; } = 10;
}
