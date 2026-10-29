using NuGetAccessChecker.Cli;

namespace NuGetAccessChecker.Tests.Cli;

public sealed class OptionsTests
{
    [Fact]
    public void Parse_ReadsProjectAndOutput()
    {
        var options = Options.Parse(["--project", @"C:\a\App.csproj", "--output", @"C:\out.txt"]);

        Assert.Equal(@"C:\a\App.csproj", options.ProjectPath);
        Assert.Equal(@"C:\out.txt", options.OutputPath);
        Assert.False(options.ShowHelp);
    }

    [Fact]
    public void Parse_ShowHelp()
    {
        var options = Options.Parse(["--help"]);
        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_ReadsPackagesListAndReport()
    {
        var options = Options.Parse([
            "--packages", @"C:\a\packages_max.txt",
            "--list", @"C:\a\unavailable-packages.txt",
            "--output", @"C:\a\report.txt"
        ]);

        Assert.Equal(@"C:\a\packages_max.txt", options.PackagesPath);
        Assert.Equal(@"C:\a\unavailable-packages.txt", options.ListPath);
        Assert.Equal(@"C:\a\report.txt", options.OutputPath);
        Assert.Equal(string.Empty, options.ProjectPath);
    }

    [Fact]
    public void Parse_Throws_WhenBothSourcesAreSet()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Options.Parse(["--project", "App.csproj", "--packages", "packages.txt"]));

        Assert.Contains("--packages", ex.Message);
    }

    [Fact]
    public void Parse_Throws_WhenProjectMissing()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options.Parse([]));
        Assert.Contains("--project", ex.Message);
    }

    [Fact]
    public void Parse_Throws_OnUnknownOption()
    {
        Assert.Throws<ArgumentException>(() => Options.Parse(["--unknown"]));
    }
}
