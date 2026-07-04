using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Sod;

namespace AuthzEntitlements.Compliance;

// Produces the segregation-of-duties (SoD) evidence section, deterministically and DB-free. It
// drives the pure GovernanceSodPolicy over every incompatible pair plus representative
// clean/single/empty role sets, AND drives the in-process ReferenceDecisionProvider on
// governance.access.request so the report proves the SYSTEM detects and denies every toxic role
// combination and permits every independent one — not just that a local helper agrees.
public static class SodEvidenceReporter
{
    // The five incompatible pairs GovernanceSodPolicy enforces, in its declared order. Kept here as
    // the case fixtures so the report enumerates each toxic combination explicitly.
    private static readonly (string First, string Second)[] IncompatiblePairs =
    [
        (RoleNames.Teller, RoleNames.BranchManager),
        (RoleNames.Teller, RoleNames.ComplianceOfficer),
        (RoleNames.Auditor, RoleNames.Teller),
        (RoleNames.Auditor, RoleNames.BranchManager),
        (RoleNames.Auditor, RoleNames.ComplianceOfficer),
    ];

    public static SodEvidenceSection Build()
    {
        var provider = new ReferenceDecisionProvider();
        var cases = new List<SodEvidenceCase>();

        // Every toxic pair MUST be detected and denied.
        foreach (var (first, second) in IncompatiblePairs)
        {
            cases.Add(Evaluate(
                provider,
                $"Incompatible pair: {first} + {second}",
                [first, second],
                expectedConflict: true));
        }

        // A superset that contains a toxic pair is still detected.
        cases.Add(Evaluate(
            provider,
            "Superset containing a toxic pair (Teller + BranchManager + ComplianceOfficer)",
            [RoleNames.Teller, RoleNames.BranchManager, RoleNames.ComplianceOfficer],
            expectedConflict: true));

        // Two oversight roles together are explicitly allowed (a clean set).
        cases.Add(Evaluate(
            provider,
            "Clean oversight pair (BranchManager + ComplianceOfficer)",
            [RoleNames.BranchManager, RoleNames.ComplianceOfficer],
            expectedConflict: false));

        // A single role never conflicts.
        cases.Add(Evaluate(
            provider,
            "Single role (Teller)",
            [RoleNames.Teller],
            expectedConflict: false));

        // An empty proposed set never conflicts.
        cases.Add(Evaluate(
            provider,
            "Empty proposed role set",
            [],
            expectedConflict: false));

        var conflicts = cases.Count(c => c.ConflictDetected);
        var toxicDenied = cases
            .Where(c => c.ExpectedConflict)
            .All(c => c.ConflictDetected
                && string.Equals(c.Decision, nameof(Decision.Deny), StringComparison.Ordinal)
                && string.Equals(c.ReasonCode, ReasonCodes.SodConflict, StringComparison.Ordinal));
        var cleanPermitted = cases
            .Where(c => !c.ExpectedConflict)
            .All(c => !c.ConflictDetected
                && string.Equals(c.Decision, nameof(Decision.Permit), StringComparison.Ordinal));

        return new SodEvidenceSection(
            IncompatiblePairCount: IncompatiblePairs.Length,
            CasesEvaluated: cases.Count,
            ConflictsDetected: conflicts,
            AllToxicCombinationsDenied: toxicDenied,
            AllCleanSetsPermitted: cleanPermitted,
            Cases: cases,
            MappedControls: BuildMappedControls());
    }

    // Runs one role set through the pure policy AND the reference engine, recording both verdicts.
    private static SodEvidenceCase Evaluate(
        ReferenceDecisionProvider provider,
        string scenario,
        IReadOnlyList<string> roles,
        bool expectedConflict)
    {
        var pair = GovernanceSodPolicy.FindConflict(roles);
        var conflictDetected = pair is not null;

        var request = new AccessRequest(
            new Subject("user", "compliance-probe", roles),
            new ActionRequest(ActionNames.GovernanceAccessRequest),
            new Resource("governance"),
            new EvaluationContext([]));
        var decision = provider.Evaluate(request);
        var reasonCode = decision.Reasons[0].Code;

        var decisionMatches = expectedConflict
            ? decision.Decision == Decision.Deny
                && string.Equals(reasonCode, ReasonCodes.SodConflict, StringComparison.Ordinal)
            : decision.Decision == Decision.Permit;

        return new SodEvidenceCase(
            Scenario: scenario,
            ProposedRoles: roles,
            ConflictDetected: conflictDetected,
            DetectedPairFirst: pair?.First,
            DetectedPairSecond: pair?.Second,
            Decision: decision.Decision.ToString(),
            ReasonCode: reasonCode,
            ExpectedConflict: expectedConflict,
            Passed: conflictDetected == expectedConflict && decisionMatches);
    }

    // The mapped SoD controls this evidence demonstrates, including the Bank.Api maker-checker
    // control (checker != maker; the 10,000 approval threshold) with its enforcement point cited to
    // the ReferenceDecisionProvider constants.
    private static IReadOnlyList<MappedControl> BuildMappedControls() =>
    [
        new MappedControl(
            ControlId: "SOD-ROLE-INCOMPATIBILITY",
            Name: "Toxic role-combination prevention (segregation of duties)",
            Framework: "SOX / PCI-DSS / GDPR",
            EnforcementPoint:
                "GovernanceSodPolicy.FindConflict + ReferenceDecisionProvider on " +
                "governance.access.request (Deny + ReasonCodes.SodConflict)",
            Evidence:
                "Every incompatible role pair is detected and denied; independent role sets permit."),
        new MappedControl(
            ControlId: "SOD-MAKER-CHECKER",
            Name: "Maker-checker approval for high-value transactions",
            Framework: "SOX / PCI-DSS",
            EnforcementPoint:
                "ReferenceDecisionProvider.EvaluateApprovalDecision " +
                "(bank.transaction.approve / bank.transaction.reject)",
            Evidence:
                "Checker must differ from maker (ReasonCodes.MakerEqualsChecker); a created " +
                "transaction at/above the 10,000 threshold is obliged to a second-person approval " +
                "(ObligationIds.RequireApproval), below it posts immediately."),
    ];
}
