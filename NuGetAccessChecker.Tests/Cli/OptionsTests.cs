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
