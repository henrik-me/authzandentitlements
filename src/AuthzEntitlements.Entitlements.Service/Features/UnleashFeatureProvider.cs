using OpenFeature;
using OpenFeature.Model;
using Unleash;

namespace AuthzEntitlements.Entitlements.Service.Features;

// A minimal OpenFeature provider backed by the Unleash .NET client. Unleash exposes
// boolean toggles, so boolean resolution delegates to IUnleash.IsEnabled and every
// other value type falls back to its default. This is the config-gated alternative to
// the in-memory provider; it is only constructed when Entitlements:FeatureProvider is
// "Unleash" and therefore never on the default/tested code path.
public sealed class UnleashFeatureProvider(IUnleash unleash) : FeatureProvider
{
    private readonly IUnleash _unleash = unleash;

    public override Metadata GetMetadata() => new("unleash");

    public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
        string flagKey,
        bool defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<bool>(flagKey, _unleash.IsEnabled(flagKey, defaultValue)));

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
        string flagKey,
        string defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<string>(flagKey, defaultValue));

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
        string flagKey,
        int defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<int>(flagKey, defaultValue));

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
        string flagKey,
        double defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<double>(flagKey, defaultValue));

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
        string flagKey,
        Value defaultValue,
        EvaluationContext? context = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<Value>(flagKey, defaultValue));
}
