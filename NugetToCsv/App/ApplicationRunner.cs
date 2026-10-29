using NugetToCsv.Cli;
using NugetToCsv.Models;
using NugetToCsv.Services;

namespace NugetToCsv.App;

public sealed class ApplicationRunner
{
    private readonly Options _options;

    public ApplicationRunner(Options options)
    {
        _options = options;
    }

    public int Run()
    {
        if (_options.ShowHelp)
        {
            Console.WriteLine(HelpText.Text);
            return 0;
        }

        string sourcePath;
        IReadOnlyList<PackageIdVersion> packages;
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.PackagesPath))
            {
                sourcePath = Path.GetFullPath(_options.PackagesPath);
                if (!File.Exists(sourcePath))
                {
                    Console.Error.WriteLine("Файл со списком пакетов не найден: " + sourcePath);
                    return 2;
                }

                packages = PackageListReader.Read(sourcePath);
            }
            else
            {
                sourcePath = Path.GetFullPath(_options.ProjectPath);
                if (!File.Exists(sourcePath))
                {
                    Console.Error.WriteLine("Файл проекта не найден: " + sourcePath);
                    return 2;
                }

                packages = ProjectPackageReader.Read(sourcePath);
            }
        }
        catch (FormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var templatePath = Path.GetFullPath(_options.TemplatePath);
        if (!File.Exists(templatePath))
        {
            Console.Error.WriteLine("CSV-шаблон не найден: " + templatePath);
            return 2;
        }

        var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
            ? CsvInventoryFiller.DefaultOutputPath(templatePath)
            : Path.GetFullPath(_options.OutputPath);

        if (string.Equals(outputPath, templatePath, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Файл --output не должен совпадать с шаблоном.");
            return 2;
        }

        var encoding = Utf8EncodingDetector.Detect(templatePath);
        var templateLines = File.ReadAllLines(templatePath, encoding);

        IReadOnlyList<string> filled;
        try
        {
            filled = CsvInventoryFiller.Fill(templateLines, packages);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        File.WriteAllLines(outputPath, filled, encoding);
        Console.WriteLine("Готово. Записано строк: " + packages.Count);
        Console.WriteLine(outputPath);
        return 0;
    }
}
