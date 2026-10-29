using NugetToCsv.Services;

namespace NugetToCsv.Tests.Services;

public sealed class CsvLineTests
{
    [Fact]
    public void Parse_ReadsQuotedCommaAndEscapedQuote()
    {
        var fields = CsvLine.Parse("plain,\"a,b\",\"say \"\"hi\"\"\"");

        Assert.Equal(new[] { "plain", "a,b", "say \"hi\"" }, fields);
    }

    [Fact]
    public void Join_QuotesFieldsThatNeedIt()
    {
        var line = CsvLine.Join(new[] { "ok", "a,b", "say \"hi\"" });

        Assert.Equal("ok,\"a,b\",\"say \"\"hi\"\"\"", line);
        Assert.Equal(new[] { "ok", "a,b", "say \"hi\"" }, CsvLine.Parse(line));
    }

    [Fact]
    public void Parse_ReturnsEmptyFieldListForNull()
    {
        Assert.Empty(CsvLine.Parse(null));
    }
}
