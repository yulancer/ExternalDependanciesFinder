using System.Xml.Linq;
using NugetToCsv.Models;

namespace NugetToCsv.Services;

public static class ProjectPackageReader
{
    public static IReadOnlyList<PackageIdVersion> Read(string csprojPath)
    {
        var list = new List<PackageIdVersion>();
        ReadProject(csprojPath, list);
        ReadPackagesConfig(csprojPath, list);
        return PackageListReader.Deduplicate(list);
    }

    private static void ReadProject(string csprojPath, List<PackageIdVersion> list)
    {
        XDocument xdoc;
        try
        {
            xdoc = XDocument.Load(csprojPath);
        }
        catch
        {
            return;
        }

        var ns = xdoc.Root?.Name.Namespace ?? XNamespace.None;
        foreach (var pr in xdoc.Descendants(ns + "PackageReference"))
        {
            var id = (string?)pr.Attribute("Include") ?? (string?)pr.Attribute("Update");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var version = (string?)pr.Attribute("Version")
                          ?? (string?)pr.Element(ns + "Version")
                          ?? string.Empty;

            if (string.IsNullOrWhiteSpace(version))
                version = "(no version specified)";

            list.Add(new PackageIdVersion(id, version));
        }
    }

    private static void ReadPackagesConfig(string csprojPath, List<PackageIdVersion> list)
    {
        var directory = Path.GetDirectoryName(csprojPath);
        if (string.IsNullOrEmpty(directory))
            return;

        var packagesConfig = Path.Combine(directory, "packages.config");
        if (!File.Exists(packagesConfig))
            return;

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Load(packagesConfig);
        }
        catch
        {
            return;
        }

        foreach (var package in xdoc.Descendants("package"))
        {
            var id = (string?)package.Attribute("id");
            var version = (string?)package.Attribute("version");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                list.Add(new PackageIdVersion(id, version));
        }
    }
}
