using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Infrastructure;
using ExternalDependenciesFinder.Models;

namespace ExternalDependenciesFinder.Services;

public sealed class PackageCollector : IPackageCollector
{
    private readonly IProcessRunner _runner;
    private readonly IReportWriter _report;
    private readonly PackageListParser _parser;
    private readonly ScanTargetFinder _targetFinder;

    public PackageCollector(
        IProcessRunner runner,
        IReportWriter report,
        PackageListParser? parser = null,
        ScanTargetFinder? targetFinder = null)
    {
        _runner = runner;
        _report = report;
        _parser = parser ?? new PackageListParser();
        _targetFinder = targetFinder ?? new ScanTargetFinder();
    }

    public IReadOnlyDictionary<string, PackageVersionInfo> Collect(Options options)
    {
        var aggregator = new PackageAggregator();
        var targets = _targetFinder.FindTargets(options.ScanRoot, options.OutputDirectory);

        if (targets.Count == 0)
        {
            _report.WriteInfo("No *.csproj files found.");
            Console.WriteLine("Проекты *.csproj не найдены.");
            return aggregator.Packages;
        }

        var first = targets[0];
        if (first.Kind == ScanTargetKind.Solution)
        {
            Console.WriteLine($"Found solution: {Path.GetFileName(first.TargetPath)}");
            _report.WriteInfo($"Found solution: {first.TargetPath}");

            if (first.IgnoredSolutions.Count > 0)
            {
                _report.WriteInfo("Multiple .sln files found. The first one is used: " + first.TargetPath);
                foreach (var extraSolution in first.IgnoredSolutions)
                    _report.WriteInfo("Ignored solution: " + extraSolution);
            }
        }
        else
        {
            Console.WriteLine("No .sln found, scanning *.csproj ...");
            _report.WriteInfo("No .sln found. Recursive *.csproj scan started.");
        }

        foreach (var target in targets)
        {
            if (target.Kind == ScanTargetKind.Project)
            {
                Console.WriteLine($"Project: {Path.GetFileName(target.TargetPath)}");
                _report.WriteInfo($"Project: {target.TargetPath}");
            }

            ProcessOneTarget(target, options, aggregator);
        }

        return aggregator.Packages;
    }

    private void ProcessOneTarget(ScanTarget target, Options options, PackageAggregator aggregator)
    {
        var result = _runner.Run(
            "dotnet",
            ["list", target.TargetPath, "package", "--include-transitive"],
            target.WorkingDirectory);

        _report.WriteDotnetListOutput(target.TargetPath, result.StdOut, result.StdErr, result.ExitCode);

        if (result.ExitCode != 0)
        {
            _report.WriteError($"dotnet list package failed for {target.TargetPath}", result.StdErr);
            Console.Error.WriteLine($"WARN: dotnet list package failed for {target.TargetPath}. Exit code: {result.ExitCode}");
        }

        var packages = _parser.Parse(
            result.StdOut,
            options.IsExcluded,
            (line, _) => _report.WriteError(
                "Cannot parse package version",
                $"Target: {target.TargetPath}{Environment.NewLine}Line: {line}"));

        aggregator.AddRange(packages);
    }
}
