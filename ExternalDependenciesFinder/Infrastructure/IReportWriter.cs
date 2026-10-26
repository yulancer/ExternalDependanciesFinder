namespace ExternalDependenciesFinder.Infrastructure;

public interface IReportWriter
{
    void WriteInfo(string message);
    void WriteError(string title, string details);
    void WriteDotnetListOutput(string targetPath, string stdout, string stderr, int exitCode);
}
