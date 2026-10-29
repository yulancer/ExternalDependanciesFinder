using NugetToCsv.Cli;

namespace NugetToCsv.Tests.Cli;

public sealed class OptionsTests
{
    [Fact]
    public void Parse_ReadsPackagesTemplateAndOutput()
    {
        var options = Options.Parse(new[]
        {
            "--packages", @"C:\a\unavailable-packages.txt",
            "--template", @"C:\a\form.csv",
            "--output", @"C:\a\filled.csv"
        });

        Assert.Equal(@"C:\a\unavailable-packages.txt", options.PackagesPath);
        Assert.Equal(@"C:\a\form.csv", options.TemplatePath);
        Assert.Equal(@"C:\a\filled.csv", options.OutputPath);
        Assert.Equal(string.Empty, options.ProjectPath);
    }

    [Fact]
    public void Parse_LegacyPositionals_AreProjectAndTemplate()
    {
        var options = Options.Parse(new[] { @"C:\a\App.csproj", @"C:\a\form.csv" });

        Assert.Equal(@"C:\a\App.csproj", options.ProjectPath);
        Assert.Equal(@"C:\a\form.csv", options.TemplatePath);
    }

    [Fact]
    public void Parse_ShowHelp()
    {
        Assert.True(Options.Parse(new[] { "--help" }).ShowHelp);
    }

    [Fact]
    public void Parse_Throws_WhenTemplateMissing()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Options.Parse(new[] { "--packages", "packages.txt" }));

        Assert.Contains("--template", ex.Message);
    }

    [Fact]
    public void Parse_Throws_WhenBothSourcesAreSet()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options.Parse(new[]
        {
            "--packages", "packages.txt",
            "--project", "App.csproj",
            "--template", "form.csv"
        }));

        Assert.Contains("--packages", ex.Message);
    }
}
