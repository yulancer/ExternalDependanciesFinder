using System.Text;
using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Infrastructure;
using NuGet.Versioning;

namespace ExternalDependenciesFinder.Services;

public sealed class SeedProjectPreparer : ISeedProjectPreparer
{
    private readonly IProcessRunner _runner;
    private readonly IReportWriter _report;

    public SeedProjectPreparer(IProcessRunner runner, IReportWriter report)
    {
        _runner = runner;
        _report = report;
    }

    public string Prepare(Options options)
    {
        if (string.IsNullOrWhiteSpace(options.PackagesFile))
            throw new InvalidOperationException("Не указан файл со списком пакетов.");

        if (!File.Exists(options.PackagesFile))
            throw new FileNotFoundException($"Файл со списком пакетов не найден: {options.PackagesFile}");

        var packages = Deduplicate(PackageListFile.Read(options.PackagesFile));
        if (packages.Count == 0)
            throw new InvalidOperationException($"В файле нет пакетов: {options.PackagesFile}");

        PathUtilities.RecreateDirectory(options.OutputDirectory, _report.WriteInfo);

        var projectDir = Path.Combine(options.OutputDirectory, "SeedProject");
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, "SeedProject.csproj");
        File.WriteAllText(projectPath, BuildProject(options.Framework, packages), new UTF8Encoding(false));

        _report.WriteInfo($"Seed project created: {projectPath}. Packages: {packages.Count}. Framework: {options.Framework}");
        Console.WriteLine($"Служебный проект для restore: {projectPath}");
        Console.WriteLine("Running dotnet restore...");
        _runner.RunOrThrow("dotnet", ["restore", projectPath], projectDir);

        return projectDir;
    }

    internal static IReadOnlyList<PackageListEntry> Deduplicate(IReadOnlyList<PackageListEntry> packages)
    {
        var map = new Dictionary<string, PackageListEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            if (!map.TryGetValue(package.Id, out var current)
                || NuGetVersion.Parse(package.Version).CompareTo(NuGetVersion.Parse(current.Version)) > 0)
            {
                map[package.Id] = package;
            }
        }

        return map.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildProject(string framework, IReadOnlyList<PackageListEntry> packages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine($"    <TargetFramework>{Escape(framework)}</TargetFramework>");
        builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        builder.AppendLine("    <Nullable>enable</Nullable>");
        builder.AppendLine("  </PropertyGroup>");
        builder.AppendLine("  <ItemGroup>");
        foreach (var package in packages)
        {
            builder.AppendLine(
                $"    <PackageReference Include=\"{Escape(package.Id)}\" Version=\"{Escape(package.Version)}\" />");
        }

        builder.AppendLine("  </ItemGroup>");
        builder.AppendLine("</Project>");
        return builder.ToString();
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
