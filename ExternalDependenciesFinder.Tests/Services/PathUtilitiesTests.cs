using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PathUtilitiesTests
{
    [Fact]
    public void SplitTokens_SplitsOnWhitespace()
    {
        var tokens = PathUtilities.SplitTokens("  Newtonsoft.Json   13.0.1   13.0.3  ");

        Assert.Equal(["Newtonsoft.Json", "13.0.1", "13.0.3"], tokens);
    }

    [Fact]
    public void IsInsideDirectory_ReturnsTrue_ForNestedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "path-utils-root-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "sub", "file.csproj");

        Assert.True(PathUtilities.IsInsideDirectory(nested, root));
    }

    [Fact]
    public void IsInsideDirectory_ReturnsFalse_ForSiblingPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "path-utils-root-" + Guid.NewGuid().ToString("N"));
        var sibling = Path.Combine(Path.GetTempPath(), "other-" + Guid.NewGuid().ToString("N"), "file.csproj");

        Assert.False(PathUtilities.IsInsideDirectory(sibling, root));
    }

    [Fact]
    public void RecreateDirectory_DeletesExistingContent()
    {
        var path = Path.Combine(Path.GetTempPath(), "recreate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "old.txt"), "x");

        PathUtilities.RecreateDirectory(path);

        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFileSystemEntries(path));

        Directory.Delete(path, recursive: true);
    }
}
