using System.Diagnostics;
using System.Text;
using NuGet.Versioning;

const string HelpText = @"ExternalDependenciesFinder

Назначение:
  Ищет прямые и транзитивные NuGet-зависимости в .NET-решении или наборе *.csproj,
  выбирает максимальную версию каждого внешнего пакета и, при необходимости,
  создаёт служебное решение для проверки restore/build.

Использование:
  ExternalDependenciesFinder [scanRoot] [options]

Аргументы:
  scanRoot                         Папка для анализа. Если не указана, используется текущая папка.

Параметры:
  --output-dir <path>              Папка, куда будет создано служебное решение.
                                   По умолчанию: <AppContext.BaseDirectory>/NugetUsingSolution

  --packages-output <path>         Путь к итоговому файлу со списком пакетов.
                                   По умолчанию: <scanRoot>/packages_max.txt

  --log-dir <path>                 Папка для логов.
                                   По умолчанию: <scanRoot>/ExternalDependenciesFinder_Logs

  --exclude-prefix <prefix>        Дополнительный префикс пакетов для исключения.
                                   Можно указывать несколько раз или через запятую/точку с запятой.
                                   По умолчанию ничего не исключается.

  --clear-default-excludes         Очистить список исключений (оставлен для совместимости)

  --framework, -f <tfm>            Target framework для генерируемого проекта.
                                   По умолчанию: net8.0

  --solution-name <name>           Имя генерируемого служебного решения.
                                   По умолчанию: NugetUsingSolution

  --project-name <name>            Имя генерируемого служебного проекта.
                                   По умолчанию: NugetUsingProject

  --list-only                      Только сформировать packages_max.txt, без создания служебного решения.

  --no-build                       Создать служебное решение и выполнить restore, но пропустить build.

  --help, -h                       Показать справку.

Примеры:
  ExternalDependenciesFinder ""C:\Projects\Repo""
  ExternalDependenciesFinder ""C:\Projects\Repo"" --list-only
  ExternalDependenciesFinder ""C:\Projects\Repo"" --framework net6.0 --no-build
  ExternalDependenciesFinder ""C:\Projects\Repo"" --exclude-prefix Abdt. --output-dir ""D:\Temp\ExternalDeps""
";
var options = Options.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(HelpText);
    return 0;
}

if (!Directory.Exists(options.ScanRoot))
{
    Console.Error.WriteLine($"Папка для анализа не найдена: {options.ScanRoot}");
    return 2;
}

Directory.CreateDirectory(options.LogDirectory);

var report = new ReportWriter(
    Path.Combine(options.LogDirectory, "ExternalDependenciesFinder_Report.txt"),
    Path.Combine(options.LogDirectory, "ExternalDependenciesFinder_Errors.txt"),
    Path.Combine(options.LogDirectory, "dotnet-list-package.log"));

report.WriteInfo("ExternalDependenciesFinder started");
report.WriteInfo($"Scan root: {options.ScanRoot}");
report.WriteInfo($"Output file: {options.PackagesOutputPath}");
report.WriteInfo($"Generated solution directory: {options.OutputDirectory}");
report.WriteInfo($"Target framework: {options.Framework}");
report.WriteInfo($"List only: {options.ListOnly}");
report.WriteInfo($"No build: {options.NoBuild}");
report.WriteInfo($"Excluded prefixes: {string.Join(", ", options.ExcludePrefixes)}");

var runner = new ProcessRunner(report);
var maxByPackage = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase);

try
{
    CollectPackages(options, runner, report, maxByPackage);
    WritePackagesFile(options.PackagesOutputPath, maxByPackage, report);

    if (options.ListOnly)
    {
        report.WriteInfo("Mode --list-only enabled. Generated solution step skipped.");
        Console.WriteLine("\nРежим --list-only: создание служебного решения пропущено.");
        Console.WriteLine($"Результат сохранён в {options.PackagesOutputPath}");
        return 0;
    }

    GenerateValidationSolution(options, runner, report, maxByPackage);

    Console.WriteLine("\nГотово.");
    Console.WriteLine($"Файл со списком пакетов: {options.PackagesOutputPath}");
    Console.WriteLine($"Служебное решение: {Path.Combine(options.OutputDirectory, $"{options.SolutionName}.sln")}");
    Console.WriteLine($"Логи: {options.LogDirectory}");

    report.WriteInfo("ExternalDependenciesFinder finished successfully");
    return 0;
}
catch (Exception ex)
{
    report.WriteError("Unhandled error", ex.ToString());
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine($"Подробности см. в логах: {options.LogDirectory}");
    return 1;
}

