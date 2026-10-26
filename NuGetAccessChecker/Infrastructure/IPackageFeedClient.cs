using NuGet.Versioning;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Infrastructure;

public interface IPackageFeedClient
{
    Task<IReadOnlyList<NuGetVersion>> GetAllVersionsAsync(
        PackageSourceInfo source,
        string packageId,
        CancellationToken cancellationToken);

    Task<bool> CanDownloadAsync(
        PackageSourceInfo source,
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken);
}
