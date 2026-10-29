using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Infrastructure;
using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.App;

public sealed class ApplicationRunner
{
    private readonly Options _options;
    private readonly IReportWriter _report;
    private readonly IPackageCollector _collector;
    private readonly PackagesFileWriter _packagesFileWriter;
    private readonly IValidationSolutionGenerator _solutionGenerator;
    private readonly ISeedProjectPreparer _seedPreparer;

    public ApplicationRunner(
        Options options,
        IReportWriter report,
        IProcessRunner runner,
        IPackageCollector? collector = null,
        PackagesFileWriter? packagesFileWriter = null,
        IValidationSolutionGenerator? solutionGenerator = null,
        ISeedProjectPreparer? seedPreparer = null)
    {
        _options = options;
        _report = report;
        _collector = collector ?? new PackageCollector(runner, report);
        _packagesFileWriter = packagesFileWriter ?? new PackagesFileWriter();
        _solutionGenerator = solutionGenerator ?? new ValidationSolutionGenerator(runner, report);
        _seedPreparer = seedPreparer ?? new SeedProjectPreparer(runner, report);
    }

    public int Run()
    {
        _report.WriteInfo("ExternalDependenciesFinder started");
        _report.WriteInfo($"Scan root: {_options.ScanRoot}");
        _report.WriteInfo($"Output file: {_options.PackagesOutputPath}");
        _report.WriteInfo($"Generated solution directory: {_options.OutputDirectory}");
        _report.WriteInfo($"Target framework: {_options.Framework}");
        _report.WriteInfo($"List only: {_options.ListOnly}");
        _report.WriteInfo($"No build: {_options.NoBuild}");
        _report.WriteInfo($"Packages file: {_options.PackagesFile}");
        _report.WriteInfo($"Excluded prefixes: {string.Join(", ", _options.ExcludePrefixes)}");

        try
        {
            var collectOptions = _options;
            if (!string.IsNullOrWhiteSpace(_options.PackagesFile))
            {
                var seedDir = _seedPreparer.Prepare(_options);
                var excludeDirectory = Path.Combine(_options.OutputDirectory, "_exclude");
                collectOptions = _options.WithScanRoot(seedDir, excludeDirectory);
            }

            var packages = _collector.Collect(collectOptions);
            _packagesFileWriter.Write(_options.PackagesOutputPath, packages, _report);

            if (_options.ListOnly || !string.IsNullOrWhiteSpace(_options.PackagesFile))
            {
                if (!string.IsNullOrWhiteSpace(_options.PackagesFile))
                {
                    _report.WriteInfo("Packages file mode. Generated solution step skipped.");
                    Console.WriteLine("\nРежим --packages-file: служебный проект использован только для restore.");
                }
                else
                {
                    _report.WriteInfo("Mode --list-only enabled. Generated solution step skipped.");
                    Console.WriteLine("\nРежим --list-only: создание служебного решения пропущено.");
                }

                Console.WriteLine($"Список для передачи: {_options.PackagesOutputPath}");
                return 0;
            }

            _solutionGenerator.Generate(_options, packages);

            Console.WriteLine("\nГотово.");
            Console.WriteLine($"Файл со списком пакетов: {_options.PackagesOutputPath}");
            Console.WriteLine($"Служебное решение: {Path.Combine(_options.OutputDirectory, $"{_options.SolutionName}.sln")}");
            Console.WriteLine($"Логи: {_options.LogDirectory}");

            _report.WriteInfo("ExternalDependenciesFinder finished successfully");
            return 0;
        }
        catch (Exception ex)
        {
            _report.WriteError("Unhandled error", ex.ToString());
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine($"Подробности см. в логах: {_options.LogDirectory}");
            return 1;
        }
    }
}
