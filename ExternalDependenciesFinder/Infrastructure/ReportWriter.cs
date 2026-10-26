using System.Text;

namespace ExternalDependenciesFinder.Infrastructure;

public sealed class ReportWriter : IReportWriter
{
    private readonly string _reportPath;
    private readonly string _errorsPath;
    private readonly string _dotnetListPath;
    private readonly object _lock = new();

    public ReportWriter(string reportPath, string errorsPath, string dotnetListPath)
    {
        _reportPath = reportPath;
        _errorsPath = errorsPath;
        _dotnetListPath = dotnetListPath;

        ResetFile(_reportPath);
        ResetFile(_errorsPath);
        ResetFile(_dotnetListPath);
    }

    public void WriteInfo(string message)
    {
        Append(_reportPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}");
    }

    public void WriteError(string title, string details)
    {
        var text = new StringBuilder()
            .AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {title}")
            .AppendLine(details)
            .AppendLine(new string('-', 120))
            .ToString();

        Append(_errorsPath, text);
    }

    public void WriteDotnetListOutput(string targetPath, string stdout, string stderr, int exitCode)
    {
        var text = new StringBuilder()
            .AppendLine(new string('=', 120))
            .AppendLine($"Target: {targetPath}")
            .AppendLine($"ExitCode: {exitCode}")
            .AppendLine("STDOUT:")
            .AppendLine(stdout)
            .AppendLine("STDERR:")
            .AppendLine(stderr)
            .ToString();

        Append(_dotnetListPath, text);
    }

    private void Append(string path, string text)
    {
        lock (_lock)
        {
            File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void ResetFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
