using ExternalDependenciesFinder.App;
using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Models;
using ExternalDependenciesFinder.Services;
using ExternalDependenciesFinder.Tests.Fakes;
using NuGet.Versioning;
using NSubstitute;

namespace ExternalDependenciesFinder.Tests.App;

public sealed class ApplicationRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "app-runner-" + Guid.NewGuid().ToString("N"));

    public ApplicationRunnerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Run_ListOnly_WritesPackagesAndSkipsSolutionGeneration()
    {
        var packagesPath = Path.Combine(_root, "packages_max.txt");
        var options = Options.Parse([_root, "--list-only", "--packages-output", packagesPath, "--log-dir", Path.Combine(_root, "logs")]);

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dapper"] = new PackageVersionInfo("2.1.35", NuGetVersion.Parse("2.1.35"))
        };

        var report = new RecordingReportWriter();
        var runner = new FakeProcessRunner();
        var collector = Substitute.For<IPackageCollector>();
        collector.Collect(options).Returns(packages);

        var solutionGenerator = Substitute.For<IValidationSolutionGenerator>();
        var app = new ApplicationRunner(options, report, runner, collector, solutionGenerator: solutionGenerator);

        var exitCode = app.Run();

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(packagesPath));
        Assert.Contains("Dapper => 2.1.35", File.ReadAllText(packagesPath));
        solutionGenerator.DidNotReceiveWithAnyArgs().Generate(default!, default!);
        Assert.Contains(report.Infos, x => x.Contains("--list-only"));
    }

    [Fact]
    public void Run_GeneratesSolution_WhenNotListOnly()
    {
        var packagesPath = Path.Combine(_root, "packages_max.txt");
        var options = Options.Parse([
            _root,
            "--packages-output", packagesPath,
            "--log-dir", Path.Combine(_root, "logs"),
            "--output-dir", Path.Combine(_root, "out")
        ]);

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dapper"] = new PackageVersionInfo("2.1.35", NuGetVersion.Parse("2.1.35"))
        };

        var report = new RecordingReportWriter();
        var runner = new FakeProcessRunner();
        var collector = Substitute.For<IPackageCollector>();
        collector.Collect(options).Returns(packages);

        var solutionGenerator = Substitute.For<IValidationSolutionGenerator>();
        var app = new ApplicationRunner(options, report, runner, collector, solutionGenerator: solutionGenerator);

        var exitCode = app.Run();

        Assert.Equal(0, exitCode);
        solutionGenerator.Received(1).Generate(options, packages);
        Assert.Contains(report.Infos, x => x.Contains("finished successfully"));
    }

    [Fact]
    public void Run_ReturnsOne_WhenUnhandledExceptionOccurs()
    {
        var options = Options.Parse([
            _root,
            "--list-only",
            "--packages-output", Path.Combine(_root, "packages_max.txt"),
            "--log-dir", Path.Combine(_root, "logs")
        ]);

        var report = new RecordingReportWriter();
        var runner = new FakeProcessRunner();
        var collector = Substitute.For<IPackageCollector>();
        collector.Collect(options).Returns(_ => throw new InvalidOperationException("boom"));

        var app = new ApplicationRunner(options, report, runner, collector);

        var exitCode = app.Run();

        Assert.Equal(1, exitCode);
        Assert.Contains(report.Errors, e => e.Title == "Unhandled error");
    }
}
