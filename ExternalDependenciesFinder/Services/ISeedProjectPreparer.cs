using ExternalDependenciesFinder.Cli;

namespace ExternalDependenciesFinder.Services;

public interface ISeedProjectPreparer
{
    string Prepare(Options options);
}
