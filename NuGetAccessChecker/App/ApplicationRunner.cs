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

        var projectDir = Path.GetDirectoryName(projectPath)!;
        var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
            ? Path.Combine(projectDir, "unavailable-packages.txt")
            : Path.GetFullPath(_options.OutputPath);

        var packages = await _collector.CollectAsync(projectDir, projectPath);
        if (packages.Count == 0)
        {
            Console.WriteLine("В проекте не найдено пакетов с конкретными версиями.");
            return 1;
        }

        var sources = _sourceProvider.GetEnabledSources(projectDir);
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
            ProjectPath = projectPath,
            Results = results
        };

        await _formatter.WriteAsync(report, outputPath);
        Console.WriteLine();
        Console.WriteLine($"Готово. Отчёт: {outputPath}");
        return 0;
    }
}
