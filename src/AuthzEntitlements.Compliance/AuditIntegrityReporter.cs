using AuthzEntitlements.Audit.Service.Contracts;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;

namespace AuthzEntitlements.Compliance;

// Produces the audit-integrity evidence section, deterministically and DB-free, using the system's
// own pure AuditHashChain. It folds a small in-memory chain, verifies it (expects Valid), then
// applies each tamper the append-only log MUST detect — a content mutation, a tail truncation
// caught by a trusted checkpoint, a sequence gap, and a broken prev-hash link — so the report proves
// tamper-evidence produced by the shipped hash-chain code, not by a bespoke re-implementation.
public static class AuditIntegrityReporter
{
    // A fixed base instant so the sample chain (and its hashes) are deterministic across runs.
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static AuditIntegritySection Build()
    {
        var baseline = BuildBaselineChain();
        var cases = new List<AuditIntegrityCase>
        {
            RunCase("Untampered chain verifies", expectedDetected: false, baseline),
        };

        // (a) Mutate a content field of a middle row: the recomputed row hash no longer matches.
        var mutated = CloneChain(baseline);
        mutated[1].Reason = "tampered-reason";
        cases.Add(RunCase("Content field mutated on a middle row", expectedDetected: true, mutated));

        // (b) Drop the tail row but supply a trusted checkpoint at the original tail: the chain no
        // longer reaches the checkpoint sequence, so the anchor catches the truncation.
        var tail = baseline[^1];
        var checkpoint = new AuditCheckpoint(tail.Sequence, tail.RowHash);
        var truncated = CloneChain(baseline);
        truncated.RemoveAt(truncated.Count - 1);
        cases.Add(RunCase(
            "Tail row dropped (trusted checkpoint at original tail)",
            expectedDetected: true,
            truncated,
            checkpoint));

        // (c) Remove a middle row: the sequence is no longer contiguous.
        var gapped = CloneChain(baseline);
        gapped.RemoveAt(1);
        cases.Add(RunCase(
            "Non-contiguous sequence (a middle row removed)", expectedDetected: true, gapped));

        // (d) Break the prev-hash linkage on a row: it no longer chains to the previous row hash.
        var relinked = CloneChain(baseline);
        relinked[2].PrevHash = AuditHashChain.GenesisPrevHash;
        cases.Add(RunCase(
            "Prev-hash linkage broken on a row", expectedDetected: true, relinked));

        var tamperCases = cases.Where(c => c.ExpectedDetected).ToList();
        var detected = tamperCases.Count(c => !c.Valid);

        return new AuditIntegritySection(
            ChainLength: baseline.Count,
            BaselineChainValid: cases[0].Valid,
            TamperCasesEvaluated: tamperCases.Count,
            TamperCasesDetected: detected,
            AllTamperDetected: detected == tamperCases.Count,
            Cases: cases,
            MappedControls: BuildMappedControls());
    }

    private static AuditIntegrityCase RunCase(
        string scenario,
        bool expectedDetected,
        IReadOnlyList<AuditEntry> chain,
        AuditCheckpoint? checkpoint = null)
    {
        var result = AuditHashChain.Verify(chain, checkpoint);
        var passed = expectedDetected ? !result.Valid : result.Valid;
        return new AuditIntegrityCase(
            Scenario: scenario,
            ExpectedDetected: expectedDetected,
            Valid: result.Valid,
            BrokenAtSequence: result.BrokenAtSequence,
            Reason: result.Reason,
            Passed: passed);
    }

    // Folds a deterministic sample chain via AuditHashChain.Append so every row's hash binds its
    // content and the previous row — exactly as the Audit.Service ingest path builds it.
    private static List<AuditEntry> BuildBaselineChain()
    {
        var payloads = new[]
        {
            NewPayload(0, "trace-0001", "Permit", "Permit", "acct-1001"),
            NewPayload(1, "trace-0002", "Deny", "SodConflict", "grant-2002"),
            NewPayload(2, "trace-0003", "Permit", "Permit", "txn-3003"),
            NewPayload(3, "trace-0004", "Deny", "MakerEqualsChecker", "txn-3003"),
        };

        long sequence = 0;
        var previousRowHash = AuditHashChain.GenesisPrevHash;
        var entries = new List<AuditEntry>(payloads.Length);
        foreach (var payload in payloads)
        {
            var (nextSequence, prevHash, rowHash) =
                AuditHashChain.Append(sequence, previousRowHash, payload);
            entries.Add(ToEntry(nextSequence, prevHash, rowHash, payload));
            sequence = nextSequence;
            previousRowHash = rowHash;
        }

        return entries;
    }

    private static AuditPayload NewPayload(
        int offsetMinutes, string traceId, string decision, string reason, string resourceId) =>
        new(
            TimestampUtc: BaseTime.AddMinutes(offsetMinutes),
            TraceId: traceId,
            Provider: "reference",
            SubjectId: "user-teller1",
            Action: "governance.access.request",
            ResourceType: "governance",
            ResourceId: resourceId,
            Decision: decision,
            Reason: reason,
            Tenant: "CONTOSO",
            Producer: "pdp");

    private static AuditEntry ToEntry(long sequence, string prevHash, string rowHash, AuditPayload p) =>
        new()
        {
            Sequence = sequence,
            TimestampUtc = p.TimestampUtc,
            TraceId = p.TraceId,
            Provider = p.Provider,
            SubjectId = p.SubjectId,
            Action = p.Action,
            ResourceType = p.ResourceType,
            ResourceId = p.ResourceId,
            Decision = p.Decision,
            Reason = p.Reason,
            Tenant = p.Tenant,
            Producer = p.Producer,
            PrevHash = prevHash,
            RowHash = rowHash,
            ReceivedAtUtc = p.TimestampUtc,
        };

    // A field-wise copy so a tamper scenario mutates its own chain without disturbing the baseline.
    private static List<AuditEntry> CloneChain(IReadOnlyList<AuditEntry> chain) =>
        [.. chain.Select(e => new AuditEntry
        {
            Sequence = e.Sequence,
            TimestampUtc = e.TimestampUtc,
            TraceId = e.TraceId,
            Provider = e.Provider,
            SubjectId = e.SubjectId,
            Action = e.Action,
            ResourceType = e.ResourceType,
            ResourceId = e.ResourceId,
            Decision = e.Decision,
            Reason = e.Reason,
            Tenant = e.Tenant,
            Producer = e.Producer,
            PrevHash = e.PrevHash,
            RowHash = e.RowHash,
            ReceivedAtUtc = e.ReceivedAtUtc,
        })];

    private static IReadOnlyList<MappedControl> BuildMappedControls() =>
    [
        new MappedControl(
            ControlId: "AUDIT-TAMPER-EVIDENCE",
            Name: "Append-only, tamper-evident audit log",
            Framework: "SOX / PCI-DSS / GDPR",
            EnforcementPoint:
                "AuditHashChain.ComputeRowHash / Verify (SHA-256 row chain + trusted checkpoint)",
            Evidence:
                "Any content mutation, tail truncation, sequence gap, or broken prev-hash link is " +
                "detected by Verify; a trusted checkpoint closes the truncation/full-rewrite gap."),
        new MappedControl(
            ControlId: "AUDIT-RETENTION",
            Name: "Immutable audit retention",
            Framework: "SOX / PCI-DSS",
            EnforcementPoint: "Audit.Service append-only chain (never-updated rows, monotonic sequence)",
            Evidence:
                "Rows are only appended (never updated/deleted); the hash chain makes any retroactive " +
                "edit detectable, evidencing retention integrity."),
    ];
}