static void CollectPackages(
    Options options,
    ProcessRunner runner,
    ReportWriter report,
    Dictionary<string, PackageVersionInfo> maxByPackage)
{
    var solutionFiles = Directory
        .EnumerateFiles(options.ScanRoot, "*.sln", SearchOption.TopDirectoryOnly)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (solutionFiles.Length > 0)
    {
        var solution = solutionFiles[0];
        Console.WriteLine($"Found solution: {Path.GetFileName(solution)}");
        report.WriteInfo($"Found solution: {solution}");

        if (solutionFiles.Length > 1)
        {
            report.WriteInfo("Multiple .sln files found. The first one is used: " + solution);
            foreach (var extraSolution in solutionFiles.Skip(1))
                report.WriteInfo("Ignored solution: " + extraSolution);
        }

        ProcessOneTarget(
            workingDir: options.ScanRoot,
            targetPath: solution,
            runner: runner,
            report: report,
            options: options,
            maxByPackage: maxByPackage);

        return;
    }

    Console.WriteLine("No .sln found, scanning *.csproj ...");
    report.WriteInfo("No .sln found. Recursive *.csproj scan started.");

    var projectFiles = Directory
        .EnumerateFiles(options.ScanRoot, "*.csproj", SearchOption.AllDirectories)
        .Where(x => !IsInsideDirectory(x, options.OutputDirectory))
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (projectFiles.Length == 0)
    {
        report.WriteInfo("No *.csproj files found.");
        Console.WriteLine("Проекты *.csproj не найдены.");
        return;
    }

    foreach (var project in projectFiles)
    {
        Console.WriteLine($"Project: {Path.GetFileName(project)}");
        report.WriteInfo($"Project: {project}");
        ProcessOneTarget(
            workingDir: Path.GetDirectoryName(project)!,
            targetPath: project,
            runner: runner,
            report: report,
            options: options,
            maxByPackage: maxByPackage);
    }
}

static void ProcessOneTarget(
    string workingDir,
    string targetPath,
    ProcessRunner runner,
    ReportWriter report,
    Options options,
    Dictionary<string, PackageVersionInfo> maxByPackage)
{
    var result = runner.Run("dotnet", new[] { "list", targetPath, "package", "--include-transitive" }, workingDir);

    report.WriteDotnetListOutput(targetPath, result.StdOut, result.StdErr, result.ExitCode);

    if (result.ExitCode != 0)
    {
        report.WriteError($"dotnet list package failed for {targetPath}", result.StdErr);
        Console.Error.WriteLine($"WARN: dotnet list package failed for {targetPath}. Exit code: {result.ExitCode}");
    }

    using var reader = new StringReader(result.StdOut);
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith(">", StringComparison.Ordinal))
            continue;

        var afterArrow = trimmed.TrimStart('>').TrimStart();
        if (string.IsNullOrWhiteSpace(afterArrow))
            continue;

        var tokens = SplitTokens(afterArrow);
        if (tokens.Count == 0)
            continue;

        var packageId = tokens[0];
        if (options.IsExcluded(packageId))
            continue;

        var version = tokens.LastOrDefault(x => NuGetVersion.TryParse(x, out _));
        if (version is null)
        {
            report.WriteError("Cannot parse package version", $"Target: {targetPath}{Environment.NewLine}Line: {line}");
            continue;
        }

        var parsedVersion = NuGetVersion.Parse(version);

        if (maxByPackage.TryGetValue(packageId, out var current))
        {
            if (parsedVersion.CompareTo(current.ParsedVersion) > 0)
                maxByPackage[packageId] = new PackageVersionInfo(version, parsedVersion);
        }
        else
        {
            maxByPackage[packageId] = new PackageVersionInfo(version, parsedVersion);
        }
    }
}

static void WritePackagesFile(
    string outputPath,
    Dictionary<string, PackageVersionInfo> maxByPackage,
    ReportWriter report)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

    using var sw = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    foreach (var pair in maxByPackage.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
        var line = $"{pair.Key} => {pair.Value.OriginalVersion}";
        Console.WriteLine(line);
        sw.WriteLine(line);
    }

    Console.WriteLine($"\nРезультат сохранён в {outputPath}");
    report.WriteInfo($"Packages file created: {outputPath}. Packages count: {maxByPackage.Count}");
}

