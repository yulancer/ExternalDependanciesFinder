using NuGetAccessChecker.Cli;
using NuGetAccessChecker.Infrastructure;
using NuGetAccessChecker.Models;
using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.App;

public sealed class ApplicationRunner
{
    private readonly Options _options;
    private readonly IPackageSourceProvider _sourceProvider;
    private readonly PackageReferenceCollector _collector;
    private readonly PackageAccessChecker _accessChecker;
    private readonly AccessReportFormatter _formatter;

    public ApplicationRunner(
        Options options,
        IPackageSourceProvider sourceProvider,
        IPackageFeedClient feedClient,
        PackageReferenceCollector? collector = null,
        PackageAccessChecker? accessChecker = null,
        AccessReportFormatter? formatter = null)
    {
        _options = options;
        _sourceProvider = sourceProvider;
        _collector = collector ?? new PackageReferenceCollector();
        _accessChecker = accessChecker ?? new PackageAccessChecker(feedClient);
        _formatter = formatter ?? new AccessReportFormatter();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ShowHelp)
        {
            Console.WriteLine(HelpText.Text);
            return 0;
        }

        string sourcePath;
        string settingsDirectory;
        string sourceLabel;
        IReadOnlyCollection<PackageIdVersion> packages;

        if (!string.IsNullOrWhiteSpace(_options.PackagesPath))
        {
            var packagesPath = Path.GetFullPath(_options.PackagesPath);
            if (!File.Exists(packagesPath))
            {
                Console.Error.WriteLine($"Файл со списком пакетов не найден: {packagesPath}");
                return 2;
            }

            try
            {
                packages = PackageListFile.Read(packagesPath);
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            sourcePath = packagesPath;
            settingsDirectory = Path.GetDirectoryName(packagesPath)!;
            sourceLabel = "СПИСОК";
        }
        else
        {
            string projectPath;
            try
            {
                projectPath = ProjectPathResolver.ResolveProjectFile(_options.ProjectPath);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            settingsDirectory = Path.GetDirectoryName(projectPath)!;
            sourcePath = projectPath;
            sourceLabel = "ПРОЕКТ";
            packages = await _collector.CollectAsync(settingsDirectory, projectPath);
        }

        if (packages.Count == 0)
        {
            Console.WriteLine("Не найдено пакетов с конкретными версиями.");
            return 1;
        }

        var inputDirectory = settingsDirectory;
        var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
            ? Path.Combine(inputDirectory, string.IsNullOrWhiteSpace(_options.PackagesPath)
                ? "unavailable-packages.txt"
                : "unavailable-packages-report.txt")
            : Path.GetFullPath(_options.OutputPath);

        var listPath = _options.ListPath;
        if (string.IsNullOrWhiteSpace(listPath) && !string.IsNullOrWhiteSpace(_options.PackagesPath))
            listPath = Path.Combine(inputDirectory, "unavailable-packages.txt");

        if (!string.IsNullOrWhiteSpace(listPath))
            listPath = Path.GetFullPath(listPath);

        if (!string.IsNullOrWhiteSpace(listPath)
            && string.Equals(listPath, Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Файлы --list и --output должны быть разными.");
            return 2;
        }

        var sources = _sourceProvider.GetEnabledSources(settingsDirectory);
        if (sources.Count == 0)
        {
            Console.WriteLine("Включённых источников NuGet не найдено. Проверьте NuGet.config.");
            return 1;
        }

        var results = new List<PackageAccessResult>();
        foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var result = await _accessChecker.CheckAsync(package, sources, cancellationToken);
            results.Add(result);

            if (result.IsAvailable)
            {
                var note = result.IsExactMatch
                    ? string.Empty
                    : $" (совместимая: {result.FoundVersion})";
                Console.WriteLine($"Доступен:   {result.PackageId} {result.RequiredVersion}{note}  (источник: {result.SourceName})");
            }
            else
            {
                Console.WriteLine($"Недоступен: {result.PackageId} {result.RequiredVersion}");
            }
        }

        var report = new AccessCheckReport
        {
            ProjectPath = sourcePath,
            SourceLabel = sourceLabel,
            Results = results
        };

        await _formatter.WriteAsync(report, outputPath);

        if (!string.IsNullOrWhiteSpace(listPath))
        {
            var unavailable = results
                .Where(r => !r.IsAvailable)
                .Select(r => new PackageIdVersion(r.PackageId, VersionText(r.RequiredVersion)))
                .ToArray();
            PackageListFile.Write(listPath, unavailable);
            Console.WriteLine($"Список недоступных пакетов: {listPath}");
        }

        Console.WriteLine();
        Console.WriteLine($"Готово. Отчёт: {outputPath}");
        return 0;
    }

    private static string VersionText(NuGet.Versioning.NuGetVersion version) =>
        string.IsNullOrEmpty(version.OriginalVersion) ? version.ToNormalizedString() : version.OriginalVersion;
}
