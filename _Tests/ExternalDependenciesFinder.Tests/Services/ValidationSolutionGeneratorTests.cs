using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Models;
using ExternalDependenciesFinder.Services;
using ExternalDependenciesFinder.Tests.Fakes;
using NuGet.Versioning;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class ValidationSolutionGeneratorTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), "val-sol-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Fact]
    public void Generate_RunsExpectedDotnetCommands_AndSkipsBuildWhenNoBuild()
    {
        var runner = new FakeProcessRunner();
        for (var i = 0; i < 10; i++)
            runner.EnqueueSuccess();

        var report = new RecordingReportWriter();
        var options = Options.Parse([
            Path.GetTempPath(),
            "--output-dir", _outputDir,
            "--solution-name", "CheckSol",
            "--project-name", "CheckProj",
            "--framework", "net8.0",
            "--no-build"
        ]);

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Newtonsoft.Json"] = new PackageVersionInfo("13.0.3", NuGetVersion.Parse("13.0.3")),
            ["Rbp.Skip"] = new PackageVersionInfo("1.0.0", NuGetVersion.Parse("1.0.0"))
        };

        new ValidationSolutionGenerator(runner, report).Generate(options, packages);

        Assert.Contains(runner.Calls, c => c.Arguments.SequenceEqual(["new", "sln", "-n", "CheckSol"]));
        Assert.Contains(runner.Calls, c =>
            c.Arguments.SequenceEqual(["new", "console", "-n", "CheckProj", "--framework", "net8.0"]));
        Assert.Contains(runner.Calls, c => c.Arguments[0] == "add" && c.Arguments.Contains("Newtonsoft.Json"));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("Rbp.Skip"));
        Assert.Contains(runner.Calls, c => c.Arguments[0] == "restore");
        Assert.DoesNotContain(runner.Calls, c => c.Arguments[0] == "build");
        Assert.Contains(report.Infos, x => x.Contains("--no-build"));
    }

    [Fact]
    public void Generate_RunsBuild_WhenNoBuildIsFalse()
    {
        var runner = new FakeProcessRunner();
        for (var i = 0; i < 10; i++)
            runner.EnqueueSuccess();

        var report = new RecordingReportWriter();
        var options = Options.Parse([
            Path.GetTempPath(),
            "--output-dir", _outputDir,
            "--clear-default-excludes"
        ]);

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dapper"] = new PackageVersionInfo("2.1.35", NuGetVersion.Parse("2.1.35"))
        };

        new ValidationSolutionGenerator(runner, report).Generate(options, packages);

        Assert.Contains(runner.Calls, c =>
            c.Arguments.SequenceEqual(["build", Path.Combine(_outputDir, "NugetUsingSolution.sln"), "-c", "Release", "--nologo"]));
    }

    [Fact]
    public void Generate_RecordsError_WhenAddPackageFails()
    {
        var runner = new FakeProcessRunner();
        runner.EnqueueSuccess(); // new sln
        runner.EnqueueSuccess(); // new console
        runner.EnqueueSuccess(); // sln add
        runner.Enqueue(new ProcessResult(1, string.Empty, "package not found")); // add package
        runner.EnqueueSuccess(); // restore
        runner.EnqueueSuccess(); // build

        var report = new RecordingReportWriter();
        var options = Options.Parse([
            Path.GetTempPath(),
            "--output-dir", _outputDir,
            "--clear-default-excludes"
        ]);

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Missing.Package"] = new PackageVersionInfo("1.0.0", NuGetVersion.Parse("1.0.0"))
        };

        new ValidationSolutionGenerator(runner, report).Generate(options, packages);

        Assert.Contains(report.Errors, e => e.Title.Contains("Failed to add package Missing.Package"));
    }
}
