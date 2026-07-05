using System.Diagnostics.CodeAnalysis;
using AuthzEntitlements.Authz.Pdp.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Providers;

// Resolves the active IAuthorizationDecisionProvider by PdpOptions.Provider from the set
// of registered providers, matched case-insensitively on Name. This is the seam the
// adapter clickstops (CS06-CS09) plug into: register a new adapter as
// IAuthorizationDecisionProvider and select it via "Pdp:Provider". An unknown provider
// name fails closed with a clear error naming it and listing the available providers,
// rather than silently defaulting to some engine.
public sealed class AuthorizationDecisionProviderFactory
{
    private readonly IReadOnlyList<IAuthorizationDecisionProvider> _providers;
    private readonly string _configuredProvider;

    public AuthorizationDecisionProviderFactory(
        IEnumerable<IAuthorizationDecisionProvider> providers,
        IOptions<PdpOptions> options,
        ILoggerFactory? loggerFactory = null)
    {
        // Validate the ORIGINAL registered providers (names are unchanged by wrapping), then build the
        // resolvable set: any provider that does NOT declare ISupportsExtendedAuthorizationContext is
        // wrapped in the CS45 fail-closed ExtendedContextGuardProvider, so an on-behalf-of /
        // delegation / break-glass request can never be silently weakened by an engine that ignores it.
        // Capable providers (the reference, and any future context-aware engine) pass through
        // unwrapped. Every resolution path (GetActiveProvider / GetProvider / TryGetProvider) and
        // ProviderNames returns/uses this guarded set, so the enforced PdpDecisionService AND the
        // factory-resolved shadow / what-if / playground paths are all covered (Decision #2). The
        // wrapper re-exposes the inner Name, so selection, parity, and telemetry are unchanged.
        var registered = providers.ToList();
        ValidateProviderNames(registered);
        _providers = registered.Select(p => Guard(p, loggerFactory)).ToList();
        // Trim the configured name so accidental whitespace from env/secret sources
        // (e.g. "reference ") still selects the intended provider; a blank value falls back to
        // the default, and a non-blank unknown name still fails closed in GetActiveProvider.
        var configured = options.Value.Provider?.Trim();
        _configuredProvider = string.IsNullOrEmpty(configured)
            ? PdpOptions.DefaultProvider
            : configured;
    }

    // Wrap a provider in the fail-closed extended-context guard unless it natively supports the
    // CS19/CS21 extended-authorization context (declares ISupportsExtendedAuthorizationContext), in
    // which case it is trusted to honour OBO/delegation/break-glass itself and passes through unwrapped.
    // The per-provider ILogger is created from the injected factory when present (DI supplies it in the
    // running host); it falls back to no logger for the hand-constructed factories in tests/benchmarks.
    private static IAuthorizationDecisionProvider Guard(
        IAuthorizationDecisionProvider provider, ILoggerFactory? loggerFactory) =>
        provider is ISupportsExtendedAuthorizationContext
            ? provider
            : new ExtendedContextGuardProvider(
                provider, loggerFactory?.CreateLogger<ExtendedContextGuardProvider>());

    // Fail fast at construction if any registered provider has a blank name or two share a name
    // (case-insensitively). Selection matches on Name and returns the first match, so a blank or
    // duplicate name would make selection silently ambiguous — a real footgun once CS06-CS09
    // register adapters alongside the reference engine. Rejecting it here surfaces the
    // misconfiguration at startup rather than as a wrong-engine decision at runtime.
    private static void ValidateProviderNames(IReadOnlyList<IAuthorizationDecisionProvider> providers)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                throw new InvalidOperationException(
                    $"An IAuthorizationDecisionProvider of type '{provider.GetType().Name}' has a " +
                    "blank Name. Every provider must expose a stable, non-blank name for " +
                    "config-driven selection.");
            }

            if (!seen.Add(provider.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate IAuthorizationDecisionProvider name '{provider.Name}' " +
                    "(case-insensitive). Provider names must be unique so selection is unambiguous.");
            }
        }
    }

    public IAuthorizationDecisionProvider GetActiveProvider()
    {
        if (TryGetProvider(_configuredProvider, out var match))
        {
            return match;
        }

        throw new InvalidOperationException(
            $"No IAuthorizationDecisionProvider named '{_configuredProvider}' is registered. " +
            $"Available providers: [{AvailableProviders()}]. Set \"Pdp:Provider\" to one of these.");
    }

    // The names of every registered provider, so lifecycle tooling (shadow-run, what-if) can
    // enumerate and target engines by name rather than only the single configured active one.
    public IReadOnlyList<string> ProviderNames =>
        _providers.Select(p => p.Name).ToList();

    // Resolves a provider by name (case-insensitive) — the by-name analogue of GetActiveProvider
    // the shadow-run + what-if harness use to target a specific engine. Fails closed with a clear,
    // engine-naming error when no such provider is registered, never a silent wrong-engine result.
    public IAuthorizationDecisionProvider GetProvider(string name)
    {
        if (TryGetProvider(name, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"No IAuthorizationDecisionProvider named '{name}' is registered. " +
            $"Available providers: [{AvailableProviders()}].");
    }

    // Non-throwing lookup so a request-boundary caller (endpoint) can return a 400 for an unknown
    // engine name instead of surfacing a 500. The name is trimmed so accidental surrounding
    // whitespace (from env/secret/query sources) still resolves — consistent with the constructor
    // trimming PdpOptions.Provider. A blank name never matches (fail closed). The out is nullable +
    // [NotNullWhen(true)] so callers get a compiler warning if they read it without checking the bool.
    public bool TryGetProvider(string? name, [NotNullWhen(true)] out IAuthorizationDecisionProvider? provider)
    {
        var trimmed = name?.Trim();
        provider = string.IsNullOrEmpty(trimmed)
            ? null
            : _providers.FirstOrDefault(
                p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        return provider is not null;
    }

    private string AvailableProviders() =>
        _providers.Count == 0
            ? "(none registered)"
            : string.Join(", ", _providers.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
}
