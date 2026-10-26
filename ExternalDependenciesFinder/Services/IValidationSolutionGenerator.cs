using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Services;

public interface IValidationSolutionGenerator
{
    void Generate(Options options, IReadOnlyDictionary<string, PackageVersionInfo> packages);
}
