using NuGet.Versioning;
using NuGetAccessChecker.Infrastructure;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Tests.Fakes;

internal sealed class FakePackageFeedClient : IPackageFeedClient
{
    private readonly Dictionary<string, IReadOnlyList<NuGetVersion>> _versionsByPackage =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<(string PackageId, string Version)> _downloadable =
        new(PackageVersionComparer.Instance);

    private readonly HashSet<(string PackageId, string Version)> _downloadFailures =
        new(PackageVersionComparer.Instance);

    public List<(string Source, string PackageId)> GetAllVersionsCalls { get; } = [];
    public List<(string Source, string PackageId, string Version)> DownloadCalls { get; } = [];

    public void SetVersions(string packageId, params string[] versions) =>
        _versionsByPackage[packageId] = versions.Select(NuGetVersion.Parse).ToArray();

    public void SetDownloadable(string packageId, params string[] versions)
    {
        foreach (var version in versions)
            _downloadable.Add((packageId, version));
    }

    public void SetDownloadFailure(string packageId, string version) =>
        _downloadFailures.Add((packageId, version));

    public Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(
        PackageSourceInfo source,
        string packageId,
        CancellationToken cancellationToken)
    {
        GetAllVersionsCalls.Add((source.Name, packageId));
        if (_versionsByPackage.TryGetValue(packageId, out var versions))
            return Task.FromResult(versions);

        return Task.FromResult<IReadOnlyList<NuGetVersion>>(Array.Empty<NuGetVersion>());
    }

    public Task<bool> CanDownloadAsync(
        PackageSourceInfo source,
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        DownloadCalls.Add((source.Name, packageId, version.ToNormalizedString()));

        if (_downloadFailures.Contains((packageId, version.ToNormalizedString())))
            return Task.FromResult(false);

        if (_downloadable.Count == 0)
            return Task.FromResult(true);

        return Task.FromResult(_downloadable.Contains((packageId, version.ToNormalizedString())));
    }

    private sealed class PackageVersionComparer : IEqualityComparer<(string PackageId, string Version)>
    {
        public static readonly PackageVersionComparer Instance = new();

        public bool Equals((string PackageId, string Version) x, (string PackageId, string Version) y) =>
            string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string PackageId, string Version) obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId) * 397
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version);
    }
}
