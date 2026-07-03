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
        _configuredProvider = options.Value.Provider;
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
