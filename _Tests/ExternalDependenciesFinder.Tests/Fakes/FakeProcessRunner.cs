using ExternalDependenciesFinder.Infrastructure;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Tests.Fakes;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();
    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Calls { get; } = [];

    public void Enqueue(ProcessResult result) => _results.Enqueue(result);

    public void EnqueueSuccess(string stdOut = "") => Enqueue(new ProcessResult(0, stdOut, string.Empty));

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        Calls.Add((fileName, arguments.ToArray(), workingDirectory));
        if (_results.Count == 0)
            return new ProcessResult(0, string.Empty, string.Empty);

        return _results.Dequeue();
    }

    public void RunOrThrow(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var result = Run(fileName, arguments, workingDirectory);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Команда завершилась с ошибкой {result.ExitCode}: {fileName} {string.Join(" ", arguments)}{Environment.NewLine}{result.StdErr}");
        }
    }
}
