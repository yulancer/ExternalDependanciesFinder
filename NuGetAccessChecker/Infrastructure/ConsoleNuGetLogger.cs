using NuGet.Common;

namespace NuGetAccessChecker.Infrastructure;

public sealed class ConsoleNuGetLogger : ILogger
{
    private static void Print(string level, string data) =>
        Console.WriteLine($"[{level}] {data}");

    public void LogDebug(string data) { }
    public void LogVerbose(string data) { }
    public void LogInformation(string data) => Print("Information", data);
    public void LogMinimal(string data) => Print("Minimal", data);
    public void LogWarning(string data) => Print("Warning", data);
    public void LogError(string data) => Print("Error", data);
    public void LogInformationSummary(string data) => Print("InfoSummary", data);

    public void Log(LogLevel level, string data)
    {
        if (level is LogLevel.Debug or LogLevel.Verbose)
            return;

        Print(level.ToString(), data);
    }

    public Task LogAsync(LogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public void Log(ILogMessage message) => Log(message.Level, message.Message);

    public Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}
