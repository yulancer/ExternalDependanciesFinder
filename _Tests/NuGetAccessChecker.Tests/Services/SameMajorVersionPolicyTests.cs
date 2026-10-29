using NuGet.Versioning;
using NuGetAccessChecker.Services;

namespace NuGetAccessChecker.Tests.Services;

public sealed class SameMajorVersionPolicyTests
{
    [Theory]
    [InlineData("13.0.1", "13.0.1", true)]
    [InlineData("13.0.1", "13.0.3", true)]
    [InlineData("13.0.1", "13.5.0", true)]
    [InlineData("13.0.3", "13.0.1", false)]
    [InlineData("13.0.1", "14.0.0", false)]
    [InlineData("13.0.1", "12.9.9", false)]
    [InlineData("13.0.1", "13.0.2-beta", false)]
    [InlineData("13.0.1-beta", "13.0.1-beta", true)]
    [InlineData("13.0.1-beta", "13.0.2-beta", true)]
    public void IsCompatible_MatchesExpected(string required, string candidate, bool expected)
    {
        var result = SameMajorVersionPolicy.IsCompatible(
            NuGetVersion.Parse(required),
            NuGetVersion.Parse(candidate));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SelectBest_PrefersExactMatch_OverNewer()
    {
        var best = SameMajorVersionPolicy.SelectBest(
            NuGetVersion.Parse("13.0.1"),
            [
                NuGetVersion.Parse("13.0.3"),
                NuGetVersion.Parse("13.0.1"),
                NuGetVersion.Parse("13.5.0")
            ]);

        Assert.Equal(NuGetVersion.Parse("13.0.1"), best);
    }

    [Fact]
    public void SelectBest_ReturnsNewest_WhenExactMissing()
    {
        var best = SameMajorVersionPolicy.SelectBest(
            NuGetVersion.Parse("13.0.1"),
            [
                NuGetVersion.Parse("13.0.3"),
                NuGetVersion.Parse("13.2.0"),
                NuGetVersion.Parse("14.0.0"),
                NuGetVersion.Parse("12.0.0")
            ]);

        Assert.Equal(NuGetVersion.Parse("13.2.0"), best);
    }

    [Fact]
    public void SelectBest_ReturnsNull_WhenNoCompatible()
    {
        var best = SameMajorVersionPolicy.SelectBest(
            NuGetVersion.Parse("13.0.1"),
            [NuGetVersion.Parse("14.0.0"), NuGetVersion.Parse("12.0.0")]);

        Assert.Null(best);
    }
}
