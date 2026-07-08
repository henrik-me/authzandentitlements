using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace AuthzEntitlements.E2E.Tests;

/// <summary>
/// Shared setup for the full-stack e2e boots.
///
/// <para>Creates the <see cref="DistributedApplicationTestingBuilder"/> for the AppHost and
/// makes the Postgres server <em>ephemeral</em> for the test run by removing the persistent
/// data-volume mount that <c>AddPostgres(...).WithDataVolume()</c> declares in
/// <c>AppHost.cs</c> (that volume exists for <c>aspire run</c> convenience — keeping bank data
/// across dev restarts — and is irrelevant to an automated e2e).</para>
///
/// <para><b>Why this matters.</b> Each e2e boots the real stack; on a timeout/cancellation or a
/// hard kill of the test host, Postgres can be force-terminated mid-write. With a <em>reused</em>
/// named data volume that leaves a corrupted write-ahead log, and the <em>next</em> run's
/// Postgres then PANICs on startup (<c>could not locate a valid checkpoint record</c>) and
/// exits — so the five Postgres-dependent services never reach Healthy, <c>StartAsync</c> blocks
/// to the timeout cap, and every stateful e2e fails with an opaque
/// <see cref="System.Threading.Tasks.TaskCanceledException"/>. An ephemeral database cannot
/// carry corruption between runs and gives each run a clean, known starting state (the tests
/// already use unique per-run identifiers and lower-bound / seeded-present assertions).</para>
///
/// <para>Only Postgres is made ephemeral; the <c>observability</c> collector's persistent
/// <c>/data</c> volume (CS61) is deliberately left intact.</para>
/// </summary>
internal static class E2EStack
{
    private const string PostgresResourceName = "postgres";

    /// <summary>
    /// Creates the testing builder for the AppHost with Postgres made ephemeral (no persistent
    /// data volume). Configure DCP/port settings on the returned builder before
    /// <c>BuildAsync</c> exactly as before.
    /// </summary>
    internal static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AuthzEntitlements_AppHost>(cancellationToken);

        MakePostgresEphemeral(builder);
        return builder;
    }

    /// <summary>
    /// Removes the single persistent data-volume mount from the <c>postgres</c> resource so the
    /// e2e boots against a fresh anonymous volume each run.
    ///
    /// <para><b>Fail-closed.</b> Throws if the <c>postgres</c> resource is absent, or if it does
    /// not carry <em>exactly one</em> volume-type <see cref="ContainerMountAnnotation"/>. A future
    /// AppHost/Aspire change that renames the resource or adds a second Postgres volume therefore
    /// surfaces loudly here instead of the strip silently doing the wrong thing (leaving a
    /// persistent volume mounted, or removing an unintended mount).</para>
    /// </summary>
    private static void MakePostgresEphemeral(IDistributedApplicationTestingBuilder builder)
    {
        var postgres = builder.Resources.FirstOrDefault(
            r => string.Equals(r.Name, PostgresResourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"E2E setup expected a '{PostgresResourceName}' resource in the AppHost model but " +
                "found none, so the ephemeral-Postgres strip cannot be applied. Update E2EStack " +
                "if the resource was renamed.");

        var dataVolumes = postgres.Annotations
            .OfType<ContainerMountAnnotation>()
            .Where(mount => mount.Type == ContainerMountType.Volume)
            .ToList();

        if (dataVolumes.Count != 1)
        {
            throw new InvalidOperationException(
                $"E2E setup expected exactly one volume-type mount on the '{PostgresResourceName}' " +
                $"resource (the WithDataVolume() data volume) but found {dataVolumes.Count}. " +
                "Refusing to strip an ambiguous mount set — update E2EStack to match the current " +
                "AppHost Postgres volume configuration.");
        }

        postgres.Annotations.Remove(dataVolumes[0]);
    }
}
