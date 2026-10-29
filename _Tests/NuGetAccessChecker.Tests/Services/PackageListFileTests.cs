using NuGetAccessChecker.Models;
using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.Tests.Services;

public sealed class PackageListFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "access-list-" + Guid.NewGuid().ToString("N"));

    public PackageListFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_ParsesArrowAndPlainLinesAndSkipsComments()
    {
        var path = Path.Combine(_root, "packages.txt");
        File.WriteAllText(path, "# comment\r\n\r\nNewtonsoft.Json => 13.0.3\r\nDapper 2.1.35\r\n");

        var packages = PackageListFile.Read(path);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(packages, p => p.Id == "Dapper" && p.Version == "2.1.35");
    }

    [Fact]
    public void Write_CreatesHeaderAndSkipsEmptyBody()
    {
        var path = Path.Combine(_root, "unavailable-packages.txt");
        PackageListFile.Write(path, Array.Empty<PackageIdVersion>());

        var packages = PackageListFile.Read(path);
        Assert.Empty(packages);
        Assert.Contains("PackageId => Version", File.ReadAllText(path));
    }
}
