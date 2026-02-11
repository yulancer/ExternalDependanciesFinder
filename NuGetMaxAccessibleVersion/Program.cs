using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

public static class NuGetMaxAccessibleVersion
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: dotnet run -- <PackageId> [SourceUrlOrName]");
            return 2;
        }

        var packageId = args[0];
        var sourceFilter = args.Length >= 2 ? args[1] : null;

        var logger = NullLogger.Instance;
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        var result = await GetMaxAccessibleVersionAsync(packageId, sourceFilter, logger, cts.Token);

        if (result is null)
        {
            Console.WriteLine("No accessible version found.");
            return 1;
        }

        Console.WriteLine(result.ToNormalizedString());
        return 0;
    }

    /// <summary>
    /// Returns the newest version that is реально доступна (nupkg скачивается без 401/403).
    /// If sourceFilter is provided, searches only that source (by name or by URL match).
    /// Otherwise searches all enabled sources and returns max among them.
    /// </summary>
    public static async Task<NuGetVersion?> GetMaxAccessibleVersionAsync(
        string packageId,
        string? sourceFilter,
        ILogger logger,
        CancellationToken ct)
    {
        // 1) Load current NuGet settings (does NOT modify config)
        ISettings settings = Settings.LoadDefaultSettings(
            root: Directory.GetCurrentDirectory(),
            configFileName: null,
            machineWideSettings: new XPlatMachineWideSetting());

        var sourceProvider = new PackageSourceProvider(settings);
        var sources = sourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();

        if (!string.IsNullOrWhiteSpace(sourceFilter))
        {
            sources = sources.Where(s =>
                    s.Name.Equals(sourceFilter, StringComparison.OrdinalIgnoreCase) ||
                    s.Source.IndexOf(sourceFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        if (sources.Count == 0)
            return null;

        // NuGet V3 providers
        var providers = Repository.Provider.GetCoreV3();
        var repoProvider = new SourceRepositoryProvider(sourceProvider, providers);

        NuGetVersion? best = null;

        foreach (var src in sources)
        {
            var repo = repoProvider.CreateRepository(src);

            FindPackageByIdResource find;
            try
            {
                find = await repo.GetResourceAsync<FindPackageByIdResource>(ct);
            }
            catch
            {
                // source not reachable / not a V3 feed / blocked
                continue;
            }

            NuGetVersion[] versions;
            try
            {
                var cache = new SourceCacheContext();
                versions = (await find.GetAllVersionsAsync(packageId, cache, logger, ct))
                    .OrderByDescending(v => v)
                    .ToArray();
            }
            catch
            {
                // metadata request may fail (auth/network/etc.)
                continue;
            }

            foreach (var v in versions)
            {
                if (best != null && v <= best)
                    break; // even if accessible, it can't beat current best

                if (await CanDownloadNupkgAsync(find, packageId, v, logger, ct))
                {
                    best = v;
                    break; // newest accessible for this source
                }
            }
        }

        return best;
    }

    private static async Task<bool> CanDownloadNupkgAsync(
        FindPackageByIdResource find,
        string packageId,
        NuGetVersion version,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            // If package exists and is downloadable, returns true and writes to stream
            var cache = new SourceCacheContext();
            var ok = await find.CopyNupkgToStreamAsync(packageId, version, ms, cacheContext: cache, logger, ct);
            return ok;
        }
        catch (NuGetProtocolException ex) when (TryGetHttpStatus(ex, out var code) && (code == HttpStatusCode.Forbidden || code == HttpStatusCode.Unauthorized))
        {
            return false;
        }
        catch (HttpRequestException ex) when (TryGetHttpStatus(ex, out var code) && (code == HttpStatusCode.Forbidden || code == HttpStatusCode.Unauthorized))
        {
            return false;
        }
        catch
        {
            // Другие ошибки (временный network, 5xx, etc.) — по желанию:
            // можно считать "не доступно" или пробрасывать наружу.
            return false;
        }
    }

    private static bool TryGetHttpStatus(Exception ex, out HttpStatusCode status)
    {
        status = default;

        // HttpRequestException in .NET 5+ может содержать StatusCode
        if (ex is HttpRequestException hre && hre.StatusCode is HttpStatusCode sc)
        {
            status = sc;
            return true;
        }

        // NuGetProtocolException часто заворачивает inner HttpRequestException
        var inner = ex.InnerException;
        while (inner != null)
        {
            if (inner is HttpRequestException ihre && ihre.StatusCode is HttpStatusCode isc)
            {
                status = isc;
                return true;
            }
            inner = inner.InnerException;
        }

        return false;
    }
}
