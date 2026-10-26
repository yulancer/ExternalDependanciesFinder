using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class ScanTargetFinderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scan-target-" + Guid.NewGuid().ToString("N"));

    public ScanTargetFinderTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindTargets_PrefersSolutionInRoot()
    {
        File.WriteAllText(Path.Combine(_root, "B.sln"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "A.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "App.csproj"), "<Project />");

        var targets = new ScanTargetFinder().FindTargets(_root, Path.Combine(_root, "out"));

        Assert.Single(targets);
        Assert.Equal(ScanTargetKind.Solution, targets[0].Kind);
        Assert.EndsWith("A.sln", targets[0].TargetPath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(targets[0].IgnoredSolutions);
        Assert.EndsWith("B.sln", targets[0].IgnoredSolutions[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindTargets_ScansProjects_WhenNoSolution()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "App.csproj"), "<Project />");

        var outputDir = Path.Combine(_root, "generated");
        Directory.CreateDirectory(Path.Combine(outputDir, "Nested"));
        File.WriteAllText(Path.Combine(outputDir, "Nested", "Generated.csproj"), "<Project />");

        var targets = new ScanTargetFinder().FindTargets(_root, outputDir);

        Assert.Single(targets);
        Assert.Equal(ScanTargetKind.Project, targets[0].Kind);
        Assert.EndsWith("App.csproj", targets[0].TargetPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindTargets_ReturnsEmpty_WhenNothingFound()
    {
        var targets = new ScanTargetFinder().FindTargets(_root, Path.Combine(_root, "out"));
        Assert.Empty(targets);
    }
}
