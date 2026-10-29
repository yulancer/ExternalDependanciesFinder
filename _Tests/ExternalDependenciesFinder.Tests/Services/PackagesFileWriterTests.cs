using System.Text;
using ExternalDependenciesFinder.Models;
using ExternalDependenciesFinder.Services;
using ExternalDependenciesFinder.Tests.Fakes;
using NuGet.Versioning;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PackagesFileWriterTests
{
    [Fact]
    public void Write_CreatesSortedUtf8FileWithoutBom()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pkgs-writer-" + Guid.NewGuid().ToString("N"));
        var outputPath = Path.Combine(dir, "packages_max.txt");
        var report = new RecordingReportWriter();
        var console = new StringWriter();

        var packages = new Dictionary<string, PackageVersionInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Zebra"] = new PackageVersionInfo("1.0.0", NuGetVersion.Parse("1.0.0")),
            ["Alpha"] = new PackageVersionInfo("2.0.0", NuGetVersion.Parse("2.0.0"))
        };

        try
        {
            new PackagesFileWriter().Write(outputPath, packages, report, console);

            var bytes = File.ReadAllBytes(outputPath);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            var lines = File.ReadAllLines(outputPath, new UTF8Encoding(false));
            Assert.Equal(["Alpha => 2.0.0", "Zebra => 1.0.0"], lines);
            Assert.Contains(report.Infos, x => x.Contains("Packages count: 2"));
            Assert.Contains("Alpha => 2.0.0", console.ToString());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
