using NuGet.Versioning;
using NuGetAccessChecker.Infrastructure;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Services;

public sealed class PackageAccessChecker
{
    private readonly IPackageFeedClient _feedClient;
    private readonly TimeSpan _perSourceTimeout;

    public PackageAccessChecker(IPackageFeedClient feedClient, TimeSpan? perSourceTimeout = null)
    {
        _feedClient = feedClient;
        _perSourceTimeout = perSourceTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<PackageAccessResult> CheckAsync(
        PackageIdVersion package,
        IReadOnlyList<PackageSourceInfo> sources,
        CancellationToken cancellationToken = default)
    {
        var required = NuGetVersion.Parse(package.Version);
        var sourceMessages = new List<string>();

        foreach (var source in sources)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_perSourceTimeout);

                var allVersions = await _feedClient.GetAllVersionsAsync(source, package.Id, cts.Token);
                var candidate = SameMajorVersionPolicy.SelectBest(required, allVersions);

                if (candidate is null)
                {
                    sourceMessages.Add($"[{source.Name}] no compatible version in major {required.Major}");
                    continue;
                }

                // Проверяем скачиваемость начиная с лучшей (точная или max),
                // затем остальные совместимые по убыванию.
                var orderedCandidates = allVersions
                    .Where(v => SameMajorVersionPolicy.IsCompatible(required, v))
                    .Distinct()
                    .OrderByDescending(v => v == candidate)
                    .ThenByDescending(v => v)
                    .ToList();

                NuGetVersion? downloaded = null;
                foreach (var version in orderedCandidates)
                {
                    if (await _feedClient.CanDownloadAsync(source, package.Id, version, cts.Token))
                    {
                        downloaded = version;
                        break;
                    }
                }

                if (downloaded is null)
                {
                    sourceMessages.Add($"[{source.Name}] compatible versions listed but download failed");
                    continue;
                }

                sourceMessages.Add($"[{source.Name}] OK ({downloaded})");
                return new PackageAccessResult
                {
                    PackageId = package.Id,
                    RequiredVersion = required,
                    IsAvailable = true,
                    FoundVersion = downloaded,
                    SourceName = source.Name,
                    SourceUrl = source.Source,
                    SourceMessages = sourceMessages
                };
            }
            catch (Exception ex)
            {
                sourceMessages.Add($"[{source.Name}] ERROR: {ex.GetType().Name} - {ex.Message}");
            }
        }

        return new PackageAccessResult
        {
            PackageId = package.Id,
            RequiredVersion = required,
            IsAvailable = false,
            SourceMessages = sourceMessages
        };
    }
}
