namespace NuGetAccessChecker.Cli;

public sealed class Options
{
    public string ProjectPath { get; private init; } = string.Empty;
    public string OutputPath { get; private init; } = string.Empty;
    public bool ShowHelp { get; private init; }

    public static Options Parse(string[] args)
    {
        string? projectPath = null;
        string? outputPath = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h")
            {
                showHelp = true;
                continue;
            }

            if (arg == "--project" && i + 1 < args.Length)
            {
                projectPath = args[++i];
                continue;
            }

            if (arg == "--output" && i + 1 < args.Length)
            {
                outputPath = args[++i];
                continue;
            }

            throw new ArgumentException($"Неизвестный параметр: {arg}");
        }

        if (showHelp)
            return new Options { ShowHelp = true };

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("Не указан параметр --project.");

        return new Options
        {
            ProjectPath = projectPath,
            OutputPath = outputPath ?? string.Empty,
            ShowHelp = false
        };
    }
}
