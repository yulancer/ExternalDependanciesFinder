using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.Tests.Services;

public sealed class PackageReferenceCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkg-collector-" + Guid.NewGuid().ToString("N"));

    public PackageReferenceCollectorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task CollectAsync_ReadsPackageReferencesFromCsproj()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="Dapper">
                  <Version>2.1.35</Version>
                </PackageReference>
                <PackageReference Include="Skip.Me" Version="$(Skip)" />
              </ItemGroup>
            </Project>
            """);

        var packages = await new PackageReferenceCollector().CollectAsync(_root, csproj);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(packages, p => p.Id == "Dapper" && p.Version == "2.1.35");
    }

    [Fact]
    public async Task CollectAsync_PrefersLockFile()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FromCsproj" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "packages.lock.json"), """
            {
              "targets": {
                "net8.0": {
                  "FromLock/2.0.0": {}
                }
              }
            }
            """);

        var packages = await new PackageReferenceCollector().CollectAsync(_root, csproj);

        Assert.Single(packages);
        Assert.Contains(packages, p => p.Id == "FromLock" && p.Version == "2.0.0");
    }

    [Fact]
    public async Task CollectAsync_UsesDirectoryPackagesProps()
    {
        var csproj = Path.Combine(_root, "App.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Central.Package" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "Directory.Packages.props"), """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Central.Package" Version="9.9.9" />
              </ItemGroup>
            </Project>
            """);

        var packages = await new PackageReferenceCollector().CollectAsync(_root, csproj);

        Assert.Single(packages);
        Assert.Equal("9.9.9", packages.First().Version);
    }
}
