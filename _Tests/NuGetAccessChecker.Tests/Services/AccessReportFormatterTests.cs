using NuGet.Versioning;
using NuGetAccessChecker.Models;
using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.Tests.Services;

public sealed class AccessReportFormatterTests
{
    [Fact]
    public void Format_IncludesCompatibleVersionNote()
    {
        var report = new AccessCheckReport
        {
            ProjectPath = @"C:\proj\App.csproj",
            GeneratedAt = new DateTime(2026, 1, 2, 3, 4, 5),
            Results =
            [
                new PackageAccessResult
                {
                    PackageId = "Newtonsoft.Json",
                    RequiredVersion = NuGetVersion.Parse("13.0.1"),
                    IsAvailable = true,
                    FoundVersion = NuGetVersion.Parse("13.0.3"),
                    SourceName = "Internal",
                    SourceUrl = "https://nuget.internal"
                },
                new PackageAccessResult
                {
                    PackageId = "Missing.Lib",
                    RequiredVersion = NuGetVersion.Parse("1.0.0"),
                    IsAvailable = false,
                    SourceMessages = ["[Internal] no compatible version in major 1"]
                }
            ]
        };

        var text = new AccessReportFormatter().Format(report);

        Assert.Contains("Доступно:        1", text);
        Assert.Contains("Недоступно:      1", text);
        Assert.Contains("Newtonsoft.Json 13.0.1 -> 13.0.3", text);
        Assert.Contains("Missing.Lib 1.0.0", text);
        Assert.Contains("no compatible version in major 1", text);
    }
}
