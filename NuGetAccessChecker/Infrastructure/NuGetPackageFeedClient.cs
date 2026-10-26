using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Infrastructure;

public sealed class NuGetPackageFeedClient : IPackageFeedClient
{
    private readonly ILogger _logger;
    private readonly SourceCacheContext _cache = new();

    public NuGetPackageFeedClient(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(
        PackageSourceInfo source,
        string packageId,
        CancellationToken cancellationToken)
    {
        var find = await GetFindResourceAsync(source, cancellationToken);
        var versions = await find.GetAllVersionsAsync(packageId, _cache, _logger, cancellationToken);
        return versions.ToArray();
    }

    public async Task<bool> CanDownloadAsync(
        PackageSourceInfo source,
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        var find = await GetFindResourceAsync(source, cancellationToken);
        using var nupkgStream = new MemoryStream();
        var found = await find.CopyNupkgToStreamAsync(
            packageId,
            version,
            nupkgStream,
            _cache,
            _logger,
            cancellationToken);

        return found && nupkgStream.Length > 0;
    }

    private static async Task<FindPackageByIdResource> GetFindResourceAsync(
        PackageSourceInfo source,
        CancellationToken cancellationToken)
    {
        var repo = Repository.Factory.GetCoreV3(source.Source);
        return await repo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
    }
}
