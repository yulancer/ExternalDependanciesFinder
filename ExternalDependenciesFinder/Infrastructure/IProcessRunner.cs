using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Infrastructure;

public interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory);
    void RunOrThrow(string fileName, IReadOnlyList<string> arguments, string workingDirectory);
}
