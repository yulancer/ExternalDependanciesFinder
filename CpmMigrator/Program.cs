using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static class Program
{
    // Требуемое имя центрального файла
    private const string CentralFileName = "Directory.Packages.props";
    private static readonly string[] BuildOutputMarkers = new[] { $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" };

    public static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        root = Path.GetFullPath(root);

        var report = new MdReport(root, DateTimeOffset.Now);

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"[CPM] Папка не существует: {root}");
            return 2;
        }

        // 1) Собираем файлы
        var buildProps = EnumerateFilesSafe(root, "Directory.Build.props")
            .Where(p => !IsUnderBuildOutput(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allProps = EnumerateFilesSafe(root, "*.props")
            .Where(p => !IsUnderBuildOutput(p))
            .Where(p => !p.EndsWith(Path.DirectorySeparatorChar + CentralFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projects = EnumerateFilesSafe(root, "*.csproj")
            .Where(p => !IsUnderBuildOutput(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        report.BuildPropsFound.AddRange(buildProps);
        report.PropsFound.AddRange(allProps);
        report.ProjectsFound.AddRange(projects);

        if (projects.Count == 0)
        {
            Console.WriteLine("[CPM] csproj не найдено. Нечего мигрировать.");
            WriteReport(root, report);
            return 0;
        }

        // 2) Извлекаем макросы *Version из Directory.Build.props (перенос в build.versions)
        var movedMacroBlocks = new List<string>(); // целые PropertyGroup блоки
        var movedMacroElements = new List<string>(); // отдельные элементы, если группа смешанная

        foreach (var bp in buildProps)
        {
            var original = File.ReadAllText(bp, Encoding.UTF8);
            var updated = original;

            // Сначала пробуем переносить целыми PropertyGroup блоками (если "чисто про версии")
            var groups = MsbuildText.FindTopLevelElements(updated, "PropertyGroup").ToList();

            var groupsToRemove = new List<(int start, int end, string text)>();

            foreach (var g in groups)
            {
                var inner = g.InnerText;

                // Найдём имена property элементов верхнего уровня внутри group (простая эвристика)
                var propNames = MsbuildText.FindDirectChildElementNames(inner).ToList();
                if (propNames.Count == 0) continue;

                var hasVersionProps = propNames.Any(n => n.EndsWith("Version", StringComparison.OrdinalIgnoreCase));
                if (!hasVersionProps) continue;

                // "Чистая" группа, если ВСЕ свойства — это *Version (разрешаем комментарии/пустое)
                var hasNonVersionProps = propNames.Any(n => !n.EndsWith("Version", StringComparison.OrdinalIgnoreCase));
                if (!hasNonVersionProps)
                {
                    groupsToRemove.Add((g.StartIndex, g.EndIndexExclusive, g.OuterText));
                }
            }

            // Удаляем группы с конца к началу (чтобы индексы не поплыли)
            if (groupsToRemove.Count > 0)
            {
                foreach (var rem in groupsToRemove.OrderByDescending(x => x.start))
                {
                    movedMacroBlocks.Add(NormalizeBlockForCentral(rem.text));
                    updated = updated.Remove(rem.start, rem.end - rem.start);
                    report.MacrosMovedGroups++;
                }
            }

            // Если есть смешанные группы — переносим отдельные элементы *Version
            // (делаем после удаления целых групп, чтобы не переносить дважды)
            var groups2 = MsbuildText.FindTopLevelElements(updated, "PropertyGroup").ToList();
            bool changed = groupsToRemove.Count > 0;

            foreach (var g in groups2)
            {
                var inner = g.InnerText;

                // Находим элементы вида <XxxVersion>...</XxxVersion> (простая, но рабочая эвристика)
                var versionElements = MsbuildText.FindDirectChildElements(inner)
                    .Where(e => e.Name.EndsWith("Version", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (versionElements.Count == 0) continue;

                // переносим каждый элемент, удаляем из исходника
                var innerUpdated = inner;
                foreach (var ve in versionElements.OrderByDescending(v => v.StartIndex))
                {
                    movedMacroElements.Add(ve.OuterText.TrimEnd());
                    innerUpdated = innerUpdated.Remove(ve.StartIndex, ve.EndIndexExclusive - ve.StartIndex);
                    report.MacrosMovedElements++;
                    changed = true;
                }

                if (!ReferenceEquals(innerUpdated, inner))
                {
                    // пересобираем PropertyGroup с тем же outer, заменив inner кусок
                    updated = updated.Remove(g.StartIndex, g.EndIndexExclusive - g.StartIndex)
                        .Insert(g.StartIndex, g.PrefixBeforeInner + innerUpdated + g.SuffixAfterInner);
                }
            }

            if (!string.Equals(updated, original, StringComparison.Ordinal))
            {
                File.WriteAllText(bp, updated, Encoding.UTF8);
                report.FilesModified.Add(bp);

                // после изменения добавим Import (можно сразу здесь)
            }

            // 3) Гарантируем Import build.versions в каждом Directory.Build.props
            var afterImport = EnsureImportBuildVersions(File.ReadAllText(bp, Encoding.UTF8));
            if (!string.Equals(afterImport, File.ReadAllText(bp, Encoding.UTF8), StringComparison.Ordinal))
            {
                File.WriteAllText(bp, afterImport, Encoding.UTF8);
                if (!report.FilesModified.Contains(bp, StringComparer.OrdinalIgnoreCase))
                    report.FilesModified.Add(bp);
                report.ImportsAdded++;
            }
        }

        // 4) Обрабатываем csproj: удаляем версии и собираем их в центральный список PackageVersion
        var centralVersions = new CentralVersionsMap(report);

        foreach (var proj in projects)
        {
            var original = File.ReadAllText(proj, Encoding.UTF8);
            var updated = original;

            var result = CsprojMigrator.RemovePackageReferenceVersionsAndCollect(updated, centralVersions, proj, report);
            updated = result.UpdatedText;

            if (!string.Equals(updated, original, StringComparison.Ordinal))
            {
                File.WriteAllText(proj, updated, Encoding.UTF8);
                report.FilesModified.Add(proj);
                report.ProjectsModified++;
            }
        }

        // 5) Пишем build.versions в корень
        var centralPath = Path.Combine(root, CentralFileName);

        var centralContent = CentralFileWriter.BuildCentralFileText(
            movedMacroBlocks: movedMacroBlocks,
            movedMacroElements: movedMacroElements,
            packageVersions: centralVersions.GetOrderedPackageVersions());

        File.WriteAllText(centralPath, centralContent, Encoding.UTF8);
        report.FilesCreatedOrOverwritten.Add(centralPath);

        // 6) Итоговый отчет
        WriteReport(root, report);

        Console.WriteLine("[CPM] Готово.");
        Console.WriteLine($"[CPM] Создан: {centralPath}");
        Console.WriteLine($"[CPM] Отчет: {Path.Combine(root, "report.md")}");
        return 0;
    }

    private static void WriteReport(string root, MdReport report)
    {
        var path = Path.Combine(root, "report.md");
        File.WriteAllText(path, report.ToMarkdown(), Encoding.UTF8);
        report.ReportPath = path;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            // на случай проблем доступа — попробуем более безопасный обход
            var list = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly))
                        list.Add(f);

                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        stack.Push(sub);
                }
                catch
                {
                    // игнорируем недоступные папки
                }
            }

            return list;
        }
    }

    private static bool IsUnderBuildOutput(string path)
    {
        var p = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return BuildOutputMarkers.Any(m => p.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string EnsureImportBuildVersions(string text)
    {
        // уже есть импорт? (более гибкий поиск)
        if (Regex.IsMatch(text, @"<\s*Import\b[^>]*\bProject\s*=\s*""build\.versions""", RegexOptions.IgnoreCase))
            return text;

        // ищем <Project ...>
        var projectMatch = Regex.Match(text, @"<\s*Project\b[^>]*>", RegexOptions.IgnoreCase);
        if (!projectMatch.Success) return text;

        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var importLine = $"  <Import Project=\"build.versions\" Condition=\"Exists('$(MSBuildThisFileDirectory)build.versions')\" />";

        // Попробуем вставить перед первой PropertyGroup или в самое начало (после <Project>)
        var pgMatch = Regex.Match(text, @"<\s*PropertyGroup\b[^>]*>", RegexOptions.IgnoreCase);
        if (pgMatch.Success && pgMatch.Index > projectMatch.Index)
        {
            // Вставляем перед PropertyGroup с сохранением отступа (предположим 2 пробела)
            return text.Insert(pgMatch.Index, importLine + nl + nl + "  ");
        }

        return text.Insert(projectMatch.Index + projectMatch.Length, nl + nl + importLine + nl);
    }

    private static string NormalizeBlockForCentral(string block)
    {
        // В центральный файл переносим как есть, но:
        // - убираем ведущие/хвостовые пустые строки
        // - не добавляем xmlns и т.п.
        return block.Trim('\r', '\n');
    }
}

internal sealed class MdReport
{
    public MdReport(string root, DateTimeOffset startedAt)
    {
        Root = root;
        StartedAt = startedAt;
    }

    public string Root { get; }
    public DateTimeOffset StartedAt { get; }
    public string? ReportPath { get; set; }

    public List<string> BuildPropsFound { get; } = new();
    public List<string> PropsFound { get; } = new();
    public List<string> ProjectsFound { get; } = new();

    public List<string> FilesModified { get; } = new();
    public List<string> FilesCreatedOrOverwritten { get; } = new();

    public int MacrosMovedGroups { get; set; }
    public int MacrosMovedElements { get; set; }
    public int ImportsAdded { get; set; }
    public int ProjectsModified { get; set; }

    public int PackageVersionsCollected { get; set; }
    public int PackageVersionConflicts { get; set; }
    public List<string> Conflicts { get; } = new();

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Отчет миграции на CPM");
        sb.AppendLine();
        sb.AppendLine($"- Папка: `{Root}`");
        sb.AppendLine($"- Время: `{StartedAt:yyyy-MM-dd HH:mm:ss zzz}`");
        sb.AppendLine();

        sb.AppendLine("## Найдено");
        sb.AppendLine();
        sb.AppendLine($"- Directory.Build.props: **{BuildPropsFound.Count}**");
        foreach (var p in BuildPropsFound) sb.AppendLine($"  - `{p}`");
        sb.AppendLine();
        sb.AppendLine($"- props файлов всего: **{PropsFound.Count}**");
        sb.AppendLine($"- csproj: **{ProjectsFound.Count}**");
        sb.AppendLine();

        sb.AppendLine("## Сделано");
        sb.AppendLine();
        sb.AppendLine($"- Перенесено PropertyGroup с макросами *Version: **{MacrosMovedGroups}**");
        sb.AppendLine($"- Перенесено отдельных элементов *Version из смешанных групп: **{MacrosMovedElements}**");
        sb.AppendLine($"- Добавлено Import build.versions в Directory.Build.props: **{ImportsAdded}**");
        sb.AppendLine($"- Проектов (csproj) изменено: **{ProjectsModified}**");
        sb.AppendLine($"- Собрано PackageVersion: **{PackageVersionsCollected}**");
        sb.AppendLine();

        sb.AppendLine("## Измененные/созданные файлы");
        sb.AppendLine();
        if (FilesCreatedOrOverwritten.Count > 0)
        {
            sb.AppendLine("### Созданы/перезаписаны");
            foreach (var p in FilesCreatedOrOverwritten.Distinct(StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- `{p}`");
            sb.AppendLine();
        }

        if (FilesModified.Count > 0)
        {
            sb.AppendLine("### Изменены");
            foreach (var p in FilesModified.Distinct(StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- `{p}`");
            sb.AppendLine();
        }

        if (PackageVersionConflicts > 0)
        {
            sb.AppendLine("## Конфликты версий");
            sb.AppendLine();
            sb.AppendLine($"Обнаружено конфликтов: **{PackageVersionConflicts}**");
            foreach (var c in Conflicts) sb.AppendLine($"- {c}");
            sb.AppendLine();
        }

        sb.AppendLine("## Примечания");
        sb.AppendLine();
        sb.AppendLine("- Приложение старается не менять форматирование, но при удалении отдельных элементов из смешанных `PropertyGroup` возможно появление лишних пустых строк.");
        sb.AppendLine("- XML схемы/`xmlns` не добавляются.");
        sb.AppendLine();

        return sb.ToString();
    }
}

internal sealed class CentralVersionsMap
{
    private readonly Dictionary<string, string> _pkgToVersion = new(StringComparer.OrdinalIgnoreCase);
    private readonly MdReport _report;

    public CentralVersionsMap(MdReport report)
    {
        _report = report;
    }

    public void AddOrUpdate(string packageId, string version, string sourceHint)
    {
        packageId = packageId.Trim();
        version = version.Trim();

        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            return;

        if (_pkgToVersion.TryGetValue(packageId, out var existing))
        {
            if (!string.Equals(existing, version, StringComparison.OrdinalIgnoreCase))
            {
                // конфликт: оставляем "более высокую" если можем сравнить, иначе оставляем существующую
                var chosen = ChooseBetterVersion(existing, version);
                var rejected = string.Equals(chosen, existing, StringComparison.OrdinalIgnoreCase) ? version : existing;

                _pkgToVersion[packageId] = chosen;
                _report.PackageVersionConflicts++;
                _report.Conflicts.Add($"`{packageId}`: конфликт версий `{existing}` vs `{version}` (источник: {sourceHint}). Выбрано: `{chosen}`, отброшено: `{rejected}`.");
            }
            return;
        }

        _pkgToVersion[packageId] = version;
        _report.PackageVersionsCollected++;
    }

    public IReadOnlyList<(string PackageId, string Version)> GetOrderedPackageVersions()
        => _pkgToVersion
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    private static string ChooseBetterVersion(string a, string b)
    {
        // Если версии — макросы или сложные выражения, сравнивать нечего: оставим "a"
        if (LooksLikeMsbuildExpression(a) || LooksLikeMsbuildExpression(b))
            return a;

        // Упрощенный NuGet-ish compare: сравним по числам (major.minor.patch...)
        var va = TryParseLooseVersion(a);
        var vb = TryParseLooseVersion(b);

        if (va != null && vb != null)
            return vb > va ? b : a;

        return a;
    }

    private static bool LooksLikeMsbuildExpression(string v)
        => v.Contains("$(", StringComparison.Ordinal) || v.Contains("%(", StringComparison.Ordinal);

    private static Version? TryParseLooseVersion(string s)
    {
        // выкусываем пререлизы/метаданные
        var core = s.Split('-', '+')[0].Trim();
        var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // добьём до 4 частей
        var nums = new List<int>();
        foreach (var p in parts.Take(4))
        {
            if (!int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return null;
            nums.Add(n);
        }
        while (nums.Count < 2) nums.Add(0);
        while (nums.Count < 4) nums.Add(0);

        return new Version(nums[0], nums[1], nums[2], nums[3]);
    }
}

internal static class CentralFileWriter
{
    public static string BuildCentralFileText(
        List<string> movedMacroBlocks,
        List<string> movedMacroElements,
        IReadOnlyList<(string PackageId, string Version)> packageVersions)
    {
        var nl = "\r\n";

        var sb = new StringBuilder();

        // без XML declaration, без xmlns
        sb.Append("<Project>");
        sb.Append(nl);
        sb.Append(nl);

        // 1) CPM секция (3 тега)
        sb.Append("  <PropertyGroup>");
        sb.Append(nl);
        sb.Append("    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        sb.Append(nl);
        sb.Append("    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>");
        sb.Append(nl);
        sb.Append("    <CentralPackageVersionOverrideEnabled>true</CentralPackageVersionOverrideEnabled>");
        sb.Append(nl);
        sb.Append("  </PropertyGroup>");
        sb.Append(nl);
        sb.Append(nl);

        // 2) Перенесённые целые PropertyGroup блоки с макросами
        if (movedMacroBlocks.Count > 0)
        {
            sb.Append("  <!-- Макросы версий, перенесенные из Directory.Build.props -->");
            sb.Append(nl);

            foreach (var block in movedMacroBlocks)
            {
                // Вставляем как есть, но обеспечим отступ 2 пробела для корня
                sb.Append(IndentBlock(block, "  "));
                sb.Append(nl);
                sb.Append(nl);
            }
        }

        // 3) Если переносили отдельные элементы *Version (из смешанных групп), положим их в отдельную PropertyGroup
        if (movedMacroElements.Count > 0)
        {
            sb.Append("  <PropertyGroup>");
            sb.Append(nl);
            sb.Append("    <!-- Макросы версий, извлеченные из смешанных PropertyGroup -->");
            sb.Append(nl);

            foreach (var el in movedMacroElements)
            {
                // Попробуем сохранить исходный отступ, но гарантируем что внутри PG будет минимум 4 пробела
                var trimmed = el.Trim();
                sb.Append("    ");
                sb.Append(trimmed);
                sb.Append(nl);
            }

            sb.Append("  </PropertyGroup>");
            sb.Append(nl);
            sb.Append(nl);
        }

        // 4) Пакетные версии
        sb.Append("  <ItemGroup>");
        sb.Append(nl);

        foreach (var (pkg, ver) in packageVersions)
        {
            sb.Append("    <PackageVersion Include=\"");
            sb.Append(EscapeXmlAttr(pkg));
            sb.Append("\" Version=\"");
            sb.Append(EscapeXmlAttr(ver));
            sb.Append("\" />");
            sb.Append(nl);
        }

        sb.Append("  </ItemGroup>");
        sb.Append(nl);
        sb.Append(nl);

        sb.Append("</Project>");
        sb.Append(nl);

        return sb.ToString();
    }

    private static string IndentBlock(string block, string indent)
    {
        var nl = block.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return string.Join(nl, lines.Select(l => indent + l));
    }

    private static string EscapeXmlAttr(string s)
        => s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

internal static class CsprojMigrator
{
    public sealed record Result(string UpdatedText);

    public static Result RemovePackageReferenceVersionsAndCollect(
        string text,
        CentralVersionsMap central,
        string projectPath,
        MdReport report)
    {
        var updated = text;

        // 1) Сначала обрабатываем PackageReference с вложенными тегами
        // Используем балансировку или убеждаемся, что внутри нет других PackageReference
        updated = Regex.Replace(updated,
            @"<\s*PackageReference\b(?<attrs>[^>]*)>(?<inner>(?:(?!<\s*PackageReference\b).)*?)<\s*/\s*PackageReference\s*>",
            m =>
            {
                var attrs = m.Groups["attrs"].Value;
                var inner = m.Groups["inner"].Value;

                var include = GetAttr(attrs, "Include") ?? GetAttr(attrs, "Update");

                if (!string.IsNullOrWhiteSpace(include))
                {
                    var v = FindInnerTagValue(inner, "Version");
                    var vo = FindInnerTagValue(inner, "VersionOverride");

                    if (!string.IsNullOrWhiteSpace(v))
                        central.AddOrUpdate(include!, v!, Path.GetFileName(projectPath));

                    if (!string.IsNullOrWhiteSpace(vo))
                        central.AddOrUpdate(include!, vo!, Path.GetFileName(projectPath));
                    
                    // Если версий нет в inner, возможно они в атрибутах
                    if (string.IsNullOrWhiteSpace(v) && string.IsNullOrWhiteSpace(vo))
                    {
                        var av = GetAttr(attrs, "Version");
                        var avo = GetAttr(attrs, "VersionOverride");
                        if (!string.IsNullOrWhiteSpace(av))
                            central.AddOrUpdate(include!, av!, Path.GetFileName(projectPath));
                        if (!string.IsNullOrWhiteSpace(avo))
                            central.AddOrUpdate(include!, avo!, Path.GetFileName(projectPath));
                    }
                }

                var inner2 = RemoveInnerTag(inner, "Version");
                inner2 = RemoveInnerTag(inner2, "VersionOverride");

                var attrs2 = RemoveAttr(attrs, "Version");
                attrs2 = RemoveAttr(attrs2, "VersionOverride");

                return "<PackageReference" + attrs2 + ">" + inner2 + "</PackageReference>";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        // 2) Затем обрабатываем самозакрывающиеся PackageReference
        updated = Regex.Replace(updated,
            @"<\s*PackageReference\b(?<attrs>[^>]*?)\/\s*>",
            m =>
            {
                var attrs = m.Groups["attrs"].Value;

                var include = GetAttr(attrs, "Include") ?? GetAttr(attrs, "Update");
                var version = GetAttr(attrs, "Version");
                var versionOverride = GetAttr(attrs, "VersionOverride");

                if (!string.IsNullOrWhiteSpace(include))
                {
                    if (!string.IsNullOrWhiteSpace(version))
                        central.AddOrUpdate(include!, version!, Path.GetFileName(projectPath));

                    if (!string.IsNullOrWhiteSpace(versionOverride))
                        central.AddOrUpdate(include!, versionOverride!, Path.GetFileName(projectPath));
                }

                var newAttrs = RemoveAttr(attrs, "Version");
                newAttrs = RemoveAttr(newAttrs, "VersionOverride");

                return "<PackageReference" + newAttrs + " />";
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return new Result(updated);
    }

    private static string? GetAttr(string attrs, string name)
    {
        var m = Regex.Match(attrs, $@"\b{name}\s*=\s*""(?<v>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups["v"].Value : null;
    }

    private static string RemoveAttr(string attrs, string name)
    {
        // удаляем атрибут, стараясь не портить пробелы. 
        // Используем \b чтобы не задеть похожие имена.
        return Regex.Replace(attrs,
            $@"\s+\b{name}\s*=\s*""[^""]*""",
            "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string? FindInnerTagValue(string inner, string tag)
    {
        var m = Regex.Match(inner, $@"<\s*{tag}\b[^>]*>(?<v>.*?)<\s*/\s*{tag}\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    private static string RemoveInnerTag(string inner, string tag)
    {
        return Regex.Replace(inner,
            $@"([ \t]*(\r?\n))?[ \t]*<\s*{tag}\b[^>]*>.*?<\s*/\s*{tag}\s*>[ \t]*(\r?\n)?",
            m =>
            {
                // Если тег был единственным на строке, удаляем строку целиком.
                // Группа 2 — это перевод строки перед тегом.
                // Группа 3 — это перевод строки после тега.
                if (m.Groups[2].Success && m.Groups[3].Success) return m.Groups[2].Value; // Оставляем один перевод строки
                if (m.Groups[2].Success || m.Groups[3].Success) return "";
                return "";
            },
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }
}

internal static class MsbuildText
{
    public sealed record ElementSlice(
        string Name,
        int StartIndex,
        int EndIndexExclusive,
        string OuterText,
        string PrefixBeforeInner,
        string InnerText,
        string SuffixAfterInner);

    // Находит top-level элементы <PropertyGroup ...>...</PropertyGroup> (без полноценного XML-парсера)
    public static IEnumerable<ElementSlice> FindTopLevelElements(string text, string elementName)
    {
        // очень упрощённо: ищем открывающий тег, потом парсим баланс по вложенности того же имени
        var openRe = new Regex($@"<\s*{elementName}\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var closeRe = new Regex($@"<\s*/\s*{elementName}\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        for (int i = 0; i < text.Length;)
        {
            var om = openRe.Match(text, i);
            if (!om.Success) yield break;

            int depth = 1;
            int searchFrom = om.Index + om.Length;

            while (depth > 0)
            {
                var nextOpen = openRe.Match(text, searchFrom);
                var nextClose = closeRe.Match(text, searchFrom);

                if (!nextClose.Success)
                    break;

                if (nextOpen.Success && nextOpen.Index < nextClose.Index)
                {
                    depth++;
                    searchFrom = nextOpen.Index + nextOpen.Length;
                }
                else
                {
                    depth--;
                    searchFrom = nextClose.Index + nextClose.Length;
                    if (depth == 0)
                    {
                        var start = om.Index;
                        var end = nextClose.Index + nextClose.Length;
                        var outer = text.Substring(start, end - start);

                        // inner: между om.End и close.Start
                        var innerStart = om.Index + om.Length;
                        var innerEnd = nextClose.Index;
                        var inner = text.Substring(innerStart, innerEnd - innerStart);

                        yield return new ElementSlice(
                            Name: elementName,
                            StartIndex: start,
                            EndIndexExclusive: end,
                            OuterText: outer,
                            PrefixBeforeInner: text.Substring(start, om.Length), // "<PropertyGroup ...>"
                            InnerText: inner,
                            SuffixAfterInner: text.Substring(nextClose.Index, nextClose.Length) // "</PropertyGroup>"
                        );

                        i = end;
                        break;
                    }
                }
            }

            if (depth > 0)
            {
                // не нашли закрытие — выходим, чтобы не зациклиться
                yield break;
            }
        }
    }

    public sealed record ChildElement(string Name, int StartIndex, int EndIndexExclusive, string OuterText);

    // Ищет "прямые" child элементы (очень простая эвристика: строки вида <X>...</X> без вложенности)
    public static IEnumerable<ChildElement> FindDirectChildElements(string propertyGroupInnerText)
    {
        // msbuild свойства обычно простые: <Name>Value</Name>, но могут быть атрибуты
        var re = new Regex(@"<\s*(?<n>[A-Za-z_][A-Za-z0-9_\.-]*)\b[^>]*>(?<v>.*?)<\s*/\s*(?<n2>[A-Za-z_][A-Za-z0-9_\.-]*)\s*>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        foreach (Match m in re.Matches(propertyGroupInnerText))
        {
            var n = m.Groups["n"].Value;
            var n2 = m.Groups["n2"].Value;
            if (!string.Equals(n, n2, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new ChildElement(
                Name: n,
                StartIndex: m.Index,
                EndIndexExclusive: m.Index + m.Length,
                OuterText: m.Value
            );
        }
    }

    public static IEnumerable<string> FindDirectChildElementNames(string propertyGroupInnerText)
        => FindDirectChildElements(propertyGroupInnerText)
            .Select(e => e.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    // Находит имена child элементов (приблизительно)
    public static IEnumerable<string> FindDirectChildElementNamesLoose(string inner)
    {
        var re = new Regex(@"<\s*(?<n>[A-Za-z_][A-Za-z0-9_\.-]*)\s*>", RegexOptions.CultureInvariant);
        foreach (Match m in re.Matches(inner))
            yield return m.Groups["n"].Value;
    }
}
