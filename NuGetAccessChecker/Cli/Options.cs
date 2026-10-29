namespace NuGetAccessChecker.Cli;

public sealed class Options
{
    public string ProjectPath { get; private init; } = string.Empty;
    public string PackagesPath { get; private init; } = string.Empty;
    public string OutputPath { get; private init; } = string.Empty;
    public string ListPath { get; private init; } = string.Empty;
    public bool ShowHelp { get; private init; }

    public static Options Parse(string[] args)
    {
        string? projectPath = null;
        string? packagesPath = null;
        string? outputPath = null;
        string? listPath = null;
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

            if (arg == "--packages" && i + 1 < args.Length)
            {
                packagesPath = args[++i];
                continue;
            }

            if (arg == "--output" && i + 1 < args.Length)
            {
                outputPath = args[++i];
                continue;
            }

            if (arg == "--list" && i + 1 < args.Length)
            {
                listPath = args[++i];
                continue;
            }

            throw new ArgumentException($"Неизвестный параметр: {arg}");
        }

        if (showHelp)
            return new Options { ShowHelp = true };

        var hasProject = !string.IsNullOrWhiteSpace(projectPath);
        var hasPackages = !string.IsNullOrWhiteSpace(packagesPath);
        if (hasProject && hasPackages)
            throw new ArgumentException("Укажите только один источник: --project или --packages.");

        if (!hasProject && !hasPackages)
            throw new ArgumentException("Укажите --project или --packages.");

        return new Options
        {
            ProjectPath = projectPath ?? string.Empty,
            PackagesPath = packagesPath ?? string.Empty,
            OutputPath = outputPath ?? string.Empty,
            ListPath = listPath ?? string.Empty,
            ShowHelp = false
        };
    }
}