static void GenerateValidationSolution(
    Options options,
    ProcessRunner runner,
    ReportWriter report,
    Dictionary<string, PackageVersionInfo> maxByPackage)
{
    RecreateDirectory(options.OutputDirectory, report);

    runner.RunOrThrow("dotnet", new[] { "new", "sln", "-n", options.SolutionName }, options.OutputDirectory);
    runner.RunOrThrow("dotnet", new[] { "new", "console", "-n", options.ProjectName, "--framework", options.Framework }, options.OutputDirectory);

    var projectPath = Path.Combine(options.OutputDirectory, options.ProjectName, $"{options.ProjectName}.csproj");
    var solutionPath = Path.Combine(options.OutputDirectory, $"{options.SolutionName}.sln");

    runner.RunOrThrow("dotnet", new[] { "sln", solutionPath, "add", projectPath }, options.OutputDirectory);

    var projectDir = Path.GetDirectoryName(projectPath)!;

    foreach (var pair in maxByPackage.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
        var packageId = pair.Key;
        var version = pair.Value.OriginalVersion;

        if (options.IsExcluded(packageId))
            continue;

        Console.WriteLine($"Adding package: {packageId} ({version})");

        var result = runner.Run(
            "dotnet",
            new[] { "add", projectPath, "package", packageId, "--version", version, "--no-restore" },
            projectDir);

        if (result.ExitCode != 0)
        {
            report.WriteError($"Failed to add package {packageId} {version}", result.StdErr);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARN: failed to add {packageId} {version}. dotnet exited {result.ExitCode}.");
            Console.ResetColor();
        }
    }

    Console.WriteLine("\nRunning dotnet restore...");
    runner.RunOrThrow("dotnet", new[] { "restore", solutionPath }, options.OutputDirectory);

    if (options.NoBuild)
    {
        Console.WriteLine("Режим --no-build: сборка пропущена.");
        report.WriteInfo("Mode --no-build enabled. Build step skipped.");
        return;
    }

    Console.WriteLine("Running dotnet build -c Release...");
    runner.RunOrThrow("dotnet", new[] { "build", solutionPath, "-c", "Release", "--nologo" }, options.OutputDirectory);
}

static void RecreateDirectory(string path, ReportWriter report)
{
    if (Directory.Exists(path))
    {
        report.WriteInfo($"Deleting existing output directory: {path}");
        Directory.Delete(path, recursive: true);

        for (var i = 0; i < 10 && Directory.Exists(path); i++)
            Thread.Sleep(50);
    }

    Directory.CreateDirectory(path);
    report.WriteInfo($"Output directory created: {path}");
}

static List<string> SplitTokens(string value)
{
    var result = new List<string>();
    var i = 0;

    while (i < value.Length)
    {
        while (i < value.Length && char.IsWhiteSpace(value[i]))
            i++;

        if (i >= value.Length)
            break;

        var start = i;
        while (i < value.Length && !char.IsWhiteSpace(value[i]))
            i++;

        result.Add(value[start..i]);
    }

    return result;
}

static bool IsInsideDirectory(string filePath, string directoryPath)
{
    var fullFilePath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    return fullFilePath.StartsWith(fullDirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

internal sealed class Options
{
    private static readonly string[] DefaultExcludePrefixes = Array.Empty<string>();

    public string ScanRoot { get; private init; } = Directory.GetCurrentDirectory();
    public string OutputDirectory { get; private init; } = Path.Combine(AppContext.BaseDirectory, "NugetUsingSolution");
    public string PackagesOutputPath { get; private init; } = string.Empty;
    public string LogDirectory { get; private init; } = string.Empty;
    public string Framework { get; private init; } = "net8.0";
    public string SolutionName { get; private init; } = "NugetUsingSolution";
    public string ProjectName { get; private init; } = "NugetUsingProject";
    public IReadOnlyList<string> ExcludePrefixes { get; private init; } = DefaultExcludePrefixes;
    public bool ListOnly { get; private init; }
    public bool NoBuild { get; private init; }
    public bool ShowHelp { get; private init; }

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
        var scanRootWasSet = false;

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

        scanRoot = Path.GetFullPath(scanRoot);
        outputDirectory = Path.GetFullPath(outputDirectory);
        packagesOutputPath = Path.GetFullPath(packagesOutputPath ?? Path.Combine(scanRoot, "packages_max.txt"));
        logDirectory = Path.GetFullPath(logDirectory ?? Path.Combine(scanRoot, "ExternalDependenciesFinder_Logs"));

        return new Options
        {
            ScanRoot = scanRoot,
            OutputDirectory = outputDirectory,
            PackagesOutputPath = packagesOutputPath,
            LogDirectory = logDirectory,
            Framework = framework,
            SolutionName = solutionName,
            ProjectName = projectName,
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
        value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class PackageVersionInfo
{
    public PackageVersionInfo(string originalVersion, NuGetVersion parsedVersion)
    {
        OriginalVersion = originalVersion;
        ParsedVersion = parsedVersion;
    }

    public string OriginalVersion { get; }
    public NuGetVersion ParsedVersion { get; }
}

internal sealed class ProcessRunner
{
    private readonly ReportWriter _report;

    public ProcessRunner(ReportWriter report)
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

        throw new InvalidOperationException($"Команда завершилась с ошибкой {result.ExitCode}: {fileName} {string.Join(" ", arguments)}{Environment.NewLine}{result.StdErr}");
    }

    private static string QuoteIfNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
}

internal sealed class ProcessResult
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

internal sealed class ReportWriter
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
