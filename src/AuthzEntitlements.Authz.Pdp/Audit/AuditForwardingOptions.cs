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

    // Bounded in-memory buffer between the decision hot path and the forwarding worker.
    // DropWrite when full so a slow/absent Audit.Service can never block a decision.
    public int ChannelCapacity { get; set; } = 2048;

    // Per-request timeout for the forwarding HttpClient.
    public int HttpTimeoutSeconds { get; set; } = 10;
}
