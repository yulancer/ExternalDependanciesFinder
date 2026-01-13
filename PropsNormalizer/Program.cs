using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal static class Program
{
    private static readonly Regex MacroUseRegex = new(@"\$\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex LooksLikeVersionRegex = new(
        @"^\d+(\.\d+){1,3}([\-+][0-9A-Za-z\.\-_]+)?$",
        RegexOptions.Compiled);

    public static int Main(string[] args)
    {
        try
        {
            var opt = Options.Parse(args);

            if (!File.Exists(opt.PackagesPropsPath))
            {
                Console.Error.WriteLine($"[ERR] Directory.Packages.props not found: {opt.PackagesPropsPath}");
                return 2;
            }
            if (!File.Exists(opt.BuildPropsPath))
            {
                Console.Error.WriteLine($"[ERR] Directory.Build.props not found: {opt.BuildPropsPath}");
                return 2;
            }

            var report = new Report();

            var packagesDoc = LoadXml(opt.PackagesPropsPath);
            var buildDoc = LoadXml(opt.BuildPropsPath);

            // 1) Find "version macro" property groups in Directory.Build.props (after <!-- Version settings -->)
            var versionGroups = FindVersionSettingsPropertyGroups(buildDoc).ToList();

            // Collect macro definitions from those groups: MacroName -> Value
            var macroValueByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pg in versionGroups)
            {
                foreach (var el in pg.Elements())
                {
                    var name = el.Name.LocalName;
                    var value = (el.Value ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // we only treat these as version macros if value "looks like a version"
                    if (!LooksLikeVersionRegex.IsMatch(value)) continue;

                    macroValueByName[name] = value;
                }
            }

            // Build reverse index: version value -> macro names
            var macrosByValue = macroValueByName
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // 2) Normalize Directory.Packages.props: replace numeric versions with $(Macro) when possible
            NormalizePackagesProps(packagesDoc, macrosByValue, report);

            // 3) Determine used macros AFTER normalization (scan full XML text)
            var packagesXmlAfter = ToXmlString(packagesDoc);
            var usedMacros = new HashSet<string>(
                MacroUseRegex.Matches(packagesXmlAfter).Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            report.UsedMacrosAfterNormalization = usedMacros.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            // 4) Remove unused macros from version settings groups in Directory.Build.props + sort remaining macros
            NormalizeBuildProps(buildDoc, versionGroups, usedMacros, report);

            // 5) Write outputs
            var packagesOutPath = opt.InPlace ? opt.PackagesPropsPath : opt.PackagesOutputPath;
            var buildOutPath = opt.InPlace ? opt.BuildPropsPath : opt.BuildOutputPath;

            SaveXml(packagesDoc, packagesOutPath);
            SaveXml(buildDoc, buildOutPath);

            // 6) Write report.md
            var reportPath = opt.ReportPath;
            File.WriteAllText(reportPath, report.ToMarkdown(opt.PackagesPropsPath, packagesOutPath, opt.BuildPropsPath, buildOutPath), new UTF8Encoding(false));

            Console.WriteLine("[OK] Done.");
            Console.WriteLine($"     Packages: {packagesOutPath}");
            Console.WriteLine($"     Build   : {buildOutPath}");
            Console.WriteLine($"     Report  : {reportPath}");
            return 0;
        }
        catch (OptionsException ex)
        {
            Console.Error.WriteLine($"[ARGS] {ex.Message}");
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ERR] Unhandled exception:");
            Console.Error.WriteLine(ex);
            return 99;
        }
    }

    private static void NormalizePackagesProps(
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
            if (verAttr == null) continue;

            var current = (verAttr.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(current)) continue;

            // already macro
            if (current.Contains("$("))
            {
                report.AlreadyMacroCount++;
                continue;
            }

            // only replace "numeric-looking" versions
            if (!LooksLikeVersionRegex.IsMatch(current))
            {
                report.NonNumericVersionLeftAsIs.Add(new Report.PackageLine(include, current));
                continue;
            }

            if (!macrosByValue.TryGetValue(current, out var macroNames) || macroNames.Count == 0)
            {
                report.NoMatchingMacroLeftAsIs.Add(new Report.PackageLine(include, current));
                continue;
            }

            // if multiple macros share same value - pick first alphabetical, record ambiguity
            var chosen = macroNames[0];
            if (macroNames.Count > 1)
            {
                report.AmbiguousMacroMatches.Add(new Report.Ambiguity(include, current, macroNames, chosen));
            }

            verAttr.Value = $"$({chosen})";
            report.ReplacedNumericWithMacro.Add(new Report.Replacement(include, current, chosen));
        }
    }

    private static void NormalizeBuildProps(
        XDocument buildDoc,
        List<XElement> versionGroups,
        HashSet<string> usedMacros,
        Report report)
    {
        foreach (var pg in versionGroups)
        {
            // collect candidate macro properties inside this PG
            var props = pg.Elements().ToList();

            // remove unused version macros (only those that look like versions)
            foreach (var el in props)
            {
                var name = el.Name.LocalName;
                var value = (el.Value ?? string.Empty).Trim();

                if (!LooksLikeVersionRegex.IsMatch(value))
                    continue; // don't touch non-version properties inside the group

                if (!usedMacros.Contains(name))
                {
                    el.Remove();
                    report.RemovedUnusedMacros.Add(new Report.RemovedMacro(name, value));
                }
            }

            // sort remaining version-macro elements by name (stable, keeps non-version elements in place)
            // Strategy:
            // - extract version-looking elements
            // - sort them
            // - replace them in the PG keeping non-version elements (and comments) untouched
            var nodes = pg.Nodes().ToList();

            var versionElements = nodes
                .OfType<XElement>()
                .Where(e => LooksLikeVersionRegex.IsMatch((e.Value ?? "").Trim()))
                .ToList();

            var sorted = versionElements
                .OrderBy(e => e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // if order changed — rewrite only those version elements in the group
            bool changedOrder = !versionElements.Select(e => e.Name.LocalName)
                .SequenceEqual(sorted.Select(e => e.Name.LocalName), StringComparer.OrdinalIgnoreCase);

            if (!changedOrder)
                continue;

            // Remove the old version elements
            foreach (var ve in versionElements)
                ve.Remove();

            // Insert sorted version elements at the position of the first removed version element.
            // Find insertion index among child nodes.
            int firstIndex = IndexOfFirstVersionElement(nodes);
            if (firstIndex < 0)
            {
                // if somehow no index - append
                foreach (var e in sorted) pg.Add(e);
            }
            else
            {
                // rebuild nodes around insertion
                var before = pg.Nodes().Take(firstIndex).ToList();
                var after = pg.Nodes().Skip(firstIndex).ToList(); // after removals, this starts from original "firstIndex" position

                pg.RemoveNodes();
                foreach (var n in before) pg.Add(n);
                foreach (var e in sorted) pg.Add(e);
                foreach (var n in after) pg.Add(n);
            }

            report.SortedMacroGroupsCount++;
        }
    }

    private static int IndexOfFirstVersionElement(List<XNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is XElement el)
            {
                var value = (el.Value ?? string.Empty).Trim();
                if (LooksLikeVersionRegex.IsMatch(value))
                    return i;
            }
        }
        return -1;
    }

    private static IEnumerable<XElement> FindVersionSettingsPropertyGroups(XDocument buildDoc)
    {
        // We detect comment nodes containing "Version settings" and pick the next PropertyGroup element after them.
        var allNodes = buildDoc
            .Root?
            .Nodes()
            .ToList() ?? new List<XNode>();

        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i] is XComment c)
            {
                var text = (c.Value ?? "").Trim();
                if (text.Contains("Version settings", StringComparison.OrdinalIgnoreCase))
                {
                    // find next element PropertyGroup
                    for (int j = i + 1; j < allNodes.Count; j++)
                    {
                        if (allNodes[j] is XElement el && el.Name.LocalName == "PropertyGroup")
                        {
                            yield return el;
                            break;
                        }
                    }
                }
            }
        }

        // Fallback: if there is no comment marker, try to find any PropertyGroup that contains version-looking properties
        // and names that are later used as $(...) macros in Directory.Packages.props.
        // (We keep fallback conservative: require at least 3 version-like children)
        if (!buildDoc.DescendantNodes().OfType<XComment>().Any(x => (x.Value ?? "").Contains("Version settings", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var pg in buildDoc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup"))
            {
                var count = pg.Elements().Count(e => LooksLikeVersionRegex.IsMatch((e.Value ?? "").Trim()));
                if (count >= 3)
                    yield return pg;
            }
        }
    }

    private static XDocument LoadXml(string path)
    {
        using var fs = File.OpenRead(path);
        return XDocument.Load(fs, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private static void SaveXml(XDocument doc, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = true
        };

        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    private static string ToXmlString(XDocument doc)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = true
        };
        using var xw = XmlWriter.Create(sb, settings);
        doc.Save(xw);
        xw.Flush();
        return sb.ToString();
    }

    private sealed class Options
    {
        public string PackagesPropsPath { get; init; } = "";
        public string BuildPropsPath { get; init; } = "";
        public bool InPlace { get; init; }
        public string PackagesOutputPath { get; init; } = "";
        public string BuildOutputPath { get; init; } = "";
        public string ReportPath { get; init; } = "";

        public static string Usage =>
@"Usage:
  PropsNormalizer --packages <Directory.Packages.props> --build <Directory.Build.props> [--inplace]
                  [--out-packages <path>] [--out-build <path>] [--report <report.md>]

Examples:
  PropsNormalizer --packages .\Directory.Packages.props --build .\Directory.Build.props --inplace
  PropsNormalizer --packages .\Directory.Packages.props --build .\Directory.Build.props --out-packages .\Directory.Packages.normalized.props --out-build .\Directory.Build.normalized.props --report .\normalize-report.md
";

        public static Options Parse(string[] args)
        {
            string? packages = null;
            string? build = null;
            bool inplace = false;
            string? outPackages = null;
            string? outBuild = null;
            string? report = null;

            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i].Trim();
                switch (a)
                {
                    case "--packages":
                        packages = Next(i++, args, "--packages");
                        break;
                    case "--build":
                        build = Next(i++, args, "--build");
                        break;
                    case "--inplace":
                        inplace = true;
                        break;
                    case "--out-packages":
                        outPackages = Next(i++, args, "--out-packages");
                        break;
                    case "--out-build":
                        outBuild = Next(i++, args, "--out-build");
                        break;
                    case "--report":
                        report = Next(i++, args, "--report");
                        break;
                    default:
                        throw new OptionsException($"Unknown argument: {a}");
                }
            }

            if (string.IsNullOrWhiteSpace(packages))
                throw new OptionsException("Missing --packages");
            if (string.IsNullOrWhiteSpace(build))
                throw new OptionsException("Missing --build");

            var packagesFull = Path.GetFullPath(packages);
            var buildFull = Path.GetFullPath(build);

            if (inplace)
            {
                outPackages = packagesFull;
                outBuild = buildFull;
            }
            else
            {
                outPackages ??= Path.Combine(Path.GetDirectoryName(packagesFull)!, "Directory.Packages.normalized.props");
                outBuild ??= Path.Combine(Path.GetDirectoryName(buildFull)!, "Directory.Build.normalized.props");
            }

            report ??= Path.Combine(Path.GetDirectoryName(packagesFull)!, "normalize-report.md");

            return new Options
            {
                PackagesPropsPath = packagesFull,
                BuildPropsPath = buildFull,
                InPlace = inplace,
                PackagesOutputPath = Path.GetFullPath(outPackages),
                BuildOutputPath = Path.GetFullPath(outBuild),
                ReportPath = Path.GetFullPath(report)
            };
        }

        private static string Next(int idx, string[] args, string name)
        {
            if (idx + 1 >= args.Length)
                throw new OptionsException($"Missing value for {name}");
            return args[idx + 1];
        }
    }

    private sealed class OptionsException : Exception
    {
        public OptionsException(string message) : base(message) { }
    }

    private sealed class Report
    {
        public int AlreadyMacroCount { get; set; }

        public List<Replacement> ReplacedNumericWithMacro { get; } = new();
        public List<PackageLine> NoMatchingMacroLeftAsIs { get; } = new();
        public List<PackageLine> NonNumericVersionLeftAsIs { get; } = new();
        public List<Ambiguity> AmbiguousMacroMatches { get; } = new();

        public List<RemovedMacro> RemovedUnusedMacros { get; } = new();
        public int SortedMacroGroupsCount { get; set; }

        public List<string> UsedMacrosAfterNormalization { get; set; } = new();

        public string ToMarkdown(string packagesIn, string packagesOut, string buildIn, string buildOut)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Props normalization report");
            sb.AppendLine();
            sb.AppendLine("## Files");
            sb.AppendLine();
            sb.AppendLine($"- Packages input: `{packagesIn}`");
            sb.AppendLine($"- Packages output: `{packagesOut}`");
            sb.AppendLine($"- Build input: `{buildIn}`");
            sb.AppendLine($"- Build output: `{buildOut}`");
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Replaced numeric versions with macros: **{ReplacedNumericWithMacro.Count}**");
            sb.AppendLine($"- PackageVersion already used macros (left as-is): **{AlreadyMacroCount}**");
            sb.AppendLine($"- Numeric versions without matching macro (left as-is): **{NoMatchingMacroLeftAsIs.Count}**");
            sb.AppendLine($"- Non-numeric versions (left as-is): **{NonNumericVersionLeftAsIs.Count}**");
            sb.AppendLine($"- Ambiguous macro matches (same value in multiple macros): **{AmbiguousMacroMatches.Count}**");
            sb.AppendLine();
            sb.AppendLine($"- Removed unused macros in build props: **{RemovedUnusedMacros.Count}**");
            sb.AppendLine($"- Sorted version-macro groups: **{SortedMacroGroupsCount}**");
            sb.AppendLine();

            if (ReplacedNumericWithMacro.Count > 0)
            {
                sb.AppendLine("## Replacements (numeric -> macro)");
                sb.AppendLine();
                sb.AppendLine("| Package | Old version | Macro |");
                sb.AppendLine("|---|---:|---|");
                foreach (var r in ReplacedNumericWithMacro.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(r.Package)}` | `{Escape(r.OldVersion)}` | `$({Escape(r.Macro)})` |");
                sb.AppendLine();
            }

            if (AmbiguousMacroMatches.Count > 0)
            {
                sb.AppendLine("## Ambiguous matches");
                sb.AppendLine();
                sb.AppendLine("Same numeric version value found in multiple macros; the first macro (alphabetical) was chosen.");
                sb.AppendLine();
                foreach (var a in AmbiguousMacroMatches)
                {
                    sb.AppendLine($"- `{Escape(a.Package)}` version `{Escape(a.Version)}` matched: {string.Join(", ", a.Macros.Select(m => $"`{Escape(m)}`"))}. Chosen: `{Escape(a.Chosen)}`");
                }
                sb.AppendLine();
            }

            if (NoMatchingMacroLeftAsIs.Count > 0)
            {
                sb.AppendLine("## Left as-is (no matching macro)");
                sb.AppendLine();
                sb.AppendLine("| Package | Version |");
                sb.AppendLine("|---|---:|");
                foreach (var p in NoMatchingMacroLeftAsIs.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(p.Package)}` | `{Escape(p.Version)}` |");
                sb.AppendLine();
            }

            if (RemovedUnusedMacros.Count > 0)
            {
                sb.AppendLine("## Removed unused macros (Directory.Build.props)");
                sb.AppendLine();
                sb.AppendLine("| Macro | Value |");
                sb.AppendLine("|---|---:|");
                foreach (var rm in RemovedUnusedMacros.OrderBy(x => x.Macro, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(rm.Macro)}` | `{Escape(rm.Value)}` |");
                sb.AppendLine();
            }

            if (UsedMacrosAfterNormalization.Count > 0)
            {
                sb.AppendLine("## Used macros after normalization");
                sb.AppendLine();
                sb.AppendLine(string.Join(", ", UsedMacrosAfterNormalization.Select(x => $"`{Escape(x)}`")));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("|", "\\|");

        public readonly record struct Replacement(string Package, string OldVersion, string Macro);
        public readonly record struct PackageLine(string Package, string Version);
        public readonly record struct RemovedMacro(string Macro, string Value);
        public readonly record struct Ambiguity(string Package, string Version, IReadOnlyList<string> Macros, string Chosen);
    }
}
