using System.Text.Json;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle.AuthZen;

// AuthZEN Authorization API 1.0 "Access Evaluation" request/response contracts (CS17). This is
// the exact wire shape the OpenID AuthZEN spec defines: subject/action/resource each carry a
// type/id (or name) plus a free-form `properties` bag, and the response is a boolean `decision`
// plus an optional free-form `context`. The fintech attributes the PDP needs (roles, tenant,
// branch, amount, maker_id, status, scopes) travel inside those property bags, so the same PDP
// speaks native AuthZEN over the wire. Properties stay raw JsonElement so heterogeneous values
// (string arrays, strings, numbers) survive deserialization for AuthZenMapper to read.

public sealed record AuthZenSubject(
    string Type,
    string Id,
    Dictionary<string, JsonElement>? Properties = null);

public sealed record AuthZenAction(
    string Name,
    Dictionary<string, JsonElement>? Properties = null);

public sealed record AuthZenResource(
    string Type,
    string? Id = null,
    Dictionary<string, JsonElement>? Properties = null);

public sealed record AuthZenEvaluationRequest(
    AuthZenSubject Subject,
    AuthZenAction Action,
    AuthZenResource Resource,
    Dictionary<string, JsonElement>? Context = null);

// The AuthZEN Access Evaluation response: `decision` (boolean, required) plus an optional
// free-form `context`. We populate context with the self-explaining primary reason code, the
// reason messages, and any obligations, so the AuthZEN surface carries the same explainability
// the native /evaluate contract does.
public sealed record AuthZenEvaluationResponse(
    bool Decision,
    AuthZenDecisionContext? Context = null);

public sealed record AuthZenDecisionContext(
    string ReasonCode,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Obligations);
