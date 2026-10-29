using System.Text;
using NuGet.Versioning;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Services;

public static class PackageListFile
{
    public static IReadOnlyList<PackageIdVersion> Read(string path)
    {
        var result = new List<PackageIdVersion>();
        var lineNo = 0;

        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            result.Add(ParseLine(line, lineNo));
        }

        return result;
    }

    public static void Write(string path, IEnumerable<PackageIdVersion> packages)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var lines = new List<string>();
        foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            lines.Add(package.Id + " => " + package.Version);

        var text = "# PackageId => Version" + Environment.NewLine;
        if (lines.Count > 0)
            text += string.Join(Environment.NewLine, lines) + Environment.NewLine;

        File.WriteAllText(fullPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static PackageIdVersion ParseLine(string line, int lineNo)
    {
        string id;
        string version;
        var arrow = line.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            id = line.Substring(0, arrow).Trim();
            version = line.Substring(arrow + 2).Trim();
        }
        else
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new FormatException("Строка " + lineNo + ": ожидается «PackageId => Version». " + line);

            id = parts[0];
            version = parts[1];
        }

        if (string.IsNullOrWhiteSpace(id) || !NuGetVersion.TryParse(version, out _))
            throw new FormatException("Строка " + lineNo + ": ожидается «PackageId => Version». " + line);

        return new PackageIdVersion(id, version);
    }
}
