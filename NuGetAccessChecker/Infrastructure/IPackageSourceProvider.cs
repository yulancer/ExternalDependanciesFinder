using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Infrastructure;

public interface IPackageSourceProvider
{
    IReadOnlyList<PackageSourceInfo> GetEnabledSources(string projectDirectory);
}
