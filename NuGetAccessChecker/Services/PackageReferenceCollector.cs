using System.Text.Json;
using System.Xml.Linq;
using NuGet.Versioning;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Services;

public sealed class PackageReferenceCollector
{
    public async Task<IReadOnlyCollection<PackageIdVersion>> CollectAsync(string projectDir, string projectPath)
    {
        var fromLock = await TryCollectFromLockFileAsync(projectDir);
        if (fromLock.Count > 0)
            return fromLock;

        return CollectFromProject(projectDir, projectPath);
    }

    private static async Task<HashSet<PackageIdVersion>> TryCollectFromLockFileAsync(string projectDir)
    {
        var result = new HashSet<PackageIdVersion>(PackageIdVersionComparer.Instance);
        var lockFile = Path.Combine(projectDir, "packages.lock.json");
        if (!File.Exists(lockFile))
            return result;

        try
        {
            await using var stream = File.OpenRead(lockFile);
            using var json = await JsonDocument.ParseAsync(stream);

            if (!json.RootElement.TryGetProperty("targets", out var targetsEl))
                return result;

            foreach (var tfmProp in targetsEl.EnumerateObject())
            {
                foreach (var pkgProp in tfmProp.Value.EnumerateObject())
                {
                    var parts = pkgProp.Name.Split('/');
                    if (parts.Length == 2)
                    {
                        AddIfValid(result, parts[0], parts[1]);
                        continue;
                    }

                    if (pkgProp.Value.TryGetProperty("resolved", out var resolvedEl))
                        AddIfValid(result, pkgProp.Name, resolvedEl.GetString());
                }
            }
        }
        catch
        {
            result.Clear();
        }

        return result;
    }

    private static HashSet<PackageIdVersion> CollectFromProject(string projectDir, string projectPath)
    {
        var result = new HashSet<PackageIdVersion>(PackageIdVersionComparer.Instance);
        var centralVersions = LoadCentralPackageVersions(projectDir);
        var csprojXml = XDocument.Load(projectPath);
        var ns = csprojXml.Root?.Name.Namespace ?? XNamespace.None;

        foreach (var pr in csprojXml.Descendants(ns + "PackageReference"))
        {
            var id = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var ver = pr.Attribute("Version")?.Value
                      ?? pr.Attribute("VersionOverride")?.Value
                      ?? pr.Element(ns + "Version")?.Value;

            if (string.IsNullOrWhiteSpace(ver) && centralVersions.TryGetValue(id!, out var central))
                ver = central;

            AddIfValid(result, id!, ver);
        }

        return result;
    }

    private static Dictionary<string, string> LoadCentralPackageVersions(string startDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(startDir);

        while (dir != null)
        {
            var propsPath = Path.Combine(dir.FullName, "Directory.Packages.props");
            if (File.Exists(propsPath))
            {
                try
                {
                    var xml = XDocument.Load(propsPath);
                    var ns = xml.Root?.Name.Namespace ?? XNamespace.None;
                    foreach (var pv in xml.Descendants(ns + "PackageVersion"))
                    {
                        var id = pv.Attribute("Include")?.Value;
                        var ver = pv.Attribute("Version")?.Value;
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                            result[id!] = ver!;
                    }
                }
                catch
                {
                    // ignore unreadable props
                }
            }

            dir = dir.Parent;
        }

        return result;
    }

    private static void AddIfValid(HashSet<PackageIdVersion> result, string? id, string? version)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            return;

        if (version.Contains('$') || !NuGetVersion.TryParse(version, out _))
            return;

        result.Add(new PackageIdVersion(id, version));
    }

    private sealed class PackageIdVersionComparer : IEqualityComparer<PackageIdVersion>
    {
        public static readonly PackageIdVersionComparer Instance = new();

        public bool Equals(PackageIdVersion? x, PackageIdVersion? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(PackageIdVersion obj) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id) * 397
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version);
    }
}
