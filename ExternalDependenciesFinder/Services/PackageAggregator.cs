using ExternalDependenciesFinder.Models;
using NuGet.Versioning;

namespace ExternalDependenciesFinder.Services;

public sealed class PackageAggregator
{
    private readonly Dictionary<string, PackageVersionInfo> _packages =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PackageVersionInfo> Packages => _packages;

    public void AddOrUpdate(string packageId, string version)
    {
        var parsedVersion = NuGetVersion.Parse(version);

        if (_packages.TryGetValue(packageId, out var current))
        {
            if (parsedVersion.CompareTo(current.ParsedVersion) > 0)
                _packages[packageId] = new PackageVersionInfo(version, parsedVersion);
        }
        else
        {
            _packages[packageId] = new PackageVersionInfo(version, parsedVersion);
        }
    }

    public void AddRange(IEnumerable<ParsedPackageLine> packages)
    {
        foreach (var package in packages)
            AddOrUpdate(package.PackageId, package.Version);
    }
}
