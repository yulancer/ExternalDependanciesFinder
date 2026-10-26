using System.Diagnostics;
using System.Text;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IReportWriter _report;

    public ProcessRunner(IReportWriter report)
    {
        _report = report;
    }

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var printableCommand = fileName + " " + string.Join(" ", arguments.Select(QuoteIfNeeded));
        _report.WriteInfo($"RUN: {printableCommand}");
        _report.WriteInfo($"Working directory: {workingDirectory}");

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить процесс: {fileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        _report.WriteInfo($"EXIT CODE: {process.ExitCode}");

        if (!string.IsNullOrWhiteSpace(stderr))
            _report.WriteError($"STDERR: {printableCommand}", stderr);

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public void RunOrThrow(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var result = Run(fileName, arguments, workingDirectory);
        if (result.ExitCode == 0)
            return;

        throw new InvalidOperationException(
            $"Команда завершилась с ошибкой {result.ExitCode}: {fileName} {string.Join(" ", arguments)}{Environment.NewLine}{result.StdErr}");
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}
