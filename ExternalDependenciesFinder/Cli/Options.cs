namespace ExternalDependenciesFinder.Cli;

public sealed class Options
{
    private static readonly string[] DefaultExcludePrefixes = { "Rbp.", "Psb." };

    public string ScanRoot { get; private init; } = Directory.GetCurrentDirectory();
    public string OutputDirectory { get; private init; } = Path.Combine(AppContext.BaseDirectory, "NugetUsingSolution");
    public string PackagesOutputPath { get; private init; } = string.Empty;
    public string LogDirectory { get; private init; } = string.Empty;
    public string Framework { get; private init; } = "net8.0";
    public string SolutionName { get; private init; } = "NugetUsingSolution";
    public string ProjectName { get; private init; } = "NugetUsingProject";
    public string PackagesFile { get; private init; } = string.Empty;
    public IReadOnlyList<string> ExcludePrefixes { get; private init; } = DefaultExcludePrefixes;
    public bool ListOnly { get; private init; }
    public bool NoBuild { get; private init; }
    public bool ShowHelp { get; private init; }

    public Options WithScanRoot(string scanRoot, string outputDirectoryToExclude) => new()
    {
        ScanRoot = Path.GetFullPath(scanRoot),
        OutputDirectory = Path.GetFullPath(outputDirectoryToExclude),
        PackagesOutputPath = PackagesOutputPath,
        LogDirectory = LogDirectory,
        Framework = Framework,
        SolutionName = SolutionName,
        ProjectName = ProjectName,
        PackagesFile = PackagesFile,
        ExcludePrefixes = ExcludePrefixes,
        ListOnly = ListOnly,
        NoBuild = NoBuild,
        ShowHelp = ShowHelp
    };

    public bool IsExcluded(string packageId) =>
        ExcludePrefixes.Any(prefix => packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static Options Parse(string[] args)
    {
        var scanRoot = Directory.GetCurrentDirectory();
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "NugetUsingSolution");
        var framework = "net8.0";
        var solutionName = "NugetUsingSolution";
        var projectName = "NugetUsingProject";
        var excludePrefixes = new List<string>(DefaultExcludePrefixes);
        var listOnly = false;
        var noBuild = false;
        var showHelp = false;
        string? packagesOutputPath = null;
        string? logDirectory = null;
        string? packagesFile = null;
        var scanRootWasSet = false;
        var outputDirectoryWasSet = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--output-dir", StringComparison.OrdinalIgnoreCase))
            {
                outputDirectory = RequireValue(args, ref i, arg);
                outputDirectoryWasSet = true;
                continue;
            }

            if (arg.Equals("--packages-file", StringComparison.OrdinalIgnoreCase))
            {
                packagesFile = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--packages-output", StringComparison.OrdinalIgnoreCase))
            {
                packagesOutputPath = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--log-dir", StringComparison.OrdinalIgnoreCase))
            {
                logDirectory = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--framework", StringComparison.OrdinalIgnoreCase) || arg.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                framework = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--solution-name", StringComparison.OrdinalIgnoreCase))
            {
                solutionName = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--project-name", StringComparison.OrdinalIgnoreCase))
            {
                projectName = RequireValue(args, ref i, arg);
                continue;
            }

            if (arg.Equals("--exclude-prefix", StringComparison.OrdinalIgnoreCase))
            {
                var value = RequireValue(args, ref i, arg);
                foreach (var prefix in SplitPrefixList(value))
                    excludePrefixes.Add(prefix);
                continue;
            }

            if (arg.Equals("--clear-default-excludes", StringComparison.OrdinalIgnoreCase))
            {
                excludePrefixes.Clear();
                continue;
            }

            if (arg.Equals("--list-only", StringComparison.OrdinalIgnoreCase))
            {
                listOnly = true;
                continue;
            }

            if (arg.Equals("--no-build", StringComparison.OrdinalIgnoreCase))
            {
                noBuild = true;
                continue;
            }

            if (arg.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Неизвестный параметр: {arg}");

            if (scanRootWasSet)
                throw new ArgumentException($"Передано больше одного пути для анализа. Лишний аргумент: {arg}");

            scanRoot = arg;
            scanRootWasSet = true;
        }

        if (!string.IsNullOrWhiteSpace(packagesFile) && scanRootWasSet)
            throw new ArgumentException("Параметр --packages-file не сочетается с путём папки. Укажите только файл со списком пакетов.");

        if (!string.IsNullOrWhiteSpace(packagesFile))
        {
            packagesFile = Path.GetFullPath(packagesFile);
            var packagesDir = Path.GetDirectoryName(packagesFile);
            if (string.IsNullOrEmpty(packagesDir))
                packagesDir = Directory.GetCurrentDirectory();
            scanRoot = packagesDir;
            if (!outputDirectoryWasSet)
                outputDirectory = Path.Combine(packagesDir, "seed-restore");
        }

        scanRoot = Path.GetFullPath(scanRoot);
        outputDirectory = Path.GetFullPath(outputDirectory);
        packagesOutputPath = Path.GetFullPath(packagesOutputPath ?? Path.Combine(scanRoot, "packages_max.txt"));
        logDirectory = Path.GetFullPath(logDirectory ?? Path.Combine(scanRoot, "ExternalDependеnciesFinder_Logs"));

        return new Options
        {
            ScanRoot = scanRoot,
            OutputDirectory = outputDirectory,
            PackagesOutputPath = packagesOutputPath,
            LogDirectory = logDirectory,
            Framework = framework,
            SolutionName = solutionName,
            ProjectName = projectName,
            PackagesFile = packagesFile ?? string.Empty,
            ExcludePrefixes = excludePrefixes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ListOnly = listOnly,
            NoBuild = noBuild,
            ShowHelp = showHelp
        };
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Для параметра {optionName} нужно указать значение.");

        index++;
        return args[index];
    }

    private static IEnumerable<string> SplitPrefixList(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
