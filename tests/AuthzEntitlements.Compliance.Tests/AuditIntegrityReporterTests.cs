using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// The audit-integrity evidence proves tamper-evidence produced by the shipped AuditHashChain: a
// valid chain verifies and every tamper is caught with the expected break.
public sealed class AuditIntegrityReporterTests
{
    private readonly AuditIntegritySection _section = AuditIntegrityReporter.Build();

    private AuditIntegrityCase Case(string scenarioFragment) =>
        _section.Cases.Single(c => c.Scenario.Contains(scenarioFragment, StringComparison.Ordinal));

    [Fact]
    public void BaselineChain_Verifies()
    {
        Assert.True(_section.BaselineChainValid);
        Assert.True(Case("Untampered").Valid);
        Assert.Null(Case("Untampered").BrokenAtSequence);
    }

    [Fact]
    public void ContentMutation_IsDetected()
    {
        var mutated = Case("Content field mutated");

        Assert.False(mutated.Valid);
        Assert.Equal(2, mutated.BrokenAtSequence);
        Assert.True(mutated.Passed);
    }

    [Fact]
    public void TailTruncationWithCheckpoint_IsDetected()
    {
        var truncated = Case("Tail row dropped");

        Assert.False(truncated.Valid);
        Assert.Equal(4, truncated.BrokenAtSequence);
        Assert.Contains("checkpoint", truncated.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonContiguousSequence_IsDetected()
    {
        var gapped = Case("Non-contiguous");

        Assert.False(gapped.Valid);
        Assert.Equal(2, gapped.BrokenAtSequence);
    }

    [Fact]
    public void PrevHashBreak_IsDetected()
    {
        var relinked = Case("Prev-hash linkage broken");

        Assert.False(relinked.Valid);
        Assert.Equal(3, relinked.BrokenAtSequence);
        Assert.Contains("Prev-hash", relinked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AllTamperDetected_AndEveryCasePasses()
    {
        Assert.Equal(4, _section.TamperCasesEvaluated);
        Assert.Equal(4, _section.TamperCasesDetected);
        Assert.True(_section.AllTamperDetected);
        Assert.All(_section.Cases, c => Assert.True(c.Passed, c.Scenario));
    }

    [Fact]
    public void Build_MapsRetentionAndTamperEvidenceControls()
    {
        Assert.Contains(_section.MappedControls, c => c.ControlId == "AUDIT-TAMPER-EVIDENCE");
        Assert.Contains(_section.MappedControls, c => c.ControlId == "AUDIT-RETENTION");
    }
}
