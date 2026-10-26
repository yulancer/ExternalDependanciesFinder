using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Models;
using ExternalDependenciesFinder.Services;
using ExternalDependenciesFinder.Tests.Fakes;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PackageCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "collector-" + Guid.NewGuid().ToString("N"));

    public PackageCollectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Collect_ParsesDotnetListOutput_AndAggregatesMaxVersions()
    {
        File.WriteAllText(Path.Combine(_root, "App.sln"), string.Empty);

        var runner = new FakeProcessRunner();
        runner.EnqueueSuccess("""
               > Newtonsoft.Json      13.0.1
               > Rbp.Internal         1.0.0
               > Newtonsoft.Json      13.0.3
            """);

        var report = new RecordingReportWriter();
        var options = Options.Parse([_root, "--list-only"]);
        var collector = new PackageCollector(runner, report);

        var packages = collector.Collect(options);

        Assert.Single(packages);
        Assert.Equal("13.0.3", packages["Newtonsoft.Json"].OriginalVersion);
        Assert.Single(runner.Calls);
        Assert.Equal("dotnet", runner.Calls[0].FileName);
        Assert.Contains("--include-transitive", runner.Calls[0].Arguments);
        Assert.Single(report.DotnetListOutputs);
    }

    [Fact]
    public void Collect_Continues_WhenDotnetListFails()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Broken.csproj"), "<Project />");

        var runner = new FakeProcessRunner();
        runner.Enqueue(new ProcessResult(1, string.Empty, "restore failed"));

        var report = new RecordingReportWriter();
        var options = Options.Parse([_root, "--list-only"]);
        var collector = new PackageCollector(runner, report);

        var packages = collector.Collect(options);

        Assert.Empty(packages);
        Assert.Contains(report.Errors, e => e.Title.Contains("dotnet list package failed"));
    }
}
