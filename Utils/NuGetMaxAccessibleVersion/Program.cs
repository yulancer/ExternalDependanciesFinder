using System;
using System.Collections.Generic;
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
            PrintUsage();
            return 2;
        }

        bool listAll = HasFlag(args, "--list", "--all", "--list-all");
        bool includePrerelease = !HasFlag(args, "--no-prerelease", "--stable", "--stable-only");

        // Positional args (не начинаются с --):
        // 1) PackageId
        // 2) SourceUrlOrName (optional)
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positional.Length < 1)
        {
            PrintUsage();
            return 2;
        }

        var packageId = positional[0];
        var sourceFilter = positional.Length >= 2 ? positional[1] : null;

        var logger = NullLogger.Instance;
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var ct = cts.Token;

        if (listAll)
        {
            var versions = await GetAllAccessibleVersionsAsync(packageId, sourceFilter, includePrerelease, logger, ct);

            if (versions.Count == 0)
            {
                Console.WriteLine("No accessible versions found.");
                return 1;
            }

            // Выводим по одной на строку, от новых к старым
            foreach (var v in versions.OrderByDescending(v => v))
                Console.WriteLine(v.ToNormalizedString());

            return 0;
        }
        else
        {
            var best = await GetMaxAccessibleVersionAsync(packageId, sourceFilter, includePrerelease, logger, ct);

            if (best is null)
            {
                Console.WriteLine("No accessible version found.");
                return 1;
            }

            Console.WriteLine(best.ToNormalizedString());
            return 0;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  dotnet run -- <PackageId> [SourceUrlOrName] [--no-prerelease] [--list]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Flags:");
        Console.Error.WriteLine("  --no-prerelease | --stable | --stable-only   Exclude prerelease versions");
        Console.Error.WriteLine("  --list | --all | --list-all                  Print all accessible versions");
    }

    private static bool HasFlag(string[] args, params string[] flags) =>
        args.Any(a => flags.Any(f => a.Equals(f, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Returns the newest version that is реально доступна (nupkg скачивается без 401/403).
    /// If sourceFilter is provided, searches only that source (by name or by URL match).
    /// Otherwise searches all enabled sources and returns max among them.
    /// </summary>
    public static async Task<NuGetVersion?> GetMaxAccessibleVersionAsync(
        string packageId,
        string? sourceFilter,
        bool includePrerelease,
        ILogger logger,
        CancellationToken ct)
    {
        var sources = LoadEnabledSources(sourceFilter);
        if (sources.Count == 0)
            return null;

        var repoProvider = CreateRepoProvider(sources);

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
                continue;
            }

            NuGetVersion[] versions;
            try
            {
                var cache = new SourceCacheContext();
                versions = (await find.GetAllVersionsAsync(packageId, cache, logger, ct))
                    .Where(v => includePrerelease || !v.IsPrerelease)
                    .OrderByDescending(v => v)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var v in versions)
            {
                if (best != null && v <= best)
                    break;

                if (await CanDownloadNupkgAsync(find, packageId, v, logger, ct))
                {
                    best = v;
                    break; // newest accessible for this source
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Returns ALL versions that are реально доступны (nupkg скачивается без 401/403).
    /// Собирает со всех источников, дедуплицирует, сортировка по убыванию — у вызывающего.
    /// </summary>
    public static async Task<List<NuGetVersion>> GetAllAccessibleVersionsAsync(
        string packageId,
        string? sourceFilter,
        bool includePrerelease,
        ILogger logger,
        CancellationToken ct)
    {
        var sources = LoadEnabledSources(sourceFilter);
        if (sources.Count == 0)
            return new List<NuGetVersion>();

        var repoProvider = CreateRepoProvider(sources);

        var accessible = new HashSet<NuGetVersion>();

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
                continue;
            }

            IEnumerable<NuGetVersion> versions;
            try
            {
                var cache = new SourceCacheContext();
                versions = await find.GetAllVersionsAsync(packageId, cache, logger, ct);
            }
            catch
            {
                continue;
            }

            foreach (var v in versions)
            {
                if (!includePrerelease && v.IsPrerelease)
                    continue;

                // Чтобы не проверять одно и то же несколько раз
                if (!accessible.Add(v))
                    continue;

                // Реальная проверка доступности (403/401 пропускаем)
                if (!await CanDownloadNupkgAsync(find, packageId, v, logger, ct))
                {
                    // Если не доступна на этом source — возможно доступна на другом.
                    // Поэтому удаляем из set НЕ нужно: доступность подтверждаем успешной скачкой.
                    // Но мы уже добавили v в set — надо откатить:
                    accessible.Remove(v);
                }
            }
        }

        return accessible.OrderByDescending(v => v).ToList();
    }

    private static List<PackageSource> LoadEnabledSources(string? sourceFilter)
    {
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

        return sources;
    }

    private static SourceRepositoryProvider CreateRepoProvider(List<PackageSource> sources)
    {
        // Важно: используем те же settings/credentials, что и dotnet restore (ничего не меняем)
        // SourceRepositoryProvider создаётся от PackageSourceProvider,
        // но нам достаточно передать providers и потом CreateRepository(PackageSource).

        // Для CreateRepoProvider нужно иметь sourceProvider. Проще пересоздать здесь:
        ISettings settings = Settings.LoadDefaultSettings(
            root: Directory.GetCurrentDirectory(),
            configFileName: null,
            machineWideSettings: new XPlatMachineWideSetting());

        var sourceProvider = new PackageSourceProvider(settings);

        var providers = Repository.Provider.GetCoreV3();
        return new SourceRepositoryProvider(sourceProvider, providers);
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
            var cache = new SourceCacheContext();
            var ok = await find.CopyNupkgToStreamAsync(packageId, version, ms, cache, logger, ct);
            return ok;
        }
        catch (NuGetProtocolException ex) when (TryGetHttpStatus(ex, out var code) &&
                                               (code == HttpStatusCode.Forbidden || code == HttpStatusCode.Unauthorized))
        {
            return false;
        }
        catch (HttpRequestException ex) when (TryGetHttpStatus(ex, out var code) &&
                                              (code == HttpStatusCode.Forbidden || code == HttpStatusCode.Unauthorized))
        {
            return false;
        }
        catch
        {
            // Любые прочие ошибки считаем "не удалось подтвердить доступность"
            return false;
        }
    }

    private static bool TryGetHttpStatus(Exception ex, out HttpStatusCode status)
    {
        status = default;

        if (ex is HttpRequestException hre && hre.StatusCode is HttpStatusCode sc)
        {
            status = sc;
            return true;
        }

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
