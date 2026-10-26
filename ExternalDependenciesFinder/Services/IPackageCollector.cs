using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Services;

public interface IPackageCollector
{
    IReadOnlyDictionary<string, PackageVersionInfo> Collect(Options options);
}
