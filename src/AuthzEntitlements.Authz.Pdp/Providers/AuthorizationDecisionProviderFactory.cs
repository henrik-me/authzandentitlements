using AuthzEntitlements.Authz.Pdp.Contracts;
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
        IOptions<PdpOptions> options)
    {
        _providers = providers.ToList();
        ValidateProviderNames(_providers);
        _configuredProvider = string.IsNullOrWhiteSpace(options.Value.Provider)
            ? PdpOptions.DefaultProvider
            : options.Value.Provider;
    }

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
        var match = _providers.FirstOrDefault(
            p => string.Equals(p.Name, _configuredProvider, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var available = _providers.Count == 0
            ? "(none registered)"
            : string.Join(", ", _providers.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        throw new InvalidOperationException(
            $"No IAuthorizationDecisionProvider named '{_configuredProvider}' is registered. " +
            $"Available providers: [{available}]. Set \"Pdp:Provider\" to one of these.");
    }
}
