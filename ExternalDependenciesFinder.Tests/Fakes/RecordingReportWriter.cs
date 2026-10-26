using ExternalDependenciesFinder.Infrastructure;

namespace ExternalDependenciesFinder.Tests.Fakes;

internal sealed class RecordingReportWriter : IReportWriter
{
    public List<string> Infos { get; } = [];
    public List<(string Title, string Details)> Errors { get; } = [];
    public List<(string Target, string StdOut, string StdErr, int ExitCode)> DotnetListOutputs { get; } = [];

    public void WriteInfo(string message) => Infos.Add(message);

    public void WriteError(string title, string details) => Errors.Add((title, details));

    public void WriteDotnetListOutput(string targetPath, string stdout, string stderr, int exitCode) =>
        DotnetListOutputs.Add((targetPath, stdout, stderr, exitCode));
}
