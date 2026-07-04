using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle;

// One golden (known-good, reviewed) decision for a catalog scenario: the decision, the primary
// reason code, and the obligation ids. This is the drift-detection baseline — richer than the
// catalog's Expected/ExpectedReasonCode because it also pins the obligations an engine must
// attach, which the parity catalog does not assert.
public sealed record GoldenDecision(
    string ScenarioId,
    Decision Decision,
    string ReasonCode,
    IReadOnlyList<string> ObligationIds);

// The committed golden-decision snapshot (CS17): the trusted, reviewed decision for every
// scenario in the FintechScenarioCatalog, treated as policy-as-code source of truth. Drift
// detection compares a live engine's Compute(...) output to Golden; any difference is drift a
// CI gate fails on. The snapshot is authored INDEPENDENTLY of the catalog's own Expected fields
// (and of any single engine) on purpose: if a rule change silently moves a decision, obligation,
// or reason, the diff catches it here even if the catalog expectations were changed in lock-step.
// Regeneration is deliberate and reviewed — see docs/authz/policy-lifecycle.md.
public static class GoldenDecisionSnapshot
{
    private static readonly string[] None = [];

    public static IReadOnlyList<GoldenDecision> Golden { get; } =
    [
        new("read-own-tenant-account", Decision.Permit, ReasonCodes.Permit, None),
        new("read-other-tenant-account", Decision.Deny, ReasonCodes.TenantMismatch, None),
        new("auditor-reads-own-tenant", Decision.Permit, ReasonCodes.Permit, None),
        new("teller-create-account", Decision.Deny, ReasonCodes.RoleNotAuthorized, None),
        new("manager-create-account-own-tenant", Decision.Permit, ReasonCodes.Permit, None),
        new("manager-create-account-other-tenant", Decision.Deny, ReasonCodes.TenantMismatch, None),
        new("teller-create-small-txn", Decision.Permit, ReasonCodes.Permit, [ObligationIds.PostImmediately]),
        new("teller-create-large-txn", Decision.Permit, ReasonCodes.Permit, [ObligationIds.RequireApproval]),
        new("manager-approve-pending", Decision.Permit, ReasonCodes.Permit, None),
        new("teller-approve-not-eligible", Decision.Deny, ReasonCodes.RoleNotAuthorized, None),
        new("manager-approve-own-txn-sod", Decision.Deny, ReasonCodes.MakerEqualsChecker, None),
        new("compliance-reject-pending", Decision.Permit, ReasonCodes.Permit, None),
        new("manager-approve-already-approved", Decision.Deny, ReasonCodes.NotPending, None),
        new("manager-approve-other-tenant-txn", Decision.Deny, ReasonCodes.TenantMismatch, None),
        new("fabrikam-teller-reads-contoso", Decision.Deny, ReasonCodes.TenantMismatch, None),
        new("auditor-create-txn", Decision.Deny, ReasonCodes.RoleNotAuthorized, None),
        new("teller-create-txn-no-scope", Decision.Deny, ReasonCodes.MissingScope, None),
        new("teller-read-no-scope", Decision.Deny, ReasonCodes.MissingScope, None),
        new("compliance-approve-own-txn-sod", Decision.Deny, ReasonCodes.MakerEqualsChecker, None),
        new("teller-create-txn-for-other-maker", Decision.Deny, ReasonCodes.SubjectNotMaker, None),
        new("teller-create-threshold-boundary", Decision.Permit, ReasonCodes.Permit, [ObligationIds.RequireApproval]),
        new("unknown-action-fails-closed", Decision.Deny, ReasonCodes.UnknownAction, None),
    ];

    // A stable content hash of the golden snapshot — the "policy version" id. It changes only when
    // the golden baseline itself changes, so callers can pin/compare policy versions and observe
    // that the enforced baseline moved (the rollout/rollback + drift-observability anchor).
    public static string Version { get; } = ComputeVersion(Golden);

    private static string ComputeVersion(IReadOnlyList<GoldenDecision> golden)
    {
        // Canonicalize (sort entries by id, and obligation ids within each entry) so the hash is a
        // true CONTENT hash — reordering the Golden list or an entry's obligations must not change
        // the version.
        var canonical = string.Join(
            "\n",
            golden
                .OrderBy(g => g.ScenarioId, StringComparer.Ordinal)
                .Select(g =>
                    $"{g.ScenarioId}|{g.Decision}|{g.ReasonCode}|" +
                    $"{string.Join(",", g.ObligationIds.OrderBy(id => id, StringComparer.Ordinal))}"));
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Run a provider over the scenarios and produce the same shape as Golden, so drift detection
    // can compare a live engine to the committed baseline. Obligation ids are sorted so the
    // comparison is order-insensitive.
    public static IReadOnlyList<GoldenDecision> Compute(
        IAuthorizationDecisionProvider provider,
        IReadOnlyList<AuthorizationScenario> scenarios)
    {
        var computed = new List<GoldenDecision>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var decision = provider.Evaluate(scenario.Request);
            var reasonCode = decision.Reasons.Count > 0 ? decision.Reasons[0].Code : string.Empty;
            var obligationIds = decision.Obligations
                .Select(o => o.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            computed.Add(new GoldenDecision(scenario.Id, decision.Decision, reasonCode, obligationIds));
        }

        return computed;
    }

    // Diff a computed snapshot against the golden baseline by scenario id, returning human-readable
    // drift lines (empty = no drift). Missing or extra scenario ids are reported too, so a catalog
    // that adds/removes a scenario without updating the golden is caught rather than silently passing.
    public static IReadOnlyList<string> Diff(
        IReadOnlyList<GoldenDecision> golden,
        IReadOnlyList<GoldenDecision> current)
    {
        var drift = new List<string>();
        var goldenById = golden.ToDictionary(g => g.ScenarioId, StringComparer.Ordinal);
        var currentById = current.ToDictionary(c => c.ScenarioId, StringComparer.Ordinal);

        foreach (var id in goldenById.Keys.Where(k => !currentById.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            drift.Add($"{id}: present in golden but missing from current snapshot.");
        }

        foreach (var id in currentById.Keys.Where(k => !goldenById.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            drift.Add($"{id}: present in current snapshot but missing from golden.");
        }

        foreach (var (id, expected) in goldenById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!currentById.TryGetValue(id, out var actual))
            {
                continue;
            }

            if (expected.Decision != actual.Decision)
            {
                drift.Add($"{id}: decision {expected.Decision} -> {actual.Decision}.");
            }

            if (!string.Equals(expected.ReasonCode, actual.ReasonCode, StringComparison.Ordinal))
            {
                drift.Add($"{id}: reason '{expected.ReasonCode}' -> '{actual.ReasonCode}'.");
            }

            if (!expected.ObligationIds.SequenceEqual(actual.ObligationIds, StringComparer.Ordinal))
            {
                drift.Add(
                    $"{id}: obligations [{string.Join(",", expected.ObligationIds)}] -> " +
                    $"[{string.Join(",", actual.ObligationIds)}].");
            }
        }

        return drift;
    }
}
