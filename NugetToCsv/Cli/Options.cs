namespace NugetToCsv.Cli;

public sealed class Options
{
    public string PackagesPath { get; private init; } = string.Empty;
    public string ProjectPath { get; private init; } = string.Empty;
    public string TemplatePath { get; private init; } = string.Empty;
    public string OutputPath { get; private init; } = string.Empty;
    public bool ShowHelp { get; private init; }

    public static Options Parse(string[] args)
    {
        string? packagesPath = null;
        string? projectPath = null;
        string? templatePath = null;
        string? outputPath = null;
        var showHelp = false;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h")
            {
                showHelp = true;
                continue;
            }

            if (arg == "--packages" && i + 1 < args.Length)
            {
                packagesPath = args[++i];
                continue;
            }

            if (arg == "--project" && i + 1 < args.Length)
            {
                projectPath = args[++i];
                continue;
            }

            if (arg == "--template" && i + 1 < args.Length)
            {
                templatePath = args[++i];
                continue;
            }

            if (arg == "--output" && i + 1 < args.Length)
            {
                outputPath = args[++i];
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Неизвестный параметр: " + arg);

            positionals.Add(arg);
        }

        if (showHelp)
            return new Options { ShowHelp = true };

        if (positionals.Count > 0)
        {
            if (positionals.Count == 2
                && packagesPath is null
                && projectPath is null
                && templatePath is null
                && outputPath is null)
            {
                projectPath = positionals[0];
                templatePath = positionals[1];
            }
            else
            {
                throw new ArgumentException("Лишний аргумент: " + positionals[0]);
            }
        }

        var hasPackages = !string.IsNullOrWhiteSpace(packagesPath);
        var hasProject = !string.IsNullOrWhiteSpace(projectPath);
        if (hasPackages && hasProject)
            throw new ArgumentException("Укажите только один источник: --packages или --project.");

        if (!hasPackages && !hasProject)
            throw new ArgumentException("Укажите --packages или --project.");

        if (string.IsNullOrWhiteSpace(templatePath))
            throw new ArgumentException("Укажите --template с CSV-шаблоном.");

        return new Options
        {
            PackagesPath = packagesPath ?? string.Empty,
            ProjectPath = projectPath ?? string.Empty,
            TemplatePath = templatePath,
            OutputPath = outputPath ?? string.Empty,
            ShowHelp = false
        };
    }
}
