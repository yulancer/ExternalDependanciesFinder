using NuGet.Versioning;
using NuGetAccessChecker.Models;
using NuGetAccessChecker.Services;
using NuGetAccessChecker.Tests.Fakes;

namespace NuGetAccessChecker.Tests.Services;

public sealed class PackageAccessCheckerTests
{
    private static readonly PackageSourceInfo SourceA = new("Internal", "https://nuget.internal/v3/index.json");
    private static readonly PackageSourceInfo SourceB = new("Backup", "https://nuget.backup/v3/index.json");

    [Fact]
    public async Task CheckAsync_FindsExactVersion()
    {
        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "13.0.1", "13.0.3");
        feed.SetDownloadable("Newtonsoft.Json", "13.0.1", "13.0.3");

        var sut = new PackageAccessChecker(feed);
        var result = await sut.CheckAsync(new PackageIdVersion("Newtonsoft.Json", "13.0.1"), [SourceA]);

        Assert.True(result.IsAvailable);
        Assert.True(result.IsExactMatch);
        Assert.Equal(NuGetVersion.Parse("13.0.1"), result.FoundVersion);
        Assert.Equal("Internal", result.SourceName);
    }

    [Fact]
    public async Task CheckAsync_AcceptsNewerVersion_InSameMajor()
    {
        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "13.0.3", "14.0.0");
        feed.SetDownloadable("Newtonsoft.Json", "13.0.3", "14.0.0");

        var sut = new PackageAccessChecker(feed);
        var result = await sut.CheckAsync(new PackageIdVersion("Newtonsoft.Json", "13.0.1"), [SourceA]);

        Assert.True(result.IsAvailable);
        Assert.False(result.IsExactMatch);
        Assert.Equal(NuGetVersion.Parse("13.0.3"), result.FoundVersion);
    }

    [Fact]
    public async Task CheckAsync_RejectsOnlyNewerMajor()
    {
        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "14.0.0", "12.0.0");
        feed.SetDownloadable("Newtonsoft.Json", "14.0.0", "12.0.0");

        var sut = new PackageAccessChecker(feed);
        var result = await sut.CheckAsync(new PackageIdVersion("Newtonsoft.Json", "13.0.1"), [SourceA]);

        Assert.False(result.IsAvailable);
        Assert.Null(result.FoundVersion);
        Assert.Contains(result.SourceMessages, m => m.Contains("no compatible version"));
    }

    [Fact]
    public async Task CheckAsync_RejectsOlderVersion_InSameMajor()
    {
        var feed = new FakePackageFeedClient();
        feed.SetVersions("Newtonsoft.Json", "13.0.0");
        feed.SetDownloadable("Newtonsoft.Json", "13.0.0");

        var sut = new PackageAccessChecker(feed);
        var result = await sut.CheckAsync(new PackageIdVersion("Newtonsoft.Json", "13.0.1"), [SourceA]);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToNextSource()
    {
        var feed = new FakePackageFeedClient();
        feed.SetVersions("Dapper", "2.1.35");
        feed.SetDownloadable("Dapper", "2.1.35");

        // SourceA will list versions but fail download; SourceB succeeds via same fake
        // Simulate: first source has no versions by clearing after... better: use two packages approach
        // Actually Fake returns same for all sources. Use download failure for first attempt only is hard.
        // Simpler: SourceA has no versions - empty by default for unknown - wait, SetVersions is global.

        // Reimplement: if SourceA name fails download for all
        feed.SetDownloadFailure("Dapper", "2.1.35");

        // With download failure, both sources fail. Need per-source control.
        // For this test, use a custom feed instead.
        var custom = new SourceAwareFeedClient();
        var sut = new PackageAccessChecker(custom);

        var result = await sut.CheckAsync(new PackageIdVersion("Dapper", "2.1.0"), [SourceA, SourceB]);

        Assert.True(result.IsAvailable);
        Assert.Equal("Backup", result.SourceName);
        Assert.Equal(NuGetVersion.Parse("2.1.35"), result.FoundVersion);
    }

    [Fact]
    public async Task CheckAsync_TriesNextCompatible_WhenBestDownloadFails()
    {
        var feed = new FakePackageFeedClient();
        // Требуется 1.0.5 — точного совпадения нет, лучшая 1.2.0 недоступна для скачивания.
        feed.SetVersions("Lib", "1.2.0", "1.1.0", "1.0.0");
        feed.SetDownloadable("Lib", "1.1.0");
        feed.SetDownloadFailure("Lib", "1.2.0");

        var sut = new PackageAccessChecker(feed);
        var result = await sut.CheckAsync(new PackageIdVersion("Lib", "1.0.5"), [SourceA]);

        Assert.True(result.IsAvailable);
        Assert.Equal(NuGetVersion.Parse("1.1.0"), result.FoundVersion);
    }

    private sealed class SourceAwareFeedClient : NuGetAccessChecker.Infrastructure.IPackageFeedClient
    {
        public Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(
            PackageSourceInfo source,
            string packageId,
            CancellationToken cancellationToken)
        {
            if (source.Name == "Internal")
                return Task.FromResult<IReadOnlyList<NuGetVersion>>(Array.Empty<NuGetVersion>());

            return Task.FromResult<IReadOnlyList<NuGetVersion>>([NuGetVersion.Parse("2.1.35")]);
        }

        public Task<bool> CanDownloadAsync(
            PackageSourceInfo source,
            string packageId,
            NuGetVersion version,
            CancellationToken cancellationToken) =>
            Task.FromResult(source.Name == "Backup");
    }
}
