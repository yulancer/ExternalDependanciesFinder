using ExternalDependenciesFinder.Services;

namespace ExternalDependenciesFinder.Tests.Services;

public sealed class PackageAggregatorTests
{
    [Fact]
    public void AddOrUpdate_KeepsMaximumVersion()
    {
        var aggregator = new PackageAggregator();

        aggregator.AddOrUpdate("Newtonsoft.Json", "13.0.1");
        aggregator.AddOrUpdate("Newtonsoft.Json", "13.0.3");
        aggregator.AddOrUpdate("Newtonsoft.Json", "12.0.3");
        aggregator.AddOrUpdate("Dapper", "2.1.35");

        Assert.Equal(2, aggregator.Packages.Count);
        Assert.Equal("13.0.3", aggregator.Packages["Newtonsoft.Json"].OriginalVersion);
        Assert.Equal("2.1.35", aggregator.Packages["Dapper"].OriginalVersion);
    }

    [Fact]
    public void AddOrUpdate_IsCaseInsensitiveForPackageId()
    {
        var aggregator = new PackageAggregator();

        aggregator.AddOrUpdate("newtonsoft.json", "13.0.1");
        aggregator.AddOrUpdate("Newtonsoft.Json", "13.0.3");

        Assert.Single(aggregator.Packages);
        Assert.Equal("13.0.3", aggregator.Packages["NEWTONSOFT.JSON"].OriginalVersion);
    }

    [Fact]
    public void AddRange_AggregatesParsedLines()
    {
        var aggregator = new PackageAggregator();

        aggregator.AddRange([
            new ParsedPackageLine("A", "1.0.0"),
            new ParsedPackageLine("A", "2.0.0"),
            new ParsedPackageLine("B", "1.0.0")
        ]);

        Assert.Equal("2.0.0", aggregator.Packages["A"].OriginalVersion);
        Assert.Equal("1.0.0", aggregator.Packages["B"].OriginalVersion);
    }
}
