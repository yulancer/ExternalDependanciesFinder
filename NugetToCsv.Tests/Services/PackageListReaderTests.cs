using NugetToCsv.Services;

namespace NugetToCsv.Tests.Services;

public sealed class PackageListReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nuget-csv-list-" + Guid.NewGuid().ToString("N"));

    public PackageListReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_ParsesBothFormatsSkipsCommentsAndDropsDuplicates()
    {
        var path = Path.Combine(_root, "unavailable-packages.txt");
        File.WriteAllText(path, "# PackageId => Version\r\n\r\nNewtonsoft.Json => 13.0.3\r\nDapper 2.1.35\r\nnewtonsoft.json => 13.0.3\r\n");

        var packages = PackageListReader.Read(path);

        Assert.Equal(2, packages.Count);
        Assert.Equal("Dapper", packages[0].Id);
        Assert.Equal("Newtonsoft.Json", packages[1].Id);
    }

    [Fact]
    public void Read_Throws_WhenLineIsNotAPackage()
    {
        var path = Path.Combine(_root, "bad.txt");
        File.WriteAllText(path, "just text");

        var ex = Assert.Throws<FormatException>(() => PackageListReader.Read(path));
        Assert.Contains("PackageId => Version", ex.Message);
    }
}
