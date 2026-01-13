using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
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
                Console.Error.WriteLine($"[ERR] Folder not found: {root}");
                return 2;
            }

            var packagesPath = Path.Combine(root, "Directory.Packages.props");
            var buildPath = Path.Combine(root, "Directory.Build.props");

            if (!File.Exists(packagesPath))
            {
                Console.Error.WriteLine($"[ERR] Directory.Packages.props not found in: {root}");
                return 2;
            }

            var report = new Report
            {
                RootFolder = root,
                PackagesInputPath = packagesPath,
                BuildPropsPath = File.Exists(buildPath) ? buildPath : null
            };

            // Load XML
            var packagesDoc = LoadXml(packagesPath);
            XDocument? buildDoc = File.Exists(buildPath) ? LoadXml(buildPath) : null;

            // Collect macros from BOTH files (Build + Packages)
            var macrosFromBuild = buildDoc is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                                   : CollectVersionMacros(buildDoc);

            var macrosFromPackagesBefore = CollectVersionMacros(packagesDoc);

            // Merge: if same macro exists in both, prefer Packages (because it’s local to file we edit)
            // (Also handy: if values differ, we track it.)
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in macrosFromBuild) merged[kv.Key] = kv.Value;
            foreach (var kv in macrosFromPackagesBefore)
            {
                if (merged.TryGetValue(kv.Key, out var prev) && !StringComparer.OrdinalIgnoreCase.Equals(prev, kv.Value))
                    report.MacroValueConflicts.Add((kv.Key, prev, kv.Value));

                merged[kv.Key] = kv.Value;
            }

            report.MacrosFoundInBuild = macrosFromBuild.Count;
            report.MacrosFoundInPackagesBefore = macrosFromPackagesBefore.Count;
            report.MacrosMergedTotal = merged.Count;

            // Reverse index: versionValue -> [macroNames...]
            var macrosByValue = merged
                .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // 1) Replace numeric PackageVersion @Version -> $(Macro) if exists
            NormalizePackageVersions(packagesDoc, macrosByValue, report);

            // 2) Determine used macros after normalization (scan resulting XML string)
            var packagesXmlAfterReplace = ToXmlString(packagesDoc);
            var usedMacros = new HashSet<string>(
                MacroUseRegex.Matches(packagesXmlAfterReplace).Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            report.UsedMacrosAfterNormalization = usedMacros.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            // 3) Remove unused macros ONLY from Directory.Packages.props (since only it is editable)
            RemoveUnusedMacrosFromPackages(packagesDoc, usedMacros, report);

            // 4) Sort macro sections (PropertyGroup with version-macro elements) by macro name
            SortMacroGroupsInPackages(packagesDoc, report);

            // Output paths
            var packagesOutPath = opt.InPlace
                ? packagesPath
                : Path.GetFullPath(opt.OutputPath ?? Path.Combine(root, "Directory.Packages.normalized.props"));

            var reportPath = Path.GetFullPath(opt.ReportPath ?? Path.Combine(root, "normalize-report.md"));

            SaveXml(packagesDoc, packagesOutPath);
            File.WriteAllText(reportPath, report.ToMarkdown(packagesOutPath), new UTF8Encoding(false));

            Console.WriteLine("[OK] Done");
            Console.WriteLine($"     Packages: {packagesOutPath}");
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

    // ---------- Core logic ----------

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
        // We treat "macro definitions" as: PropertyGroup child elements without attributes
        // whose value looks like a version.
        var propertyGroups = packagesDoc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup").ToList();

        foreach (var pg in propertyGroups)
        {
            var versionMacroElements = pg.Elements()
                .Where(e => !e.HasAttributes && LooksLikeVersionRegex.IsMatch((e.Value ?? "").Trim()))
                .ToList();

            foreach (var el in versionMacroElements)
            {
                var name = el.Name.LocalName;
                var value = (el.Value ?? "").Trim();

                if (!usedMacros.Contains(name))
                {
                    el.Remove();
                    report.RemovedUnusedMacrosFromPackages.Add((name, value));
                }
            }

            // If PropertyGroup became empty (no element children), remove it to avoid empty blocks
            if (!pg.Elements().Any())
            {
                // keep PropertyGroup if it still has non-element nodes? Usually not needed.
                // If there are no elements at all, it’s empty for practical purposes.
                pg.Remove();
                report.RemovedEmptyPropertyGroups++;
            }
        }
    }

    private static void SortMacroGroupsInPackages(XDocument packagesDoc, Report report)
    {
        var groups = packagesDoc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup").ToList();

        foreach (var pg in groups)
        {
            // macro group = has at least one version-macro element
            var macroEls = pg.Elements()
                .Where(e => !e.HasAttributes && LooksLikeVersionRegex.IsMatch((e.Value ?? "").Trim()))
                .ToList();

            if (macroEls.Count == 0)
                continue;

            // If group has any non-macro elements, we do a conservative sort:
            // sort only those macro elements, leave others as-is.
            var otherEls = pg.Elements().Except(macroEls).ToList();

            bool hasOnlyMacros = otherEls.Count == 0;

            var sortedMacros = macroEls
                .OrderBy(e => e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .Select(e => new XElement(e.Name, (e.Value ?? "").Trim()))
                .ToList();

            if (hasOnlyMacros)
            {
                var before = macroEls.Select(e => e.Name.LocalName).ToList();
                var after = sortedMacros.Select(e => e.Name.LocalName).ToList();
                if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
                    report.SortedMacroGroupsCount++;

                pg.RemoveNodes();
                foreach (var e in sortedMacros) pg.Add(e);
            }
            else
            {
                // Replace each original macro element in place using sorted order,
                // keeping non-macro elements in the same relative positions.
                var originalMacroNodes = pg.Elements()
                    .Select((el, idx) => (el, idx))
                    .Where(t => macroEls.Contains(t.el))
                    .ToList();

                var before = originalMacroNodes.Select(x => x.el.Name.LocalName).ToList();
                var after = sortedMacros.Select(x => x.Name.LocalName).ToList();

                if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
                    report.SortedMacroGroupsCount++;

                for (int i = 0; i < originalMacroNodes.Count; i++)
                {
                    var (el, _) = originalMacroNodes[i];
                    el.ReplaceWith(sortedMacros[i]);
                }
            }
        }
    }

    // Collect macros in a doc: name->value, where element in PropertyGroup, no attributes, value looks like version
    private static Dictionary<string, string> CollectVersionMacros(XDocument doc)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pg in doc.Descendants().Where(x => x.Name.LocalName == "PropertyGroup"))
        {
            foreach (var el in pg.Elements())
            {
                if (el.HasAttributes) continue;

                var name = el.Name.LocalName;
                var value = (el.Value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) continue;

                if (!LooksLikeVersionRegex.IsMatch(value)) continue;

                dict[name] = value;
            }
        }

        return dict;
    }

    // ---------- XML helpers ----------

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

    // ---------- Options + report ----------

    private sealed class Options
    {
        public string RootFolder { get; init; } = "";
        public bool InPlace { get; init; }
        public string? OutputPath { get; init; }
        public string? ReportPath { get; init; }

        public static string Usage =>
@"Usage:
  PropsNormalizer --root <folder> [--inplace] [--out <path>] [--report <report.md>]

What it does:
  - Reads <root>\Directory.Packages.props (required)
  - Reads <root>\Directory.Build.props (optional; only for macro lookup)
  - Modifies ONLY Directory.Packages.props (or writes to --out)
  - Replaces numeric PackageVersion versions with $(Macro) if macro value exists
  - Removes unused macros ONLY from Directory.Packages.props
  - Sorts macro groups in Directory.Packages.props by macro name
  - Writes Markdown report

Examples:
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
                        throw new OptionsException($"Unknown argument: {a}");
                }
            }

            if (string.IsNullOrWhiteSpace(root))
                throw new OptionsException("Missing --root");

            if (inplace && outPath is not null)
                throw new OptionsException("Use either --inplace OR --out, not both.");

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
            sb.AppendLine("# Directory.Packages.props normalization report");
            sb.AppendLine();
            sb.AppendLine("## Paths");
            sb.AppendLine();
            sb.AppendLine($"- Root: `{Escape(RootFolder)}`");
            sb.AppendLine($"- Input: `{Escape(PackagesInputPath)}`");
            sb.AppendLine($"- Output: `{Escape(packagesOutputPath)}`");
            sb.AppendLine($"- Directory.Build.props used for macro lookup: {(BuildPropsPath is null ? "_not found_" : $"`{Escape(BuildPropsPath)}`")}");
            sb.AppendLine();

            sb.AppendLine("## Macro discovery");
            sb.AppendLine();
            sb.AppendLine($"- Macros found in Directory.Build.props: **{MacrosFoundInBuild}**");
            sb.AppendLine($"- Macros found in Directory.Packages.props (before): **{MacrosFoundInPackagesBefore}**");
            sb.AppendLine($"- Total macros for matching (merged): **{MacrosMergedTotal}**");
            sb.AppendLine();

            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Replaced numeric versions with macros: **{ReplacedNumericWithMacro.Count}**");
            sb.AppendLine($"- PackageVersion already used macros (left as-is): **{PackageVersionAlreadyMacro}**");
            sb.AppendLine($"- Numeric versions without matching macro (left as-is): **{PackageVersionsNoMacroLeftAsIs.Count}**");
            sb.AppendLine($"- Non-numeric versions (left as-is): **{PackageVersionsNonNumericLeftAsIs.Count}**");
            sb.AppendLine($"- Ambiguous matches (same value in multiple macros): **{AmbiguousMacroMatches.Count}**");
            sb.AppendLine();
            sb.AppendLine($"- Removed unused macros from Directory.Packages.props: **{RemovedUnusedMacrosFromPackages.Count}**");
            sb.AppendLine($"- Removed empty PropertyGroup(s): **{RemovedEmptyPropertyGroups}**");
            sb.AppendLine($"- Sorted macro group(s): **{SortedMacroGroupsCount}**");
            sb.AppendLine();

            if (MacroValueConflicts.Count > 0)
            {
                sb.AppendLine("## Macro value conflicts (Build vs Packages)");
                sb.AppendLine();
                sb.AppendLine("Same macro name had different values; matching prefers the value from Directory.Packages.props.");
                sb.AppendLine();
                sb.AppendLine("| Macro | Build value | Packages value |");
                sb.AppendLine("|---|---:|---:|");
                foreach (var c in MacroValueConflicts.OrderBy(x => x.Macro, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(c.Macro)}` | `{Escape(c.BuildValue)}` | `{Escape(c.PackagesValue)}` |");
                sb.AppendLine();
            }

            if (ReplacedNumericWithMacro.Count > 0)
            {
                sb.AppendLine("## Replacements (numeric -> macro)");
                sb.AppendLine();
                sb.AppendLine("| Package | Old version | New |");
                sb.AppendLine("|---|---:|---|");
                foreach (var r in ReplacedNumericWithMacro.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(r.Package)}` | `{Escape(r.OldVersion)}` | `$({Escape(r.Macro)})` |");
                sb.AppendLine();
            }

            if (AmbiguousMacroMatches.Count > 0)
            {
                sb.AppendLine("## Ambiguous matches");
                sb.AppendLine();
                sb.AppendLine("Same numeric version matched multiple macros; first macro (alphabetical) was chosen.");
                sb.AppendLine();
                foreach (var a in AmbiguousMacroMatches)
                {
                    sb.AppendLine($"- `{Escape(a.Package)}` version `{Escape(a.Version)}` matched: {string.Join(", ", a.Macros.Select(m => $"`{Escape(m)}`"))}. Chosen: `{Escape(a.Chosen)}`");
                }
                sb.AppendLine();
            }

            if (RemovedUnusedMacrosFromPackages.Count > 0)
            {
                sb.AppendLine("## Removed unused macros (only from Directory.Packages.props)");
                sb.AppendLine();
                sb.AppendLine("| Macro | Value |");
                sb.AppendLine("|---|---:|");
                foreach (var rm in RemovedUnusedMacrosFromPackages.OrderBy(x => x.Macro, StringComparer.OrdinalIgnoreCase))
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

            if (PackageVersionsNoMacroLeftAsIs.Count > 0)
            {
                sb.AppendLine("## Left as-is (no matching macro)");
                sb.AppendLine();
                sb.AppendLine("| Package | Version |");
                sb.AppendLine("|---|---:|");
                foreach (var p in PackageVersionsNoMacroLeftAsIs.OrderBy(x => x.Package, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"| `{Escape(p.Package)}` | `{Escape(p.Version)}` |");
                sb.AppendLine();
            }

            if (PackageVersionsNonNumericLeftAsIs.Count > 0)
            {
                sb.AppendLine("## Left as-is (non-numeric version)");
                sb.AppendLine();
                sb.AppendLine("| Package | Version |");
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
