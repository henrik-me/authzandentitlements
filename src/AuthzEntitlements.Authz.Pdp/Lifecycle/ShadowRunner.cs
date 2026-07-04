using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle;

// Shadow / dual-run comparison harness (CS17): evaluate the SAME AccessRequest (or the whole
// scenario catalog) against a primary engine and one or more shadow engines and report where
// they diverge. This is the "run a candidate engine in shadow before trusting it" tool — the
// migration/rollout safety net and the head-to-head that proves engine parity on identical
// input. It resolves engines by name through the factory, so any registered provider can be
// shadowed against any other without changing calling code.
public sealed class ShadowRunner
{
    // The deterministic, in-process RBAC engine family — the engines that answer the fintech
    // RBAC catalog with no external dependency. This is the default shadow set when a caller
    // does not name one. OPA is excluded (it needs a live out-of-process server; it fails closed
    // to ProviderUnavailable otherwise) and OpenFGA is excluded (it is ReBAC — a different model
    // and catalog by design, so comparing it on RBAC input would diverge spuriously).
    public static readonly IReadOnlyList<string> DeterministicRbacFamily =
        ["reference", "aspnet", "casbin", "cedar"];

    private readonly AuthorizationDecisionProviderFactory _factory;

    public ShadowRunner(AuthorizationDecisionProviderFactory factory) => _factory = factory;

    // Single-request shadow run: evaluate one request against the primary and every shadow, and
    // report per-shadow agreement. The primary is evaluated once and reused across comparisons so
    // the comparison is stable and each engine is asked exactly once.
    public ShadowRunResult Run(
        string primaryEngine,
        IReadOnlyList<string> shadowEngines,
        AccessRequest request)
    {
        var primary = Flatten(_factory.GetProvider(primaryEngine), request);

        var comparisons = new List<ShadowComparison>(shadowEngines.Count);
        foreach (var shadowEngine in shadowEngines)
        {
            var shadow = Flatten(_factory.GetProvider(shadowEngine), request);
            var divergences = Diff(primary, shadow);
            comparisons.Add(new ShadowComparison(primary, shadow, divergences.Count == 0, divergences));
        }

        return new ShadowRunResult(primary, comparisons, comparisons.All(c => c.Agrees));
    }

    // Whole-catalog dual run: shadow one engine against another across every scenario and collect
    // only the divergences. An empty divergence list is the parity verdict a CI gate / migration
    // harness checks before trusting a swap. Engines are resolved once, not per scenario.
    public CatalogShadowReport RunCatalog(
        string primaryEngine,
        string shadowEngine,
        IReadOnlyList<AuthorizationScenario> scenarios)
    {
        var primaryProvider = _factory.GetProvider(primaryEngine);
        var shadowProvider = _factory.GetProvider(shadowEngine);

        var divergences = new List<ScenarioDivergence>();
        foreach (var scenario in scenarios)
        {
            var primary = Flatten(primaryProvider, scenario.Request);
            var shadow = Flatten(shadowProvider, scenario.Request);
            var diff = Diff(primary, shadow);
            if (diff.Count > 0)
            {
                divergences.Add(new ScenarioDivergence(scenario.Id, primary, shadow, diff));
            }
        }

        return new CatalogShadowReport(
            primaryProvider.Name,
            shadowProvider.Name,
            scenarios.Count,
            scenarios.Count - divergences.Count,
            divergences);
    }

    // Flatten a provider's decision to the fields a comparison keys on. Obligation ids are sorted
    // so the comparison is order-insensitive; the primary reason is Reasons[0] (the contract), or
    // empty if a provider returned no reason (which itself surfaces as a divergence).
    private static EngineDecision Flatten(IAuthorizationDecisionProvider provider, AccessRequest request)
    {
        var decision = provider.Evaluate(request);
        var reasonCode = decision.Reasons.Count > 0 ? decision.Reasons[0].Code : string.Empty;
        var obligationIds = decision.Obligations
            .Select(o => o.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        return new EngineDecision(provider.Name, decision.Decision, reasonCode, obligationIds);
    }

    // Produce human-readable divergence lines between two flattened decisions. An empty list means
    // the engines agree on decision, primary reason, AND obligations.
    private static IReadOnlyList<string> Diff(EngineDecision primary, EngineDecision shadow)
    {
        var divergences = new List<string>();

        if (primary.Decision != shadow.Decision)
        {
            divergences.Add($"decision: {primary.Decision} vs {shadow.Decision}");
        }

        if (!string.Equals(primary.ReasonCode, shadow.ReasonCode, StringComparison.Ordinal))
        {
            divergences.Add($"reason: '{primary.ReasonCode}' vs '{shadow.ReasonCode}'");
        }

        if (!primary.ObligationIds.SequenceEqual(shadow.ObligationIds, StringComparer.Ordinal))
        {
            divergences.Add(
                $"obligations: [{string.Join(",", primary.ObligationIds)}] vs " +
                $"[{string.Join(",", shadow.ObligationIds)}]");
        }

        return divergences;
    }
}
