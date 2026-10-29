using NugetToCsv.Services;

namespace NugetToCsv.Tests.Services;

public sealed class ProjectPackageReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nuget-csv-proj-" + Guid.NewGuid().ToString("N"));

    public ProjectPackageReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Read_TakesPackageReferenceAndPackagesConfig()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="NoVersion" />
                <PackageReference Update="Dapper">
                  <Version>2.1.35</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "packages.config"), """
            <packages>
              <package id="Legacy.Lib" version="1.0.0" />
              <package id="Newtonsoft.Json" version="13.0.3" />
            </packages>
            """);

        var packages = ProjectPackageReader.Read(csproj);

        Assert.Equal(4, packages.Count);
        Assert.Contains(packages, p => p.Id == "Dapper" && p.Version == "2.1.35");
        Assert.Contains(packages, p => p.Id == "Legacy.Lib" && p.Version == "1.0.0");
        Assert.Contains(packages, p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(packages, p => p.Id == "NoVersion" && p.Version == "(no version specified)");
    }
}
