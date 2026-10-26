using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Infrastructure;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Services;

public sealed class ValidationSolutionGenerator : IValidationSolutionGenerator
{
    private readonly IProcessRunner _runner;
    private readonly IReportWriter _report;

    public ValidationSolutionGenerator(IProcessRunner runner, IReportWriter report)
    {
        _runner = runner;
        _report = report;
    }

    public void Generate(Options options, IReadOnlyDictionary<string, PackageVersionInfo> packages)
    {
        PathUtilities.RecreateDirectory(options.OutputDirectory, _report.WriteInfo);

        _runner.RunOrThrow("dotnet", ["new", "sln", "-n", options.SolutionName], options.OutputDirectory);
        _runner.RunOrThrow(
            "dotnet",
            ["new", "console", "-n", options.ProjectName, "--framework", options.Framework],
            options.OutputDirectory);

        var projectPath = Path.Combine(options.OutputDirectory, options.ProjectName, $"{options.ProjectName}.csproj");
        var solutionPath = Path.Combine(options.OutputDirectory, $"{options.SolutionName}.sln");

        _runner.RunOrThrow("dotnet", ["sln", solutionPath, "add", projectPath], options.OutputDirectory);

        var projectDir = Path.GetDirectoryName(projectPath)!;

        foreach (var pair in packages.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var packageId = pair.Key;
            var version = pair.Value.OriginalVersion;

            if (options.IsExcluded(packageId))
                continue;

            Console.WriteLine($"Adding package: {packageId} ({version})");

            var result = _runner.Run(
                "dotnet",
                ["add", projectPath, "package", packageId, "--version", version, "--no-restore"],
                projectDir);

            if (result.ExitCode != 0)
            {
                _report.WriteError($"Failed to add package {packageId} {version}", result.StdErr);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARN: failed to add {packageId} {version}. dotnet exited {result.ExitCode}.");
                Console.ResetColor();
            }
        }

        Console.WriteLine("\nRunning dotnet restore...");
        _runner.RunOrThrow("dotnet", ["restore", solutionPath], options.OutputDirectory);

        if (options.NoBuild)
        {
            Console.WriteLine("Режим --no-build: сборка пропущена.");
            _report.WriteInfo("Mode --no-build enabled. Build step skipped.");
            return;
        }

        Console.WriteLine("Running dotnet build -c Release...");
        _runner.RunOrThrow("dotnet", ["build", solutionPath, "-c", "Release", "--nologo"], options.OutputDirectory);
    }
}
