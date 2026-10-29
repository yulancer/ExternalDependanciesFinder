using System.Text;
using NugetToCsv.Services;

namespace NugetToCsv.Tests.Services;

public sealed class Utf8EncodingDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nuget-csv-enc-" + Guid.NewGuid().ToString("N"));

    public Utf8EncodingDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Detect_SeesBom()
    {
        var path = Path.Combine(_root, "bom.csv");
        File.WriteAllText(path, "a", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(3, Utf8EncodingDetector.Detect(path).GetPreamble().Length);
    }

    [Fact]
    public void Detect_OmitsBomWhenFileHasNone()
    {
        var path = Path.Combine(_root, "plain.csv");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("a"));

        Assert.Empty(Utf8EncodingDetector.Detect(path).GetPreamble());
    }
}
