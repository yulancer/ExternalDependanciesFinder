using ExternalDependenciesFinder.Cli;
using ExternalDependenciesFinder.Services;
using ExternalDependenciesFinder.Tests.Fakes;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class SeedProjectPreparerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "seed-" + Guid.NewGuid().ToString("N"));

    public SeedProjectPreparerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Prepare_WritesProjectWithHighestVersionAndRestores()
    {
        var packagesFile = Path.Combine(_root, "initial-packages.txt");
        File.WriteAllText(packagesFile, """
            # direct packages
            Newtonsoft.Json => 13.0.1
            Dapper 2.1.35
            Newtonsoft.Json => 13.0.3
            """);

        var options = Options.Parse([
            "--packages-file", packagesFile,
            "--framework", "net8.0",
            "--log-dir", Path.Combine(_root, "logs")
        ]);

        var runner = new FakeProcessRunner();
        runner.EnqueueSuccess();

        var projectDir = new SeedProjectPreparer(runner, new RecordingReportWriter()).Prepare(options);
        var csproj = File.ReadAllText(Path.Combine(projectDir, "SeedProject.csproj"));

        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", csproj);
        Assert.Contains("Include=\"Newtonsoft.Json\" Version=\"13.0.3\"", csproj);
        Assert.Contains("Include=\"Dapper\" Version=\"2.1.35\"", csproj);
        Assert.DoesNotContain("13.0.1", csproj);
        Assert.Contains(runner.Calls, call => call.Arguments.Contains("restore"));
    }
}
