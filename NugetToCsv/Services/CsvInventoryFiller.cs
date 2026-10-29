using NugetToCsv.Models;

namespace NugetToCsv.Services;

public static class CsvInventoryFiller
{
    public const string NameHeader = "Наименование ПО";
    public const string VersionHeader = "Версия ПО";
    public const string LinkHeader = "Прямая ссылка на скачивание";
    public const string IpHeader = "Список IP (где будет установлено ПО)";
    public const string RussianSoftwareHeader = "Российское ПО";

    public static IReadOnlyList<string> Fill(
        IReadOnlyList<string> templateLines,
        IReadOnlyList<PackageIdVersion> packages)
    {
        if (templateLines.Count < 3)
            throw new InvalidOperationException("CSV должен содержать минимум 3 строки: преамбулу, заголовки и строку-шаблон.");

        var headers = CsvLine.Parse(templateLines[1]);
        var templateRow = CsvLine.Parse(templateLines[2]);
        if (templateRow.Count < headers.Count)
        {
            templateRow.AddRange(Enumerable.Repeat(string.Empty, headers.Count - templateRow.Count));
        }

        var nameColumn = IndexOfHeader(headers, NameHeader);
        var versionColumn = IndexOfHeader(headers, VersionHeader);
        var linkColumn = IndexOfHeader(headers, LinkHeader);
        var ipColumn = IndexOfHeader(headers, IpHeader);
        var russianColumn = IndexOfHeader(headers, RussianSoftwareHeader);

        var output = new List<string>
        {
            templateLines[0],
            CsvLine.Join(headers)
        };

        foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var row = new List<string>(templateRow);
            Set(row, nameColumn, package.Id);
            Set(row, versionColumn, package.Version);
            Set(row, linkColumn, BuildNugetUrl(package.Id, package.Version));
            Set(row, ipColumn, "*");
            Set(row, russianColumn, "Нет");

            if (row.Count < headers.Count)
                row.AddRange(Enumerable.Repeat(string.Empty, headers.Count - row.Count));
            else if (row.Count > headers.Count)
                row = row.GetRange(0, headers.Count);

            output.Add(CsvLine.Join(row));
        }

        return output;
    }

    public static string BuildNugetUrl(string id, string version) =>
        "https://www.nuget.org/api/v2/package/" + id + "/" + version;

    public static string DefaultOutputPath(string templatePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(templatePath)) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(templatePath) + "_filled" + Path.GetExtension(templatePath);
        return Path.Combine(directory, name);
    }

    private static void Set(List<string> row, int column, string value)
    {
        if (column >= 0)
            row[column] = value;
    }

    private static int IndexOfHeader(List<string> headers, string name)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
