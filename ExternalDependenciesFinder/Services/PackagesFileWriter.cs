using System.Text;
using ExternalDependenciesFinder.Infrastructure;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Services;

public sealed class PackagesFileWriter
{
    public void Write(
        string outputPath,
        IReadOnlyDictionary<string, PackageVersionInfo> packages,
        IReportWriter report,
        TextWriter? console = null)
    {
        console ??= Console.Out;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var sw = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        foreach (var pair in packages.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var line = $"{pair.Key} => {pair.Value.OriginalVersion}";
            console.WriteLine(line);
            sw.WriteLine(line);
        }

        console.WriteLine($"\nРезультат сохранён в {outputPath}");
        report.WriteInfo($"Packages file created: {outputPath}. Packages count: {packages.Count}");
    }
}
