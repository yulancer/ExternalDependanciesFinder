using ExternalDependenciesFinder.Cli;

namespace ExternalDependenciesFinder.Tests.Cli;

public sealed class OptionsTests
{
    [Fact]
    public void Parse_ShowHelp_WhenHelpFlagPassed()
    {
        var options = Options.Parse(["--help"]);

        Assert.True(options.ShowHelp);
    }

    [Fact]
    public void Parse_UsesDefaults_WhenOnlyScanRootProvided()
    {
        var scanRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var options = Options.Parse([scanRoot]);

        Assert.Equal(Path.GetFullPath(scanRoot), options.ScanRoot);
        Assert.Equal("net8.0", options.Framework);
        Assert.Equal("NugetUsingSolution", options.SolutionName);
        Assert.Equal("NugetUsingProject", options.ProjectName);
        Assert.False(options.ListOnly);
        Assert.False(options.NoBuild);
        Assert.Contains("Rbp.", options.ExcludePrefixes);
        Assert.Contains("Psb.", options.ExcludePrefixes);
        Assert.Equal(Path.Combine(options.ScanRoot, "packages_max.txt"), options.PackagesOutputPath);
    }

    [Fact]
    public void Parse_AppliesFlags()
    {
        var scanRoot = Path.GetTempPath();
        var outputDir = Path.Combine(Path.GetTempPath(), "out-deps");
        var packages = Path.Combine(Path.GetTempPath(), "pkgs.txt");
        var logs = Path.Combine(Path.GetTempPath(), "logs");

        var options = Options.Parse([
            scanRoot,
            "--output-dir", outputDir,
            "--packages-output", packages,
            "--log-dir", logs,
            "--framework", "net6.0",
            "--solution-name", "Sol",
            "--project-name", "Proj",
            "--exclude-prefix", "Abdt.,System.",
            "--list-only",
            "--no-build"
        ]);

        Assert.Equal(Path.GetFullPath(outputDir), options.OutputDirectory);
        Assert.Equal(Path.GetFullPath(packages), options.PackagesOutputPath);
        Assert.Equal(Path.GetFullPath(logs), options.LogDirectory);
        Assert.Equal("net6.0", options.Framework);
        Assert.Equal("Sol", options.SolutionName);
        Assert.Equal("Proj", options.ProjectName);
        Assert.True(options.ListOnly);
        Assert.True(options.NoBuild);
        Assert.Contains("Abdt.", options.ExcludePrefixes);
        Assert.Contains("System.", options.ExcludePrefixes);
    }

    [Fact]
    public void Parse_ClearDefaultExcludes_RemovesRbpAndPsb()
    {
        var options = Options.Parse(["--clear-default-excludes", "--exclude-prefix", "Company."]);

        Assert.DoesNotContain("Rbp.", options.ExcludePrefixes);
        Assert.DoesNotContain("Psb.", options.ExcludePrefixes);
        Assert.Contains("Company.", options.ExcludePrefixes);
    }

    [Fact]
    public void IsExcluded_RespectsPrefixCaseInsensitively()
    {
        var options = Options.Parse(["--clear-default-excludes", "--exclude-prefix", "Rbp."]);

        Assert.True(options.IsExcluded("rbp.Core"));
        Assert.False(options.IsExcluded("Newtonsoft.Json"));
    }

    [Fact]
    public void Parse_Throws_OnUnknownOption()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options.Parse(["--unknown"]));
        Assert.Contains("Неизвестный параметр", ex.Message);
    }

    [Fact]
    public void Parse_Throws_OnMissingOptionValue()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options.Parse(["--framework"]));
        Assert.Contains("--framework", ex.Message);
    }

    [Fact]
    public void Parse_Throws_OnMultipleScanRoots()
    {
        var ex = Assert.Throws<ArgumentException>(() => Options.Parse(["C:\\a", "C:\\b"]));
        Assert.Contains("больше одного пути", ex.Message);
    }
}
