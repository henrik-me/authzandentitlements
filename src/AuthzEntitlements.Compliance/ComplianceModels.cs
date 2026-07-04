using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthzEntitlements.Compliance;

// The serializable shape of a compliance evidence report and its four sections. These records are
// the on-disk contract (persisted by ComplianceReportStore, rendered by MarkdownRenderer), so field
// names and the schema version are stable: bump SchemaVersion on any breaking shape change.

// A regulatory control the lab enforces, mapped to its enforcement point and the evidence that
// demonstrates it. Framework is a free-form list of the frameworks the control satisfies
// (e.g. "SOX / PCI-DSS / GDPR").
public sealed record MappedControl(
    string ControlId,
    string Name,
    string Framework,
    string EnforcementPoint,
    string Evidence);

// One segregation-of-duties evidence case: a proposed role set driven through both the pure
// GovernanceSodPolicy and the in-process ReferenceDecisionProvider on governance.access.request.
// Passed is true when detection and the reference decision match the expectation for the case.
public sealed record SodEvidenceCase(
    string Scenario,
    IReadOnlyList<string> ProposedRoles,
    bool ConflictDetected,
    string? DetectedPairFirst,
    string? DetectedPairSecond,
    string Decision,
    string ReasonCode,
    bool ExpectedConflict,
    bool Passed);

public sealed record SodEvidenceSection(
    int IncompatiblePairCount,
    int CasesEvaluated,
    int ConflictsDetected,
    bool AllToxicCombinationsDenied,
    bool AllCleanSetsPermitted,
    IReadOnlyList<SodEvidenceCase> Cases,
    IReadOnlyList<MappedControl> MappedControls);

// One audit-integrity evidence case: a chain scenario verified by the pure AuditHashChain.
// ExpectedDetected is true for a tamper scenario that MUST be caught; Passed is true when the
// actual verification outcome matches the expectation.
public sealed record AuditIntegrityCase(
    string Scenario,
    bool ExpectedDetected,
    bool Valid,
    long? BrokenAtSequence,
    string? Reason,
    bool Passed);

public sealed record AuditIntegritySection(
    int ChainLength,
    bool BaselineChainValid,
    int TamperCasesEvaluated,
    int TamperCasesDetected,
    bool AllTamperDetected,
    IReadOnlyList<AuditIntegrityCase> Cases,
    IReadOnlyList<MappedControl> MappedControls);

// A summary of one access-review (recertification) campaign collected from a live Governance
// service. Counts partition the campaign's items by their recertification decision.
public sealed record CampaignSummary(
    string Id,
    string Name,
    string TenantCode,
    string Status,
    int TotalItems,
    int Certified,
    int Revoked,
    int Pending);

// The access-certification section. Collected is false when the Governance service was not
// supplied or was unreachable (a self-skip); Reason then explains why and ReproductionCommand is
// the exact command to collect it live.
public sealed record CertificationSection(
    bool Collected,
    string? Reason,
    string ReproductionCommand,
    IReadOnlyList<CampaignSummary> Campaigns);

public sealed record AccessPackageSummary(
    string Code,
    string DisplayName,
    bool RequiresApproval,
    int DefaultDurationMinutes,
    IReadOnlyList<string> Roles);

// A single grant attestation for a probed principal: its lifecycle status (active/expired/revoked)
// and whether it is currently active, evidencing time-bound (JIT) least privilege.
public sealed record GrantAttestation(
    string Id,
    string PrincipalId,
    string AccessPackageCode,
    string Status,
    bool Active,
    string GrantedAt,
    string ExpiresAt);

public sealed record LeastPrivilegeSection(
    bool Collected,
    string? Reason,
    string ReproductionCommand,
    string? ProbedPrincipalId,
    IReadOnlyList<AccessPackageSummary> AccessPackages,
    IReadOnlyList<GrantAttestation> Grants);

// A whole compliance evidence report: provenance metadata plus the four evidence sections. The two
// deterministic sections (Sod, AuditIntegrity) are always populated; the two live sections
// (Certification, LeastPrivilege) self-skip when the Governance service is offline.
public sealed record ComplianceReport(
    int SchemaVersion,
    string GeneratedAtUtc,
    string GitSha,
    SodEvidenceSection Sod,
    AuditIntegritySection AuditIntegrity,
    CertificationSection Certification,
    LeastPrivilegeSection LeastPrivilege)
{
    // The current on-disk schema version. Bump when the persisted shape changes incompatibly.
    public const int CurrentSchemaVersion = 1;
}

// Shared System.Text.Json configuration for every compliance artefact: camelCase property names,
// indented output, and enum-as-string (defensive — the model uses string status constants today).
public static class ComplianceJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    // Built once and frozen (MakeReadOnly) so this shared instance — which defines the on-disk
    // contract (Web camelCase, indented, enum-as-string) — cannot be mutated by any caller and
    // silently break persistence/round-tripping.
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
