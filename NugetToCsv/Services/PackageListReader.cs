using NugetToCsv.Models;

namespace NugetToCsv.Services;

public static class PackageListReader
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

        return Deduplicate(result);
    }

    public static IReadOnlyList<PackageIdVersion> Deduplicate(IEnumerable<PackageIdVersion> packages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PackageIdVersion>();
        foreach (var package in packages)
        {
            var key = package.Id + "\n" + package.Version;
            if (!seen.Add(key))
                continue;

            result.Add(package);
        }

        result.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
        return result;
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

        if (string.IsNullOrWhiteSpace(id) || !LooksLikeVersion(version))
            throw new FormatException("Строка " + lineNo + ": ожидается «PackageId => Version». " + line);

        return new PackageIdVersion(id, version);
    }

    private static bool LooksLikeVersion(string version) =>
        version.Length > 0 && char.IsDigit(version[0]) && version.IndexOf(' ') < 0;
}
