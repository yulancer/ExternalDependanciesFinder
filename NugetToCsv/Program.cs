using System.Xml.Linq;
using System.Text;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: NugetToCsv <path-to-csproj> <path-to-csv>");
                return 2;
            }

            var csprojPath = Path.GetFullPath(args[0]);
            var csvPath    = Path.GetFullPath(args[1]);

            if (!File.Exists(csprojPath))
                throw new FileNotFoundException("Project file not found", csprojPath);
            if (!File.Exists(csvPath))
                throw new FileNotFoundException("CSV file not found", csvPath);

            // 1) Читаем пакеты NuGet из проекта
            var packages = ReadNugetPackages(csprojPath);
            if (packages.Count == 0)
            {
                Console.WriteLine("No NuGet packages found.");
            }

            // 2) Читаем CSV (ожидается минимум 3 строки)
            var csvLines = File.ReadAllLines(csvPath, DetectEncoding(csvPath)).ToList();
            if (csvLines.Count < 3)
                throw new InvalidOperationException("CSV must have at least 3 lines: preface, headers, and a template row.");

            var headerFields = ParseCsvLine(csvLines[1]);
            var templateRow  = ParseCsvLine(csvLines[2]);

            if (templateRow.Count < headerFields.Count)
                // убедимся, что в шаблоне есть все столбцы
                templateRow.AddRange(Enumerable.Repeat("", headerFields.Count - templateRow.Count));

            // Сопоставляем столбцы по заголовкам
            int colName      = IndexOfHeader(headerFields, "Наименование ПО");
            int colVersion   = IndexOfHeader(headerFields, "Версия ПО");
            int colLink      = IndexOfHeader(headerFields, "Прямая ссылка на скачивание");
            int colIPs       = IndexOfHeader(headerFields, "Список IP (где будет установлено ПО)");
            int colRu        = IndexOfHeader(headerFields, "Российское ПО");

            // Формируем вывод
            var output = new List<string>();
            output.Add(csvLines[0]);                 // первая строка (префейс/заголовок)
            output.Add(JoinCsv(headerFields));       // заголовки

            foreach (var (id, version) in packages)
            {
                var row = new List<string>(templateRow); // копируем строку-шаблон
                if (colName    >= 0) row[colName]    = id;
                if (colVersion >= 0) row[colVersion] = version;
                if (colLink    >= 0) row[colLink]    = BuildNugetUrl(id, version);
                if (colIPs     >= 0) row[colIPs]     = "*";
                if (colRu      >= 0) row[colRu]      = "Нет";

                // нормализуем длину строки под заголовки
                if (row.Count < headerFields.Count)
                    row.AddRange(Enumerable.Repeat("", headerFields.Count - row.Count));
                else if (row.Count > headerFields.Count)
                    row = row.Take(headerFields.Count).ToList();

                output.Add(JoinCsv(row));
            }

            var outPath = Path.Combine(Path.GetDirectoryName(csvPath)!,
                                       Path.GetFileNameWithoutExtension(csvPath) + "_filled" + Path.GetExtension(csvPath));
            File.WriteAllLines(outPath, output, DetectEncoding(csvPath));

            Console.WriteLine($"Done. Wrote {packages.Count} rows to: {outPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static string BuildNugetUrl(string id, string version)
        => $"https://www.nuget.org/api/v2/package/{id}/{version}";

    // Читает как ссылки PackageReference в стиле SDK, так и устаревший packages.config (если есть).
    static List<(string id, string version)> ReadNugetPackages(string csprojPath)
    {
        var list = new List<(string, string)>();

        // Стиль SDK
        try
        {
            var xdoc = XDocument.Load(csprojPath);
            XNamespace ns = xdoc.Root?.Name.Namespace ?? XNamespace.None;
            var refs = xdoc.Descendants(ns + "PackageReference");
            foreach (var pr in refs)
            {
                var id = (string?)pr.Attribute("Include") ?? (string?)pr.Attribute("Update");
                if (string.IsNullOrWhiteSpace(id)) continue;

                var ver = (string?)pr.Attribute("Version")
                          ?? (string?)pr.Element(ns + "Version")
                          ?? string.Empty;

                // Если версия плавающая или пустая — оставляем как есть
                if (string.IsNullOrWhiteSpace(ver)) ver = "(no version specified)";

                list.Add((id!, ver));
            }
        }
        catch { /* игнорируем, попробуем также packages.config */ }

        // Устаревший packages.config в той же папке
        var packagesConfig = Path.Combine(Path.GetDirectoryName(csprojPath)!, "packages.config");
        if (File.Exists(packagesConfig))
        {
            try
            {
                var xdoc = XDocument.Load(packagesConfig);
                foreach (var p in xdoc.Descendants("package"))
                {
                    var id  = (string?)p.Attribute("id");
                    var ver = (string?)p.Attribute("version");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                        list.Add((id!, ver!));
                }
            }
            catch { /* игнорируем */ }
        }

        // Убираем дубликаты по (id,version)
        return list
            .Distinct()
            .OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static int IndexOfHeader(List<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
            if (string.Equals(headers[i]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return i;
        // заголовок не найден — это допустимо, соответствующие столбцы не трогаем
        return -1;
    }

    // --- Вспомогательные функции для CSV (запятые, кавычки) ---

    static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Экранированная кавычка?
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // пропускаем следующую
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    static string JoinCsv(IEnumerable<string> fields)
    {
        return string.Join(",", fields.Select(EscapeCsv));
    }

    static string EscapeCsv(string? value)
    {
        if (value == null) return "";
        bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // Пытаемся сохранить исходную кодировку CSV (обычно UTF-8 с/без BOM)
    static Encoding DetectEncoding(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length >= 3)
        {
            byte[] bom = new byte[3];
            fs.Read(bom, 0, 3);
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
        }
        return new UTF8Encoding(false);
    }
}