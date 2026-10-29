using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PackageListParserTests
{
    private readonly PackageListParser _sut = new();

    [Fact]
    public void Parse_ExtractsPackageIdAndVersion()
    {
        const string stdout = """
            Project 'Demo' has the following package references
               [net8.0]:
               Top-level Package      Requested   Resolved
               > Newtonsoft.Json      13.0.1      13.0.3
               > Dapper               2.1.35      2.1.35

               Transitive Package                        Resolved
               > Microsoft.Extensions.Logging.Abstractions 8.0.2
            """;

        var packages = _sut.Parse(stdout);

        Assert.Equal(3, packages.Count);
        Assert.Contains(packages, p => p.PackageId == "Newtonsoft.Json" && p.Version == "13.0.3");
        Assert.Contains(packages, p => p.PackageId == "Dapper" && p.Version == "2.1.35");
        Assert.Contains(packages, p => p.PackageId == "Microsoft.Extensions.Logging.Abstractions" && p.Version == "8.0.2");
    }

    [Fact]
    public void Parse_SkipsExcludedPackages()
    {
        const string stdout = """
               > Rbp.Core      1.0.0
               > Newtonsoft.Json      13.0.3
            """;

        var packages = _sut.Parse(stdout, id => id.StartsWith("Rbp.", StringComparison.OrdinalIgnoreCase));

        Assert.Single(packages);
        Assert.Equal("Newtonsoft.Json", packages[0].PackageId);
    }

    [Fact]
    public void Parse_ReportsUnparseableLines()
    {
        const string stdout = """
               > BrokenPackage      not-a-version
               > Valid.Package      1.2.3
            """;

        var errors = new List<string>();
        var packages = _sut.Parse(stdout, onUnparseableLine: (line, _) => errors.Add(line));

        Assert.Single(packages);
        Assert.Equal("Valid.Package", packages[0].PackageId);
        Assert.Single(errors);
        Assert.Contains("BrokenPackage", errors[0]);
    }

    [Fact]
    public void Parse_IgnoresNonPackageLines()
    {
        const string stdout = """
            Something without arrow
            Top-level Package
            """;

        Assert.Empty(_sut.Parse(stdout));
    }
}
