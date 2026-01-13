using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class Program
{
    // $(MacroName)
    private static readonly Regex MacroUseRegex = new(@"\$\(([^)]+)\)", RegexOptions.Compiled);

    // 1.2.3 / 1.2.3.4 / 1.2.3-alpha.1 / 1.2.3+build
    private static readonly Regex LooksLikeVersionRegex =
        new(@"^\d+(\.\d+){1,3}([\-+][0-9A-Za-z\.\-_]+)?$",
            RegexOptions.Compiled);

    public static int Main(string[] args)
    {
        try
        {
            var opt = Options.Parse(args);

            var root = Path.GetFullPath(opt.RootFolder);
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"[ОШИБКА] Папка не найдена: {root}");
                return 2;
            }

            var packagesPath = Path.Combine(root, "Directory.Packages.props");
            var buildPath = Path.Combine(root, "Directory.Build.props");

            if (!File.Exists(packagesPath))
            {
                Console.Error.WriteLine($"[ОШИБКА] В папке нет Directory.Packages.props: {root}");
                return 2;
            }

            var report = new Report
            {
                RootFolder = root,
                PackagesInputPath = packagesPath,
                BuildPropsPath = File.Exists(buildPath) ? buildPath : null
            };

            // Загружаем XML, сохраняя пробелы/переносы строк
            var packagesDoc = LoadXmlPreserveWhitespace(packagesPath);
            XDocument? buildDoc = File.Exists(buildPath) ? LoadXmlPreserveWhitespace(buildPath) : null;

            // Собираем макросы версий из ОБОИХ файлов (Build + Packages)
            var macrosFromBuild = buildDoc is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : CollectVersionMacros(buildDoc);

            var macrosFromPackagesBefore = CollectVersionMacros(packagesDoc);

            // Объединяем:
            // если одинаковое имя макроса есть и там и там — предпочитаем значение из Directory.Packages.props
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in macrosFromBuild) merged[kv.Key] = kv.Value;

            foreach (var kv in macrosFromPackagesBefore)
            {
                if (merged.TryGetValue(kv.Key, out var prev) &&
                    !StringComparer.OrdinalIgnoreCase.Equals(prev, kv.Value))
                {
                    report.MacroValueConflicts.Add((kv.Key, prev, kv.Value));
                }
                merged[kv.Key] = kv.Value;
            }

            report.MacrosFoundInBuild = macrosFromBuild.Count;
            report.MacrosFoundInPackagesBefore = macrosFromPackagesBefore.Count;
            report.MacrosMergedTotal = merged.Count;

            // Обратный индекс: "значение версии" -> [имена макросов]
            var macrosByValue = merged
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // 1) Заменяем числовые версии в PackageVersion на $(Macro), если найден макрос с таким значением
            NormalizePackageVersions(packagesDoc, macrosByValue, report);

            // 2) Находим реально используемые макросы после замены (сканируем итоговый XML как текст)
            var packagesXmlAfterReplace = packagesDoc.ToString(SaveOptions.DisableFormatting);
            var usedMacros = new HashSet<string>(
                MacroUseRegex.Matches(packagesXmlAfterReplace).Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            report.UsedMacrosAfterNormalization = usedMacros.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            // 3) Удаляем НЕиспользуемые макросы ТОЛЬКО из Directory.Packages.props
            RemoveUnusedMacrosFromPackages(packagesDoc, usedMacros, report);

            // 4) Сортируем макросы в группах Directory.Packages.props по имени,
            //    сохраняя исходные переносы/отступы (не схлопываем в одну строку).
            SortMacroGroupsInPackagesPreserveFormatting(packagesDoc, report);

            // Пути вывода
            var packagesOutPath = opt.InPlace
                ? packagesPath
                : Path.GetFullPath(opt.OutputPath ?? Path.Combine(root, "Directory.Packages.normalized.props"));

            var reportPath = Path.GetFullPath(opt.ReportPath ?? Path.Combine(root, "normalize-report.md"));

            // ВАЖНО: сохраняем без переформатирования всего файла
            SaveXmlPreserveFormatting(packagesDoc, packagesOutPath);

            File.WriteAllText(reportPath, report.ToMarkdown(packagesOutPath), new UTF8Encoding(false));

            Console.WriteLine("[ГОТОВО] Нормализация завершена");
            Console.WriteLine($"        Файл: {packagesOutPath}");
            Console.WriteLine($"        Отчёт: {reportPath}");
            return 0;
        }
        catch (OptionsException ex)
        {
            Console.Error.WriteLine($"[АРГУМЕНТЫ] {ex.Message}");
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ОШИБКА] Необработанное исключение:");
            Console.Error.WriteLine(ex);
            return 99;
        }
    }

    // -------------------- Основная логика --------------------

    private static void NormalizePackageVersions(
        XDocument packagesDoc,
        Dictionary<string, List<string>> macrosByValue,
        Report report)
    {
        var pkgVersions = packagesDoc
            .Descendants()
            .Where(x => x.Name.LocalName == "PackageVersion")
            .ToList();

        foreach (var pv in pkgVersions)
        {
            var include = pv.Attribute("Include")?.Value?.Trim() ?? "";
            var verAttr = pv.Attribute("Version");
            if (verAttr is null) continue;

            var current = (verAttr.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(current)) continue;

            if (current.Contains("$("))
            {
                report.PackageVersionAlreadyMacro++;
                continue;
            }

            if (!LooksLikeVersionRegex.IsMatch(current))
            {
                report.PackageVersionsNonNumericLeftAsIs.Add((include, current));
                continue;
            }

            if (!macrosByValue.TryGetValue(current, out var macroNames) || macroNames.Count == 0)
            {
                report.PackageVersionsNoMacroLeftAsIs.Add((include, current));
                continue;
            }

            // Если найдено несколько макросов на одно значение — выбираем первый по алфавиту
            var chosen = macroNames[0];
            if (macroNames.Count > 1)
                report.AmbiguousMacroMatches.Add((include, current, macroNames, chosen));

            verAttr.Value = $"$({chosen})";
            report.ReplacedNumericWithMacro.Add((include, current, chosen));
        }
    }

    private static void RemoveUnusedMacrosFromPackages(
        XDocument packagesDoc,
        HashSet<string> usedMacros,
        Report report)
    {
        var propertyGroups = packagesDoc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup").ToList();

        foreach (var pg in propertyGroups)
        {
            var versionMacroElements = pg.Elements()
                .Where(IsVersionMacroElement)
                .ToList();

            foreach (var el in versionMacroElements)
            {
                var name = el.Name.LocalName;
                var value = (el.Value ?? "").Trim();

                if (usedMacros.Contains(name))
                    continue;

                // Удаляем "красиво": элемент + ближайший whitespace перед ним (если он только из пробелов/переносов)
                var prevNode = el.PreviousNode;
                el.Remove();

                if (prevNode is XText xt && string.IsNullOrWhiteSpace(xt.Value))
                    xt.Remove();

                report.RemovedUnusedMacrosFromPackages.Add((name, value));
            }

            // Если группа стала пустой — удаляем её и whitespace перед ней
            if (!pg.Elements().Any())
            {
                var prev = pg.PreviousNode;
                pg.Remove();

                if (prev is XText xt && string.IsNullOrWhiteSpace(xt.Value))
                    xt.Remove();

                report.RemovedEmptyPropertyGroups++;
            }
        }
    }

    private static void SortMacroGroupsInPackagesPreserveFormatting(XDocument packagesDoc, Report report)
    {
        var groups = packagesDoc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup").ToList();

        foreach (var pg in groups)
        {
            // Собираем "чанки" макросов: (leading whitespace текст, элемент)
            // leading whitespace — XText перед элементом, если он только из пробелов/переносов
            var nodes = pg.Nodes().ToList();
            var chunks = new List<MacroChunk>();

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not XElement el)
                    continue;

                if (!IsVersionMacroElement(el))
                    continue;

                string? leadingWs = null;
                if (i - 1 >= 0 && nodes[i - 1] is XText xt && string.IsNullOrWhiteSpace(xt.Value))
                    leadingWs = xt.Value;

                chunks.Add(new MacroChunk(el, leadingWs));
            }

            if (chunks.Count <= 1)
                continue;

            var before = chunks.Select(c => c.Element.Name.LocalName).ToList();

            var sorted = chunks
                .OrderBy(c => c.Element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var after = sorted.Select(c => c.Element.Name.LocalName).ToList();
            if (before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
                continue;

            // Находим место вставки: позиция первого элемента-макроса или его leading whitespace
            XNode insertionAnchor = FindFirstChunkAnchor(pg, chunks[0].Element);

            // Удаляем исходные элементы и их leading whitespace (если был)
            // Удаляем whitespace именно как узел в документе, а не по строке — поэтому ищем по соседству.
            foreach (var c in chunks)
            {
                var el = c.Element;
                var prev = el.PreviousNode;
                el.Remove();

                if (prev is XText xt && string.IsNullOrWhiteSpace(xt.Value))
                    xt.Remove();
            }

            // Вставляем отсортированные чанки перед якорем (или в конец, если якорь пропал)
            // Поскольку узлы уже вырезаны, insertionAnchor мог быть удалён — проверяем, что он ещё в дереве.
            if (insertionAnchor.Parent is null)
            {
                // fallback: просто добавляем в конец PropertyGroup
                foreach (var c in sorted)
                    AddChunkToEnd(pg, c);
            }
            else
            {
                foreach (var c in sorted)
                {
                    if (c.LeadingWhitespace is not null)
                        insertionAnchor.AddBeforeSelf(new XText(c.LeadingWhitespace));

                    insertionAnchor.AddBeforeSelf(c.Element);
                }
            }

            report.SortedMacroGroupsCount++;
        }

        static XNode FindFirstChunkAnchor(XElement pg, XElement firstMacroElement)
        {
            // Якорь = либо leading whitespace перед первым макросом (если есть),
            // либо сам элемент, либо последний вариант — первый узел группы.
            var prev = firstMacroElement.PreviousNode;
            if (prev is XText xt && string.IsNullOrWhiteSpace(xt.Value))
                return prev;

            return (XNode)firstMacroElement;
        }

        static void AddChunkToEnd(XElement pg, MacroChunk c)
        {
            if (c.LeadingWhitespace is not null)
                pg.Add(new XText(c.LeadingWhitespace));
            pg.Add(c.Element);
        }
    }

    private sealed class MacroChunk
    {
        public XElement Element { get; }
        public string? LeadingWhitespace { get; }

        public MacroChunk(XElement element, string? leadingWhitespace)
        {
            Element = element;
            LeadingWhitespace = leadingWhitespace;
        }
    }

    // Макрос версии:
    // - элемент внутри PropertyGroup
    // - БЕЗ атрибутов
    // - имя оканчивается на Version
    // - значение похоже на версию
    private static bool IsVersionMacroElement(XElement el)
    {
        if (el.HasAttributes) return false;

        var name = el.Name.LocalName;
        if (!name.EndsWith("Version", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = (el.Value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        return LooksLikeVersionRegex.IsMatch(value);
    }

    private static Dictionary<string, string> CollectVersionMacros(XDocument doc)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pg in doc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup"))
        {
            foreach (var el in pg.Elements())
            {
                if (!IsVersionMacroElement(el)) continue;
                dict[el.Name.LocalName] = (el.Value ?? "").Trim();
            }
        }

        return dict;
    }

    // -------------------- XML helpers --------------------

    private static XDocument LoadXmlPreserveWhitespace(string path)
    {
        using var fs = File.OpenRead(path);
        return XDocument.Load(fs, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private static void SaveXmlPreserveFormatting(XDocument doc, string path)
    {
        // DisableFormatting: сохраняет исходные XText (переносы/отступы), если мы их не уничтожили
        var xml = doc.ToString(SaveOptions.DisableFormatting);
        File.WriteAllText(path, xml, new UTF8Encoding(false));
    }

    // -------------------- Параметры + отчёт --------------------

    private sealed class Options
    {
        public string RootFolder { get; init; } = "";
        public bool InPlace { get; init; }
        public string? OutputPath { get; init; }
        public string? ReportPath { get; init; }

        public static string Usage =>
@"Использование:
  PropsNormalizer --root <папка> [--inplace] [--out <путь>] [--report <report.md>]

Что делает:
  - Читает <папка>\Directory.Packages.props (обязательно)
  - Читает <папка>\Directory.Build.props (если есть, только для поиска макросов)
  - Правит ТОЛЬКО Directory.Packages.props (или пишет в --out)
  - Макрос версии: имя оканчивается на ""Version"" и значение похоже на версию
  - Заменяет числовые версии PackageVersion на $(Macro), если найден макрос с таким значением
  - Удаляет неиспользуемые макросы версий ТОЛЬКО из Directory.Packages.props
  - Сортирует макросы версий в PropertyGroup по имени, сохраняя переносы/отступы
  - Пишет Markdown-отчёт

Примеры:
  PropsNormalizer --root . --inplace
  PropsNormalizer --root . --out .\Directory.Packages.normalized.props --report .\normalize-report.md
";

        public static Options Parse(string[] args)
        {
            string? root = null;
            bool inplace = false;
            string? outPath = null;
            string? report = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].Trim();
                switch (a)
                {
                    case "--root":
                        root = Next(i++, args, "--root");
                        break;
                    case "--inplace":
                        inplace = true;
                        break;
                    case "--out":
                        outPath = Next(i++, args, "--out");
                        break;
                    case "--report":
                        report = Next(i++, args, "--report");
                        break;
                    default:
                        throw new OptionsException($"Неизвестный аргумент: {a}");
                }
            }

            if (string.IsNullOrWhiteSpace(root))
                throw new OptionsException("Не задан обязательный аргумент --root");

            if (inplace && outPath is not null)
                throw new OptionsException("Нужно выбрать одно: либо --inplace, либо --out (не оба сразу).");

            return new Options
            {
                RootFolder = root!,
                InPlace = inplace,
                OutputPath = outPath,
                ReportPath = report
            };
        }

        private static string Next(int idx, string[] args, string name)
        {
            if (idx + 1 >= args.Length)
                throw new OptionsException($"Не задано значение для {name}");
            return args[idx + 1];
        }
    }

    private sealed class OptionsException : Exception
    {
        public OptionsException(string message) : base(message) { }
    }

    private sealed class Report
    {
        public string RootFolder { get; set; } = "";
        public string PackagesInputPath { get; set; } = "";
        public string? BuildPropsPath { get; set; }

        public int MacrosFoundInBuild { get; set; }
        public int MacrosFoundInPackagesBefore { get; set; }
        public int MacrosMergedTotal { get; set; }

        public int PackageVersionAlreadyMacro { get; set; }

        public List<(string Package, string OldVersion, string Macro)> ReplacedNumericWithMacro { get; } = new();
        public List<(string Package, string Version)> PackageVersionsNoMacroLeftAsIs { get; } = new();
        public List<(string Package, string Version)> PackageVersionsNonNumericLeftAsIs { get; } = new();

        public List<(string Package, string Version, IReadOnlyList<string> Macros, string Chosen)> AmbiguousMacroMatches { get; } = new();
        public List<(string Macro, string BuildValue, string PackagesValue)> MacroValueConflicts { get; } = new();

        public List<string> UsedMacrosAfterNormalization { get; set; } = new();

        public List<(string Macro, string Value)> RemovedUnusedMacrosFromPackages { get; } = new();
        public int RemovedEmptyPropertyGroups { get; set; }

        public int SortedMacroGroupsCount { get; set; }

        public string ToMarkdown(string packagesOutputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Отчёт о нормализации Directory.Packages.props");
            sb.AppendLine();
            sb.AppendLine("## Пути");
            sb.AppendLine();
            sb.AppendLine($"- Папка: `{Escape(RootFolder)}`");
            sb.AppendLine($"- Вход: `{Escape(PackagesInputPath)}`");
            sb.AppendLine($"- Выход: `{Escape(packagesOutputPath)}`");
            sb.AppendLine($"- Directory.Build.props (только для поиска макросов): {(BuildPropsPath is null ? "_не найден_" : $"`{Escape(BuildPropsPath)}`")}");
            sb.AppendLine();

            sb.AppendLine("## Поиск макросов версий");
            sb.AppendLine();
            sb.AppendLine($"- Найдено в Directory.Build.props: **{MacrosFoundInBuild}**");
            sb.AppendLine($"- Найдено в Directory.Packages.props (до правок): **{MacrosFoundInPackagesBefore}**");
            sb.AppendLine($"- Всего использовано для сопоставления (объединение): **{MacrosMergedTotal}**");
            sb.AppendLine();
            sb.AppendLine("> Макрос версии — это элемент в `<PropertyGroup>`, имя которого оканчивается на `Version`, а значение похоже на версию (например `1.2.3` или `1.2.3-alpha`).");
            sb.AppendLine();

            sb.AppendLine("## Итоги");
            sb.AppendLine();
            sb.AppendLine($"- Заменено числовых версий на макросы: **{ReplacedNumericWithMacro.Count}**");
            sb.AppendLine($"- Уже были макросы в PackageVersion (оставлено как есть): **{PackageVersionAlreadyMacro}**");
            sb.AppendLine($"- Числовые версии без подходящего макроса (оставлено как есть): **{PackageVersionsNoMacroLeftAsIs.Count}**");
            sb.AppendLine($"- Нечисловые версии (оставлено как есть): **{PackageVersionsNonNumericLeftAsIs.Count}**");
            sb.AppendLine($"- Неоднозначных совпадений: **{AmbiguousMacroMatches.Count}**");
            sb.AppendLine();
            sb.AppendLine($"- Удалено неиспользуемых макросов из Directory.Packages.props: **{RemovedUnusedMacrosFromPackages.Count}**");
            sb.AppendLine($"- Удалено пустых PropertyGroup: **{RemovedEmptyPropertyGroups}**");
            sb.AppendLine($"- Отсортировано групп макросов: **{SortedMacroGroupsCount}**");
            sb.AppendLine();

            if (MacroValueConflicts.Count > 0)
            {
                sb.AppendLine("## Конфликты значений макросов (Build vs Packages)");
                sb.AppendLine();
                sb.AppendLine("Один и тот же макрос имел разные значения. Для сопоставления предпочтение отдавалось значению из Directory.Packages.props.");
                sb.AppendLine();
                sb.AppendLine("| Макрос | Значение в Build | Значение в Packages |");
                sb.AppendLine("|---|---:|---:|");
                foreach (var c in MacroValueConflicts.OrderBy(x => x.Macro, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(c.Macro)}` | `{Escape(c.BuildValue)}` | `{Escape(c.PackagesValue)}` |");
                sb.AppendLine();
            }

            if (ReplacedNumericWithMacro.Count > 0)
            {
                sb.AppendLine("## Замены (числовая версия → макрос)");
                sb.AppendLine();
                sb.AppendLine("| Пакет | Было | Стало |");
                sb.AppendLine("|---|---:|---|");
                foreach (var r in ReplacedNumericWithMacro.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(r.Package)}` | `{Escape(r.OldVersion)}` | `$({Escape(r.Macro)})` |");
                sb.AppendLine();
            }

            if (AmbiguousMacroMatches.Count > 0)
            {
                sb.AppendLine("## Неоднозначные совпадения");
                sb.AppendLine();
                sb.AppendLine("Одинаковое значение версии найдено в нескольких макросах; выбран первый по алфавиту.");
                sb.AppendLine();
                foreach (var a in AmbiguousMacroMatches)
                {
                    sb.AppendLine($"- `{Escape(a.Package)}` версия `{Escape(a.Version)}` совпала с: {string.Join(", ", a.Macros.Select(m => $"`{Escape(m)}`"))}. Выбран: `{Escape(a.Chosen)}`");
                }
                sb.AppendLine();
            }

            if (RemovedUnusedMacrosFromPackages.Count > 0)
            {
                sb.AppendLine("## Удалённые неиспользуемые макросы (только из Directory.Packages.props)");
                sb.AppendLine();
                sb.AppendLine("| Макрос | Значение |");
                sb.AppendLine("|---|---:|");
                foreach (var rm in RemovedUnusedMacrosFromPackages.OrderBy(x => x.Macro, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(rm.Macro)}` | `{Escape(rm.Value)}` |");
                sb.AppendLine();
            }

            if (UsedMacrosAfterNormalization.Count > 0)
            {
                sb.AppendLine("## Используемые макросы после нормализации");
                sb.AppendLine();
                sb.AppendLine(string.Join(", ", UsedMacrosAfterNormalization.Select(x => $"`{Escape(x)}`")));
                sb.AppendLine();
            }

            if (PackageVersionsNoMacroLeftAsIs.Count > 0)
            {
                sb.AppendLine("## Оставлено как есть (нет подходящего макроса)");
                sb.AppendLine();
                sb.AppendLine("| Пакет | Версия |");
                sb.AppendLine("|---|---:|");
                foreach (var p in PackageVersionsNoMacroLeftAsIs.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(p.Package)}` | `{Escape(p.Version)}` |");
                sb.AppendLine();
            }

            if (PackageVersionsNonNumericLeftAsIs.Count > 0)
            {
                sb.AppendLine("## Оставлено как есть (версия не похожа на числовую)");
                sb.AppendLine();
                sb.AppendLine("| Пакет | Версия |");
                sb.AppendLine("|---|---:|");
                foreach (var p in PackageVersionsNonNumericLeftAsIs.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(p.Package)}` | `{Escape(p.Version)}` |");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("|", "\\|");
    }
}
