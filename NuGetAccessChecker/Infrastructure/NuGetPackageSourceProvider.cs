using NuGet.Configuration;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Infrastructure;

public sealed class NuGetPackageSourceProvider : IPackageSourceProvider
{
    public IReadOnlyList<PackageSourceInfo> GetEnabledSources(string projectDirectory)
    {
        var settings = Settings.LoadDefaultSettings(projectDirectory);
        var sourceProvider = new PackageSourceProvider(settings);

        return sourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .Select(s => new PackageSourceInfo(s.Name, s.Source))
            .ToArray();
    }
}
