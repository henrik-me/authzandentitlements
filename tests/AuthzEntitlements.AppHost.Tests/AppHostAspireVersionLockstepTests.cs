using System.Xml.Linq;
using Xunit;

namespace AuthzEntitlements.AppHost.Tests;

/// <summary>
/// Guards the Aspire version-lockstep invariant. The Aspire.AppHost.Sdk version lives in the
/// AppHost csproj's <c>&lt;Sdk&gt;</c> attribute (the build SDK that also delivers the DCP
/// orchestrator), while the <c>Aspire.Hosting.*</c> packages are pinned in
/// Directory.Packages.props. They MUST share the same major.minor.patch: if they drift — e.g. a
/// Dependabot bump of a single Aspire.Hosting.* package (PR #105 bumped only
/// Aspire.Hosting.PostgreSQL to 13.4.6 while the SDK stayed 13.1.0) — the runtime DCP version
/// check throws at <c>aspire run</c> ("Newer version of the Aspire.Hosting.AppHost package is
/// required"). Plain <c>dotnet build</c>/<c>dotnet test</c> never boots the orchestrator, so this
/// static guard is what makes the drift fail CI (via <c>dotnet test</c>, no workflow change).
/// </summary>
public class AppHostAspireVersionLockstepTests
{
    [Fact]
    public void Aspire_AppHost_Sdk_and_Hosting_packages_share_the_same_version()
    {
        var repoRoot = FindRepoRoot();

        var sdkVersion = ReadAppHostSdkVersion(
            Path.Combine(repoRoot, "src", "AuthzEntitlements.AppHost", "AuthzEntitlements.AppHost.csproj"));

        var hostingPackages = ReadAspireHostingPackageVersions(
            Path.Combine(repoRoot, "Directory.Packages.props"));

        Assert.False(string.IsNullOrWhiteSpace(sdkVersion),
            "Could not parse the Aspire.AppHost.Sdk version from the AppHost csproj <Sdk> attribute.");
        Assert.NotEmpty(hostingPackages);

        var sdkBase = BaseVersion(sdkVersion!);

        var mismatches = hostingPackages
            .Where(kvp => BaseVersion(kvp.Value) != sdkBase)
            .Select(kvp => $"{kvp.Key}={kvp.Value} (base {BaseVersion(kvp.Value)})")
            .ToList();

        Assert.True(
            mismatches.Count == 0,
            $"Aspire family must move in lockstep: Aspire.AppHost.Sdk={sdkVersion} (base {sdkBase}) " +
            $"but these Aspire.Hosting.* packages differ: {string.Join(", ", mismatches)}. " +
            "Bump the AppHost <Sdk> attribute, every Aspire.Hosting.* package, and the Aspire CLI/DCP together.");
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AuthzEntitlements.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (AuthzEntitlements.sln).");
    }

    static string? ReadAppHostSdkVersion(string csprojPath)
    {
        // The AppHost SDK lives in the root <Project Sdk="Name/Version"> attribute (';'-separated
        // if multiple SDKs). Parse it as XML — the test already loads the props via XDocument.
        var sdkAttr = XDocument.Load(csprojPath).Root?.Attribute("Sdk")?.Value;
        var entry = sdkAttr?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(s => s.StartsWith("Aspire.AppHost.Sdk/", StringComparison.OrdinalIgnoreCase));
        var slash = entry?.IndexOf('/');
        return slash is > 0 ? entry![(slash.Value + 1)..] : null;
    }

    static IReadOnlyDictionary<string, string> ReadAspireHostingPackageVersions(string propsPath)
    {
        var doc = XDocument.Load(propsPath);
        return doc.Descendants("PackageVersion")
            .Select(e => (Include: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version")))
            .Where(p => p.Include is not null && p.Version is not null &&
                        (p.Include.StartsWith("Aspire.Hosting.", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p.Include, "Aspire.Hosting", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(p => p.Include!, p => p.Version!, StringComparer.OrdinalIgnoreCase);
    }

    static string BaseVersion(string version)
    {
        // Strip any prerelease/build-metadata suffix: "13.4.6-preview.1.26319.6" -> "13.4.6".
        var cut = version.IndexOfAny(['-', '+']);
        return cut >= 0 ? version[..cut] : version;
    }
}
