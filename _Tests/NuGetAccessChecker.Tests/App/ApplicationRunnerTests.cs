using NuGetAccessChecker.App;
using NuGetAccessChecker.Cli;
using NuGetAccessChecker.Infrastructure;
using NuGetAccessChecker.Models;
using NuGetAccessChecker.Tests.Fakes;
using NSubstitute;

namespace NuGetAccessChecker.Tests.App;

public sealed class ApplicationRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "access-app-" + Guid.NewGuid().ToString("N"));

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
    public async Task RunAsync_WritesReport_WithCompatibleNewerVersion()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        var output = Path.Combine(_root, "report.txt");
        var options = Options.Parse(["--project", csproj, "--output", output]);

        var sources = Substitute.For<IPackageSourceProvider>();
        sources.GetEnabledSources(Arg.Any<string>()).Returns([
            new PackageSourceInfo("Internal", "https://nuget.internal/v3/index.json")
        ]);

        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "13.0.3");
        feed.SetDownloadable("Newtonsoft.Json", "13.0.3");

        var exitCode = await new ApplicationRunner(options, sources, feed).RunAsync();
        var report = await File.ReadAllTextAsync(output);

        Assert.Equal(0, exitCode);
        Assert.Contains("Newtonsoft.Json 13.0.1 -> 13.0.3", report);
        Assert.Contains("Недоступно:      0", report);
    }

    [Fact]
    public async Task RunAsync_PackagesFile_WritesUnavailableList()
    {
        var packagesFile = Path.Combine(_root, "packages_max.txt");
        await File.WriteAllTextAsync(packagesFile, """
            Newtonsoft.Json => 13.0.1
            Missing.Lib => 1.2.3
            """);

        var listPath = Path.Combine(_root, "unavailable-packages.txt");
        var reportPath = Path.Combine(_root, "unavailable-packages-report.txt");
        var options = Options.Parse([
            "--packages", packagesFile,
            "--list", listPath,
            "--output", reportPath
        ]);

        var sources = Substitute.For<IPackageSourceProvider>();
        sources.GetEnabledSources(Arg.Any<string>()).Returns([
            new PackageSourceInfo("Internal", "https://nuget.internal/v3/index.json")
        ]);

        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "13.0.3");
        feed.SetDownloadable("Newtonsoft.Json", "13.0.3");

        var exitCode = await new ApplicationRunner(options, sources, feed).RunAsync();
        var list = await File.ReadAllTextAsync(listPath);
        var report = await File.ReadAllTextAsync(reportPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Missing.Lib => 1.2.3", list);
        Assert.DoesNotContain("Newtonsoft.Json", list);
        Assert.Contains("СПИСОК:", report);
        Assert.Contains("Missing.Lib 1.2.3", report);
    }

    [Fact]
    public async Task RunAsync_ReturnsOne_WhenNoSources()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Dapper" Version="2.1.35" />
              </ItemGroup>
            </Project>
            """);

        var options = Options.Parse(["--project", csproj, "--output", Path.Combine(_root, "out.txt")]);
        var sources = Substitute.For<IPackageSourceProvider>();
        sources.GetEnabledSources(Arg.Any<string>()).Returns(Array.Empty<PackageSourceInfo>());

        var exitCode = await new ApplicationRunner(options, sources, new FakePackageFeedClient()).RunAsync();

        Assert.Equal(1, exitCode);
    }
}
