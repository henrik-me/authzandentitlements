using AuthzEntitlements.Authz.Pdp.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MonoCloud.Cedar;
using MonoCloud.Cedar.Model;
using MonoCloud.Cedar.Model.Entity;
using MonoCloud.Cedar.Model.Policy;
using MonoCloud.Cedar.Value;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cedar;

// Engine adapter backed by a genuine in-process Cedar engine (MonoCloud.Cedar — a fork of
// cedar-policy/cedar-java with prebuilt native bindings). Unlike the RBAC-only Casbin/ASP.NET
// adapters, Cedar NATIVELY owns the FULL fintech authorization decision — coarse scope re-check,
// role eligibility, tenant isolation, subject-is-maker, pending status, and segregation of duties
// — exactly as the OPA/Rego adapter does out-of-process (LRN-026: let a richer engine express the
// whole decision rather than forcing the role-gate-only split). The embedded CedarPolicyModel
// answers the shared 22-scenario catalog identically to the reference provider: same Decision and
// same primary reason code. The maker-checker threshold obligation on a permitted transaction.create
// is computed adapter-side (the catalog does not check obligations, but the contract requires it).
//
// Fail-closed posture (mirrors OpaDecisionProvider): every failure to obtain a well-formed
// Permit/Deny — a Cedar evaluation error, a Failure response, a Deny whose determining forbids map
// to no known reason, or ANY thrown exception — returns Deny with the provider-local
// ProviderUnavailable reason. It never throws through to the caller and never falls through to a
// permit. The specific cause is LOGGED; the AccessDecision returned to (anonymous)
// /api/authz/evaluate callers carries only a stable, non-sensitive message so no internal detail
// leaks.
public sealed class CedarDecisionProvider : IAuthorizationDecisionProvider
{
    // Mirrors the reference ApprovalThreshold: at/above this a created transaction obliges a
    // second-person approval; below it, it may post immediately.
    private const decimal ApprovalThreshold = 10_000m;

    // Provider-local reason for an engine that fails to produce a usable decision. Deliberately NOT
    // added to the shared ReasonCodes: it maps to no Bank.Api rule and never appears in the parity
    // catalog. It exists only so an internal failure is a legible, machine-stable Deny (identical
    // posture to the OPA adapter's ProviderUnavailable).
    private const string ProviderUnavailable = "ProviderUnavailable";

    // Stable, non-sensitive message returned to callers on every fail-closed decision; the specific
    // cause is logged instead of surfaced so the anonymous evaluate endpoint cannot leak detail.
    private const string ProviderUnavailableMessage =
        "The Cedar authorization engine did not return a usable decision; failing closed.";

    // The known action vocabulary. An action outside it is denied UnknownAction adapter-side (mirrors
    // the reference's `_ => Deny(UnknownAction)` default) rather than expressed in Cedar, since
    // "action not in the known set" is awkward and non-total to encode as policy.
    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
    {
        ActionNames.AccountRead,
        ActionNames.AccountCreate,
        ActionNames.TransactionCreate,
        ActionNames.TransactionApprove,
        ActionNames.TransactionReject,
    };

    private static readonly EntityTypeName UserType = EntityTypeName.Parse("User")!;
    private static readonly EntityTypeName ActionType = EntityTypeName.Parse("Action")!;

    private readonly PolicySet _policySet;
    private readonly AuthorizationEngine _engine;
    private readonly ILogger<CedarDecisionProvider> _logger;

    // Optional logger so tests can `new CedarDecisionProvider()` while DI injects the real logger.
    // The PolicySet is parsed ONCE here (never per request), matching the Casbin adapter's model load.
    public CedarDecisionProvider(ILogger<CedarDecisionProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<CedarDecisionProvider>.Instance;
        _policySet = CedarPolicyModel.Build();
        _engine = new BasicAuthorizationEngine();
    }

    public string Name => "cedar";

    public AccessDecision Evaluate(AccessRequest request)
    {
        // Fail closed before touching Cedar: an action outside the known vocabulary is denied, never
        // permitted (parity with the reference engine's default arm).
        if (!KnownActions.Contains(request.Action.Name))
        {
            return AccessDecision.Deny(new Reason(
                ReasonCodes.UnknownAction,
                $"Action '{request.Action.Name}' is not a recognized bank action."));
        }

        try
        {
            var principal = BuildPrincipal(request.Subject);
            var resource = BuildResource(request.Resource);
            var action = new Entity(ActionType.Of(request.Action.Name));
            var context = BuildContext(request.Context);

            var authRequest = new AuthorizationRequest(principal, action, resource, context);
            var entities = new Entities(new HashSet<Entity> { principal, resource });

            var response = _engine.IsAuthorized(authRequest, _policySet, entities);
            return MapResponse(request, response);
        }
        catch (Exception ex)
        {
            // An authorization PDP must never throw through to the caller: any failure to obtain a
            // well-formed decision (a malformed/degenerate request, a native evaluation error, etc.)
            // is a fail-closed Deny, never a permit or an unhandled 500. The exception is passed to
            // the logger so stack traces/inner exceptions are available for diagnosing native failures.
            return FailClosed($"Cedar evaluation failed ({ex.GetType().Name}): {ex.Message}", ex);
        }
    }

