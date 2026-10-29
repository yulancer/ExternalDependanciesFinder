using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PackageListFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkg-list-" + Guid.NewGuid().ToString("N"));

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
        File.WriteAllText(path, """
            # comment

            Newtonsoft.Json => 13.0.3
            Dapper 2.1.35
            """);

        var packages = PackageListFile.Read(path);

        Assert.Equal(2, packages.Count);
        Assert.Equal("Newtonsoft.Json", packages[0].Id);
        Assert.Equal("13.0.3", packages[0].Version);
        Assert.Equal("Dapper", packages[1].Id);
        Assert.Equal("2.1.35", packages[1].Version);
    }

    [Fact]
    public void Read_Throws_WhenVersionIsMissing()
    {
        var path = Path.Combine(_root, "packages.txt");
        File.WriteAllText(path, "Not a package line");

        var ex = Assert.Throws<FormatException>(() => PackageListFile.Read(path));
        Assert.Contains("PackageId => Version", ex.Message);
    }

    [Fact]
    public void Write_RoundTripsSortedLines()
    {
        var path = Path.Combine(_root, "out", "packages.txt");
        PackageListFile.Write(path,
        [
            new PackageListEntry("Newtonsoft.Json", "13.0.3"),
            new PackageListEntry("Dapper", "2.1.35")
        ]);

        var text = File.ReadAllText(path);
        var dapper = text.IndexOf("Dapper", StringComparison.Ordinal);
        var newtonsoft = text.IndexOf("Newtonsoft.Json", StringComparison.Ordinal);
        Assert.True(dapper >= 0 && dapper < newtonsoft);

        var packages = PackageListFile.Read(path);
        Assert.Equal(2, packages.Count);
    }
}
