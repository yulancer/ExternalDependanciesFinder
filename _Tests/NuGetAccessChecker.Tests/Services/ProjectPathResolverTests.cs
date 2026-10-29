using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.Tests.Services;

public sealed class ProjectPathResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "path-resolver-" + Guid.NewGuid().ToString("N"));

    public ProjectPathResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ResolveProjectFile_FromFolder()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        File.WriteAllText(csproj, "<Project />");

        var resolved = ProjectPathResolver.ResolveProjectFile(_root);

        Assert.Equal(Path.GetFullPath(csproj), resolved);
    }

    [Fact]
    public void ResolveProjectFile_Throws_WhenFolderHasNoCsproj()
    {
        Assert.Throws<FileNotFoundException>(() => ProjectPathResolver.ResolveProjectFile(_root));
    }

    [Fact]
    public void ResolveProjectFile_Throws_WhenFileMissing()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ProjectPathResolver.ResolveProjectFile(Path.Combine(_root, "Missing.csproj")));
    }
}
