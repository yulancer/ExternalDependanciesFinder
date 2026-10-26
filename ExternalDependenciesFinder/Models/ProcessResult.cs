namespace ExternalDependenciesFinder.Models;

public sealed class ProcessResult
{
    public ProcessResult(int exitCode, string stdOut, string stdErr)
    {
        ExitCode = exitCode;
        StdOut = stdOut;
        StdErr = stdErr;
    }

    public int ExitCode { get; }
    public string StdOut { get; }
    public string StdErr { get; }
}
