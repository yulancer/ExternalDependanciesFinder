using ExternalDependenciesFinder.App;
using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Infrastructure;

var options = Options.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(HelpText.Text);
    return 0;
}

if (!string.IsNullOrWhiteSpace(options.PackagesFile))
{
    if (!File.Exists(options.PackagesFile))
    {
        Console.Error.WriteLine($"Файл со списком пакетов не найден: {options.PackagesFile}");
        return 2;
    }
}
else if (!Directory.Exists(options.ScanRoot))
{
    Console.Error.WriteLine($"Папка для анализа не найдена: {options.ScanRoot}");
    return 2;
}

Directory.CreateDirectory(options.LogDirectory);

var report = new ReportWriter(
    Path.Combine(options.LogDirectory, "ExternalDependеnciesFinder_Report.txt"),
    Path.Combine(options.LogDirectory, "ExternalDependеnciesFinder_Errors.txt"),
    Path.Combine(options.LogDirectory, "dotnet-list-package.log"));

var runner = new ProcessRunner(report);
var app = new ApplicationRunner(options, report, runner);
return app.Run();