    private static Entity BuildPrincipal(Subject subject)
    {
        var attributes = new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["tenant"] = new PrimString(NormalizeTenant(subject.Tenant)),
            ["subjectId"] = new PrimString(subject.Id),
            ["roles"] = new CedarList(subject.Roles.Select(role => (Value)new PrimString(role))),
        };

        return new Entity(UserType.Of(subject.Id), attributes, new HashSet<EntityUID>());
    }

    private static Entity BuildResource(Resource resource)
    {
        var attributes = new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["tenant"] = new PrimString(NormalizeTenant(resource.Tenant)),
            ["makerId"] = new PrimString(resource.MakerId ?? string.Empty),
            ["status"] = new PrimString(resource.Status ?? string.Empty),
            // A checked cast: a degenerate out-of-range amount overflows and fails closed rather than
            // silently wrapping to a bogus value.
            ["amount"] = new PrimLong(checked((long)(resource.Amount ?? 0m))),
        };

        var typeName = ResolveResourceType(resource.Type);
        return new Entity(typeName.Of(resource.Id ?? "resource"), attributes, new HashSet<EntityUID>());
    }

    // Fail-closed tenant normalization matching the reference's IsNullOrWhiteSpace semantics: a null
    // OR whitespace-only tenant collapses to "" so the tenant forbids (which require both sides be
    // non-empty AND equal) FIRE and deny TenantMismatch — never silently permit a blank-vs-blank pair.
    private static string NormalizeTenant(string? tenant) =>
        string.IsNullOrWhiteSpace(tenant) ? string.Empty : tenant;

    private static Context BuildContext(EvaluationContext context) =>
        new(new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["scopes"] = new CedarList(context.Scopes.Select(scope => (Value)new PrimString(scope))),
        });

    // Maps the AuthZEN resource type ("account", "transaction", "tenant", "branch") to a Cedar entity
    // type name (Account, Transaction, ...). An unresolvable type throws and fails closed.
    private static EntityTypeName ResolveResourceType(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            throw new ArgumentException("Resource type must be non-empty.", nameof(type));
        }

        var pascal = char.ToUpperInvariant(type[0]) + type[1..];
        return EntityTypeName.Parse(pascal)
            ?? throw new ArgumentException($"Resource type '{type}' is not a valid Cedar entity type.", nameof(type));
    }

    private AccessDecision MapResponse(AccessRequest request, AuthorizationResponse response)
    {
        if (response.Type != AuthorizationResponse.SuccessOrFailure.Success || response.Success is null)
        {
            var detail = response.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors)
                : "no diagnostics";
            return FailClosed($"Cedar returned a failure response ({detail}).");
        }

        var success = response.Success;
        if (success.IsAllowed())
        {
            return AccessDecision.Permit(PermitReason(), ObligationsFor(request));
        }

        // Deny: the determining set is the forbid ids that matched. Map to reason codes and pick the
        // FIRST-FAILING one — the lowest Precedence value in the reference order (OrderBy ascending +
        // First) — so a combined-failure input returns the reference's FIRST-failing reason (LRN-021),
        // not an arbitrary set member.
        var determining = success.GetReason()
            .Where(CedarPolicyModel.ForbidReasons.ContainsKey)
            .Select(id => CedarPolicyModel.ForbidReasons[id])
            .OrderBy(forbid => forbid.Precedence)
            .ToList();

        if (determining.Count == 0)
        {
            // A Deny whose determining forbids map to no known reason (or an implicit deny with no
            // matching permit) is incoherent for this total policy set — fail closed rather than
            // surface an unexplained deny.
            return FailClosed(
                $"Cedar denied with no mapped forbid reason (determining set: [{string.Join(", ", success.GetReason())}]).");
        }

        var code = determining[0].ReasonCode;
        return AccessDecision.Deny(new Reason(code, $"Cedar policy denied the request: {code}."));
    }

    // The catalog runner does not check obligations, but the contract requires the maker-checker
    // threshold obligation on a permitted transaction.create, matching the reference engine.
    private static Obligation[] ObligationsFor(AccessRequest request)
    {
        if (request.Action.Name != ActionNames.TransactionCreate)
        {
            return [];
        }

        var amount = request.Resource.Amount ?? 0m;
        var obligationId = amount >= ApprovalThreshold
            ? ObligationIds.RequireApproval
            : ObligationIds.PostImmediately;
        return [new Obligation(obligationId)];
    }

    private static Reason PermitReason() =>
        new(ReasonCodes.Permit, "Request satisfies all applicable Cedar policies.");

    // Log the specific cause for operators/telemetry; return only the stable, non-sensitive message
    // to the caller so no internal detail leaks through the anonymous evaluate endpoint. The optional
    // exception (from the catch-all) is passed to the logger so stack traces are captured, matching
    // the OPA/OpenFGA adapters.
    private AccessDecision FailClosed(string diagnostic, Exception? exception = null)
    {
        _logger.LogWarning(exception, "Cedar adapter failing closed: {Diagnostic}", diagnostic);
        return AccessDecision.Deny(new Reason(ProviderUnavailable, ProviderUnavailableMessage));
    }
}
