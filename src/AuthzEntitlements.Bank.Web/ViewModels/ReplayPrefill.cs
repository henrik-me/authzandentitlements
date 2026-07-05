using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Parses a CS36 audit request-snapshot (the canonical JSON of the original AccessRequest, produced
// by the PDP's RequestSnapshotSerializer) into a Playground form model for a FAITHFUL "Replay in
// Playground" pre-fill — recovering the ABAC inputs the CS15 best-effort replay could not (subject
// roles, context scopes, amount/maker/status, and a distinct resource tenant/branch).
//
// Kept pure and dependency-free so the parse is unit-testable offline. Defensive by design: a null,
// blank, or malformed snapshot returns null so the caller falls back to the CS15 best-effort
// pre-fill + banner. The snapshot is non-authoritative (not part of the tamper-evident hash), so it
// is only ever used to seed a what-if form — never to assert anything about the recorded decision.
public static class ReplayPrefill
{
    // The snapshot shares the camelCase wire shape of PdpAccessRequestDto (both mirror the PDP's
    // AccessRequest), so it deserializes straight onto that DTO with the shared client options.
    public static PlaygroundInput? FromSnapshot(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return null;
        }

        PdpAccessRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize<PdpAccessRequestDto>(snapshotJson, BankJson.Options);
        }
        catch (JsonException)
        {
            // Malformed snapshot: degrade gracefully to the best-effort replay path.
            return null;
        }

        if (request?.Subject is null || request.Action is null || request.Resource is null)
        {
            return null;
        }

        var subject = request.Subject;
        var resource = request.Resource;
        var actor = subject.Actor;

        return new PlaygroundInput
        {
            SubjectType = string.IsNullOrWhiteSpace(subject.Type) ? "user" : subject.Type,
            SubjectId = subject.Id ?? string.Empty,
            Roles = JoinCsv(subject.Roles),
            // The OBO delegate (if any) is reconstructed 1:1 so a delegated decision replays as the
            // SAME on-behalf-of request rather than collapsing to a direct/human call.
            ActorType = actor?.Type,
            ActorId = actor?.Id,
            ActorScopes = JoinCsv(actor?.Scopes),
            Tenant = subject.Tenant,
            // Subject and resource branch are recovered INDEPENDENTLY (the form models both), so a
            // cross-branch request round-trips faithfully instead of collapsing to one shared branch.
            Branch = subject.Branch,
            Action = request.Action.Name ?? string.Empty,
            ResourceType = resource.Type ?? string.Empty,
            ResourceId = resource.Id,
            ResourceTenant = resource.Tenant,
            ResourceBranch = resource.Branch,
            Amount = resource.Amount,
            MakerId = resource.MakerId,
            Status = resource.Status,
            Scopes = JoinCsv(request.Context?.Scopes),
        };
    }

    // Roles/scopes render back into the form's comma-separated inputs, which PlaygroundInput splits
    // again on submit — an exact round-trip for the array inputs.
    private static string JoinCsv(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? string.Join(", ", values) : string.Empty;
}
