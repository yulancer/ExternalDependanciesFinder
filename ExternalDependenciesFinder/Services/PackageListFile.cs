using System.Text;
using NuGet.Versioning;

namespace ExternalDependenciesFinder.Services;

public sealed record PackageListEntry(string Id, string Version);

public static class PackageListFile
{
    public static IReadOnlyList<PackageListEntry> Read(string path)
    {
        var result = new List<PackageListEntry>();
        var lineNo = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var entry = ParseLine(line, lineNo);
            result.Add(entry);
        }

        return result;
    }

    public static void Write(string path, IEnumerable<PackageListEntry> packages)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = packages
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Id} => {p.Version}");

        var text = "# PackageId => Version" + Environment.NewLine
                   + string.Join(Environment.NewLine, lines)
                   + Environment.NewLine;
        File.WriteAllText(fullPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static PackageListEntry ParseLine(string line, int lineNo)
    {
        string id;
        string version;
        var arrow = line.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            id = line[..arrow].Trim();
            version = line[(arrow + 2)..].Trim();
        }
        else
        {
            var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException($"Строка {lineNo}: ожидается «PackageId => Version». {line}");

            id = parts[0];
            version = parts[1];
        }

        if (string.IsNullOrWhiteSpace(id) || !NuGetVersion.TryParse(version, out _))
            throw new FormatException($"Строка {lineNo}: ожидается «PackageId => Version». {line}");

        return new PackageListEntry(id, version);
    }
}
