using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TestSolutionMaker.TestProjectCreator
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;

                var namedArgs = ParseArguments(args);
                bool useProjectPath = namedArgs.ContainsKey("ProjectPath");

                if (useProjectPath)
                {
                    if (!namedArgs.ContainsKey("DotnetVersion"))
                    {
                        Console.WriteLine("Использование: --ProjectPath=<путь.к.csproj> --DotnetVersion=<TFM>");
                        return -1;
                    }
                }
                else
                {
                    if (!namedArgs.ContainsKey("PackageName") ||
                        !namedArgs.ContainsKey("Version") ||
                        !namedArgs.ContainsKey("DotnetVersion"))
                    {
                        Console.WriteLine(
                            "Использование (1): --PackageName=<Имя пакета> --Version=<Версия> --DotnetVersion=<TFM>\n" +
                            "Или (2): --ProjectPath=<путь.к.csproj> --DotnetVersion=<TFM>");
                        return -1;
                    }
                }

                string dotnetVersion = namedArgs["DotnetVersion"];

                // Предупреждения, которые не должны падать выполнение (например, нет PDB)
                var warnings = new List<string>();

                // 1) Готовим пустую директорию решения
                string solutionDir = PrepareSolutionDirectory();

                // 2) Создаём решение
                RunProcessOrThrow("dotnet", "new sln -n TestSolution", solutionDir, "Создание решения");

                // 3) Список пакетов к обработке
                var packages = new List<(string PackageName, string Version, string ProjectName)>();

                if (useProjectPath)
                {
                    string projectPathParam = namedArgs["ProjectPath"];
                    if (!File.Exists(projectPathParam))
                        throw new FileNotFoundException("Файл проекта не найден: " + projectPathParam);

                    Console.WriteLine($"Анализ проекта: {projectPathParam}");
                    var discovered = DiscoverPackagesFromProject(projectPathParam);
                    if (discovered.Count == 0)
                    {
                        Console.WriteLine("Пакеты не найдены в указанном проекте (нет PackageReference).");
                        return -1;
                    }

                    foreach (var p in discovered)
                    {
                        string projectName = BuildProjectName(p.PackageName, p.Version);
                        packages.Add((p.PackageName, p.Version, projectName));
                    }

                    Console.WriteLine($"Найдено пакетов: {packages.Count}");
                }
                else
                {
                    string packageName = namedArgs["PackageName"];
                    string version = namedArgs["Version"];
                    packages.Add((packageName, version, BuildProjectName(packageName, version)));
                }

                // Для отчёта
                var successes = new List<(string Package, string Version, string Project, string DllName)>();
                var failures = new List<(string Package, string Version, string Reason)>();

                // 4) Для каждого пакета — предварительная проверка наличия DLL
                var dllNamesForCoverage = new List<string>();

                foreach (var pkg in packages)
                {
                    Console.WriteLine(
                        $"Проверка пакета: {pkg.PackageName} {pkg.Version} для {dotnetVersion}");

                    var probe = ProbePackageAssembly(pkg.PackageName, pkg.Version, dotnetVersion);
                    if (!probe.HasDll)
                    {
                        Console.WriteLine($"Пропуск: {pkg.PackageName} — {probe.Reason}");
                        failures.Add((pkg.PackageName, pkg.Version, probe.Reason ?? "нет подходящей DLL"));
                        continue;
                    }

                    string actualDllName = probe.DllName!;
                    Console.WriteLine(
                        $"Найдена сборка {actualDllName}.dll (lib папка: {probe.LibFolder ?? "неизвестно"}) — создаю тестовый проект {pkg.ProjectName}");

                    // 4.1 Создаём xUnit-проект
                    CreateTestProject(dotnetVersion, solutionDir, pkg.ProjectName);

                    // 4.2 Добавляем проект в решение
                    var projectPath = AddProjectToSolution(solutionDir, pkg.ProjectName);

                    // 4.3 Добавляем пакет
                    RunProcessOrThrow("dotnet", $"add {projectPath} package {pkg.PackageName} --version {pkg.Version}",
                        solutionDir, "Добавление NuGet пакета");

                    // 4.4 Восстанавливаем и собираем
                    RunProcessOrThrow("dotnet",
                        $"restore {projectPath} -p:TreatWarningsAsErrors=false -p:NoWarn=NU1605", solutionDir,
                        "Восстановление пакетов");
                    RunProcessOrThrow("dotnet", $"build {projectPath} -p:TreatWarningsAsErrors=false -p:NoWarn=NU1605",
                        solutionDir, "Сборка проекта");

                    // 4.5 Папка артефактов
                    string artifactsDir = GetArtifactsDirectory(solutionDir, dotnetVersion, pkg.ProjectName);

                    // 4.6 Гарантируем наличие DLL (копируем из nupkg) — используем то же имя, что нашли на пробе
                    string ensuredDllName = EnsurePackageDllPresentAndGetName(
                        artifactsDir, pkg.PackageName, pkg.Version, dotnetVersion, preferredDllName: actualDllName);

                    // 4.7 Символы (не фейлим выполнение, добавляем предупреждения)
                    DownloadSymbols(solutionDir, artifactsDir, ensuredDllName, warnings);

                    // 4.8 Тестовый файл (не фейлим по отсутствию PDB)
                    PrepareAndValidateTestFile(solutionDir, artifactsDir, ensuredDllName, pkg.ProjectName, warnings);

                    dllNamesForCoverage.Add(ensuredDllName);
                    successes.Add((pkg.PackageName, pkg.Version, pkg.ProjectName, ensuredDllName));
                }

                // 5) Пишем отчёт
                WriteReport(solutionDir, dotnetVersion, successes, failures, warnings);
                WriteReadme(solutionDir, dotnetVersion, successes, failures);
                
                if (successes.Count == 0)
                {
                    Console.WriteLine(
                        "Подходящих для тестирования пакетов не найдено. См. отчёт TestPackagesReport.txt");
                    return 0; // завершаем без ошибки
                }

                // 6) Запускаем все тесты одним прогоном и собираем покрытие
                RunProcessOrThrow("dotnet",
                    "test --blame --collect:\"Code Coverage\" --results-directory \"TestResults\" -p:TreatWarningsAsErrors=false -p:NoWarn=NU1605",
                    solutionDir,
                    "Выполнение тестов с измерением покрытия", 100000);


                // 7) Сводим результаты
                ConsolidateTestResults(solutionDir);

                // 8) Конвертируем покрытие в XML
                string testResultsDir = Path.Combine(solutionDir, "TestResults");
                RunProcessOrThrow("dotnet", "coverage merge *.coverage -o outfile.xml -f xml", testResultsDir,
                    "Конвертация результатов покрытия в XML");

                // 9) Выводим проценты по каждому модулю
                foreach (var dll in dllNamesForCoverage.Distinct(StringComparer.OrdinalIgnoreCase))
                    DisplayCoverageResults(testResultsDir, dll);

                Console.WriteLine("Все шаги успешно завершены.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Исключение: " + ex.Message);
                return -1;
            }
        }
        private static ProbeResult ProbePackageAssembly(string packageName, string version, string targetTfm)
        {
            try
            {
                // 1) Пакет уже есть в глобальном кэше?
                var fromCache = TryProbeFromGlobalPackages(packageName, version, targetTfm);
                if (fromCache != null) return fromCache;

                // 2) Кладём пакет в кэш через обычный restore из сконфигурированных источников
                var restored = EnsureInGlobalPackagesByRestore(packageName, version);

                // 3) Пробуем снова из кэша
                var afterRestore = TryProbeFromGlobalPackages(packageName, version, targetTfm);
                if (afterRestore != null) return afterRestore;

                // 4) Фолбэк: старая схема прямой загрузки с nuget.org (если пакет действительно там есть)
                return ProbeByDirectDownloadFromNugetOrg(packageName, version, targetTfm);
            }
            catch (Exception ex)
            {
                return new ProbeResult { HasDll = false, Reason = "ошибка анализа пакета: " + ex.Message };
            }
        }
        // ------------------ режим ProjectPath: парсинг пакетов ------------------

        private static List<(string PackageName, string Version)> DiscoverPackagesFromProject(string csprojPath)
        {
            var result = new List<(string, string)>();

            var projectDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
            var xproj = XDocument.Load(csprojPath);

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadPropertiesFromProjectAndProps(xproj, projectDir, properties);

            var centralVersions = LoadCentralPackageVersions(projectDir);

            var pkgRefs = xproj.Descendants("PackageReference").ToList();
            foreach (var pr in pkgRefs)
            {
                var include = (string?) pr.Attribute("Include") ?? (string?) pr.Attribute("Update");
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                string? version = (string?) pr.Attribute("Version");
                if (string.IsNullOrWhiteSpace(version))
                {
                    if (!centralVersions.TryGetValue(include!, out version))
                    {
                        var verNode = pr.Element("Version");
                        version = verNode?.Value;
                    }
                }

                if (!string.IsNullOrWhiteSpace(version) && version!.Contains("$("))
                    version = ResolveMsBuildProps(version!, properties);

                if (string.IsNullOrWhiteSpace(version))
                {
                    Console.WriteLine($"Предупреждение: версия не определена для пакета {include}. Пакет пропущен.");
                    continue;
                }

                result.Add((include!, version!));
            }

            return result;
        }

        private static void LoadPropertiesFromProjectAndProps(XDocument xproj, string projectDir,
            Dictionary<string, string> bag)
        {
            foreach (var pg in xproj.Descendants("PropertyGroup"))
            {
                foreach (var el in pg.Elements())
                {
                    if (!el.HasElements && !string.IsNullOrWhiteSpace(el.Name.LocalName))
                    {
                        var key = el.Name.LocalName;
                        var val = el.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                            bag[key] = val!;
                    }
                }
            }

            foreach (var propsPath in EnumerateUpwards(projectDir, "Directory.Build.props"))
            {
                try
                {
                    var x = XDocument.Load(propsPath);
                    foreach (var pg in x.Descendants("PropertyGroup"))
                    {
                        foreach (var el in pg.Elements())
                        {
                            if (!el.HasElements && !string.IsNullOrWhiteSpace(el.Name.LocalName))
                            {
                                var key = el.Name.LocalName;
                                var val = el.Value?.Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                    bag[key] = val!;
                            }
                        }
                    }
                }
                catch
                {
                    /* best-effort */
                }
            }
        }

        private static Dictionary<string, string> LoadCentralPackageVersions(string projectDir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dp in EnumerateUpwards(projectDir, "Directory.Packages.props"))
            {
                try
                {
                    var x = XDocument.Load(dp);

                    foreach (var pv in x.Descendants("PackageVersion"))
                    {
                        var name = (string?) pv.Attribute("Include") ?? (string?) pv.Attribute("Update");
                        var version = (string?) pv.Attribute("Version") ?? pv.Element("Version")?.Value;
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
                            map[name!] = version!;
                    }

                    foreach (var pg in x.Descendants("PropertyGroup"))
                    {
                        foreach (var el in pg.Elements())
                        {
                            if (!el.HasElements && !string.IsNullOrWhiteSpace(el.Name.LocalName))
                            {
                                var key = el.Name.LocalName;
                                var val = el.Value?.Trim();
                                if (!string.IsNullOrWhiteSpace(val))
                                    map[key] = val!;
                            }
                        }
                    }
                }
                catch
                {
                    /* best-effort */
                }
            }

            return map;
        }

        private static IEnumerable<string> EnumerateUpwards(string startDir, string fileName)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                    yield return candidate;
                dir = dir.Parent;
            }
        }

        private static string ResolveMsBuildProps(string value, Dictionary<string, string> props)
        {
            return Regex.Replace(value, @"\$\((?<n>[^\)]+)\)", m =>
            {
                var key = m.Groups["n"].Value;
                if (props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;
                return m.Value;
            });
        }

        private static string SanitizeName(string raw)
        {
            var s = new string(raw.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            if (s.Length == 0) s = "Package";
            if (char.IsDigit(s[0])) s = "_" + s;
            return s;
        }
        private static string SanitizeVersion(string raw)
        {
            // заменяем всё, кроме букв/цифр, на "_"
            var s = new string(raw.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
            if (string.IsNullOrEmpty(s)) s = "v";
            // для наглядности префиксуем "v" если начинается с цифры
            if (char.IsDigit(s[0])) s = "v" + s;
            return s;
        }

        private static string BuildProjectName(string packageName, string version)
        {
            return $"TestProject_{SanitizeName(packageName)}_{SanitizeVersion(version)}";
        }

        // ------------------ подготовка решения ------------------

        private static string PrepareSolutionDirectory()
        {
            string solutionDir = Path.Combine(Directory.GetCurrentDirectory(), "TestSolution");
            if (Directory.Exists(solutionDir))
            {
                Console.WriteLine($"Очистка директории решения: {solutionDir}");
                TryDeleteDirectory(solutionDir);
            }

            Directory.CreateDirectory(solutionDir);
            Console.WriteLine($"Директория решения готова: {solutionDir}");
            return solutionDir;
        }

        private static void CreateTestProject(string dotnetVersion, string solutionDir, string projectName)
        {
            RunProcessOrThrow("dotnet", $"new xunit -n {projectName} -f {dotnetVersion}", solutionDir,
                "Создание тестового проекта");

            string defaultTestFile = Path.Combine(solutionDir, projectName, "UnitTest1.cs");
            if (File.Exists(defaultTestFile))
            {
                File.Delete(defaultTestFile);
                Console.WriteLine($"Удалён файл {projectName}/UnitTest1.cs по умолчанию.");
            }
        }

        private static string AddProjectToSolution(string solutionDir, string projectName)
        {
            string projectPath = Path.Combine(projectName, $"{projectName}.csproj");
            RunProcessOrThrow("dotnet", $"sln add {projectPath}", solutionDir, "Добавление проекта в решение");
            return projectPath;
        }

        private static string GetArtifactsDirectory(string solutionDir, string dotnetVersion, string projectName)
        {
            string artifactsDir = Path.Combine(solutionDir, projectName, "bin", "Debug", dotnetVersion);
            if (!Directory.Exists(artifactsDir))
                throw new Exception($"Папка с артефактами сборки не найдена: {artifactsDir}");
            Console.WriteLine($"Папка с артефактами ({projectName}): {artifactsDir}");
            return artifactsDir;
        }

        // ------------------ ПРОБА пакета (до создания проекта) ------------------

        private sealed class ProbeResult
        {
            public bool HasDll { get; init; }
            public string? DllName { get; init; } // без .dll
            public string? LibFolder { get; init; } // полный путь к выбранному lib/<tfm>
            public string? Reason { get; init; } // если HasDll == false
        }

     private static string EnsurePackageDllPresentAndGetName(
    string artifactsDir, string packageName, string version, string targetTfm, string? preferredDllName = null)
{
    // 1) Пробуем взять DLL из глобального кэша
    var probe = TryProbeFromGlobalPackages(packageName, version, targetTfm);

    // 2) Если нет в кэше — «подкладываем» пакет в кэш через restore
    if (probe == null)
    {
        EnsureInGlobalPackagesByRestore(packageName, version);
        probe = TryProbeFromGlobalPackages(packageName, version, targetTfm);
    }

    // 3) Если до сих пор не нашли — конечный фолбэк: прямая загрузка с nuget.org (как было раньше)
    if (probe == null)
    {
        var fallback = ProbeByDirectDownloadFromNugetOrg(packageName, version, targetTfm);
        if (!fallback.HasDll) throw new Exception(fallback.Reason ?? "не удалось получить DLL из пакета");
        // Скопировать из распаковки во временной папке уже делает старый путь через ExtractPackage,
        // но здесь нам нужен явный копипаст:
        var src = Path.Combine(fallback.LibFolder!, (preferredDllName ?? fallback.DllName) + ".dll");
        var dst = Path.Combine(artifactsDir, (preferredDllName ?? fallback.DllName) + ".dll");
        File.Copy(src, dst, overwrite: true);
        CopySideBys(fallback.LibFolder!, artifactsDir);
        return preferredDllName ?? fallback.DllName!;
    }

    // 4) Копируем найденную в кэше DLL + сопутствующие рядом файлы (satellite dll, *.xml, т.п.)
    var dllName = preferredDllName ?? probe.DllName!;
    var srcDll = Path.Combine(probe.LibFolder!, dllName + ".dll");
    var dstDll = Path.Combine(artifactsDir, dllName + ".dll");
    Directory.CreateDirectory(artifactsDir);
    File.Copy(srcDll, dstDll, overwrite: true);
    CopySideBys(probe.LibFolder!, artifactsDir);
    Console.WriteLine($"Выбрана сборка {dllName}.dll из {probe.LibFolder}");
    return dllName;
}

private static void CopySideBys(string libFolder, string artifactsDir)
{
    foreach (var f in Directory.EnumerateFiles(libFolder))
    {
        var ext = Path.GetExtension(f);
        // Кладём всё полезное рядом с DLL
        if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".pri", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            var to = Path.Combine(artifactsDir, Path.GetFileName(f));
            if (!File.Exists(to)) File.Copy(f, to, overwrite: true);
        }
    }
}

// --------- ХЕЛПЕРЫ ДЛЯ ПРОБЫ ИЗ ГЛОБАЛЬНОГО КЭША ---------

private static ProbeResult? TryProbeFromGlobalPackages(string packageName, string version, string targetTfm)
{
    var root = GetGlobalPackagesFolder();
    var idLower = packageName.ToLowerInvariant();
    var verFolder = Path.Combine(root, idLower, version);
    if (!Directory.Exists(verFolder)) return null;

    var libFolder = GetBestLibFolder(verFolder, targetTfm);
    if (libFolder == null)
        return new ProbeResult { HasDll = false, Reason = $"не найден совместимый lib/* для {targetTfm}" };

    var dllName = ChooseAssemblyName(libFolder, packageName);
    if (dllName == null)
        return new ProbeResult { HasDll = false, Reason = "в lib/* нет *.dll" };

    return new ProbeResult { HasDll = true, DllName = dllName, LibFolder = libFolder };
}

private static string GetGlobalPackagesFolder()
{
    var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
    if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".nuget", "packages");
}

private static bool EnsureInGlobalPackagesByRestore(string packageName, string version)
{
    var tempDir = CreateTemporaryDirectory();
    try
    {
        RunProcessOrThrow("dotnet", "new classlib -n _TmpProbe -f net8.0", tempDir, "Создание временного проекта", 120000);
        var projDir = Path.Combine(tempDir, "_TmpProbe");
        var projPath = Path.Combine(projDir, "_TmpProbe.csproj");

        RunProcessOrThrow("dotnet", $"add \"{projPath}\" package {packageName} --version {version}",
            tempDir, "Добавление пакета в временный проект", 120000);

        // Используются все источники из NuGet.Config пользователя/репозитория/solution.
        RunProcessOrThrow("dotnet",
            $"restore \"{projPath}\" -p:TreatWarningsAsErrors=false -p:NoWarn=NU1605",
            tempDir, "Восстановление пакета (загрузка в глобальный кэш)", 180000);

        return true;
    }
    catch
    {
        // Не валим — пусть дальше сработает фолбэк на прямую загрузку/ошибка «не найден»
        return false;
    }
    finally
    {
        TryDeleteDirectory(tempDir);
    }
}

// Старая прямая загрузка — вынесена в отдельный метод (оставляем как фолбэк)
private static ProbeResult ProbeByDirectDownloadFromNugetOrg(string packageName, string version, string targetTfm)
{
    string tempDir = CreateTemporaryDirectory();
    try
    {
        string packageUrl = $"https://www.nuget.org/api/v2/package/{packageName}/{version}";
        string nupkgFile = DownloadPackage(packageUrl, packageName, version, tempDir);
        string extractDir = Path.Combine(tempDir, "extracted");
        ExtractPackage(nupkgFile, extractDir);

        string libFolder = GetBestLibFolder(extractDir, targetTfm);
        if (libFolder == null)
            return new ProbeResult { HasDll = false, Reason = $"не найден совместимый lib/* для {targetTfm}" };

        string actualDllName = ChooseAssemblyName(libFolder, packageName);
        if (actualDllName == null)
            return new ProbeResult { HasDll = false, Reason = "в lib/* нет *.dll" };

        return new ProbeResult { HasDll = true, DllName = actualDllName, LibFolder = libFolder };
    }
    catch (WebException wex)
    {
        return new ProbeResult { HasDll = false, Reason = "ошибка загрузки пакета: " + wex.Message };
    }
    catch (Exception ex)
    {
        return new ProbeResult { HasDll = false, Reason = "ошибка анализа пакета: " + ex.Message };
    }
    finally
    {
        TryDeleteDirectory(tempDir);
    }
}



        private static string GetBestLibFolder(string extractDir, string targetTfm)
        {
            string libRoot = Path.Combine(extractDir, "lib");
            if (!Directory.Exists(libRoot)) return null;

            var exact = Path.Combine(libRoot, targetTfm);
            if (Directory.Exists(exact) && Directory.GetFiles(exact, "*.dll").Any()) return exact;

            string[] fallbacks = new[]
            {
                "net8.0", "net7.0", "net6.0", "net5.0",
                "netcoreapp3.1",
                "netstandard2.1", "netstandard2.0",
                "net48", "net472"
            };

            var list = new List<string> {targetTfm};
            list.AddRange(fallbacks.Where(f => !f.Equals(targetTfm, StringComparison.OrdinalIgnoreCase)));

            foreach (var tfm in list)
            {
                var p = Path.Combine(libRoot, tfm);
                if (Directory.Exists(p) && Directory.GetFiles(p, "*.dll").Any())
                    return p;
            }

            var candidates = Directory.GetDirectories(libRoot);
            if (candidates.Length == 1 && Directory.GetFiles(candidates[0], "*.dll").Any())
                return candidates[0];

            return Directory.GetFiles(libRoot, "*.dll").Any() ? libRoot : null;
        }

        private static string ChooseAssemblyName(string libFolder, string packageName)
        {
            var dlls = Directory.GetFiles(libFolder, "*.dll")
                .Where(p => !Path.GetFileNameWithoutExtension(p)
                    .EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count == 0) return null;

            string lastSeg = packageName.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries).Last();
            var exact = dlls.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), lastSeg, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return Path.GetFileNameWithoutExtension(exact);

            string idCompact = Regex.Replace(packageName, @"[.\-]", "", RegexOptions.Compiled);
            var compact = dlls.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p).Replace(".", "").Replace("-", ""),
                    idCompact, StringComparison.OrdinalIgnoreCase));
            if (compact != null) return Path.GetFileNameWithoutExtension(compact);

            if (dlls.Count == 1) return Path.GetFileNameWithoutExtension(dlls[0]);

            var biggest = dlls.OrderByDescending(f => new FileInfo(f).Length).First();
            return Path.GetFileNameWithoutExtension(biggest);
        }

        private static string CreateTemporaryDirectory()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "NuGetPackage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        private static string DownloadPackage(string packageUrl, string packageName, string version, string tempDir)
        {
            string nupkgFile = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
            using (var client = new WebClient())
            {
                client.DownloadFile(packageUrl, nupkgFile);
            }

            return nupkgFile;
        }

        private static void ExtractPackage(string nupkgFile, string extractDir)
        {
            ZipFile.ExtractToDirectory(nupkgFile, extractDir);
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Не удалось удалить временную папку: " + ex.Message);
            }
        }

        // ------------------ работа с символами / PDB ------------------

        private static bool TryDownloadSymbols(string workingDir, string dllFile, string dllDir,
            string serverPath = null)
        {
            string arguments = serverPath == null
                ? $"symbol download --output \"{dllDir}\" \"{dllFile}\""
                : $"symbol download --server-path \"{serverPath}\" --output \"{dllDir}\" \"{dllFile}\"";

            RunProcess("dotnet", arguments, workingDir);
            string pdbPath = Path.ChangeExtension(dllFile, ".pdb");
            return File.Exists(pdbPath);
        }

        private static void DownloadSymbols(string workingDir, string artifactsDir, string dllName,
            List<string> warnings)
        {
            string dllFile = Path.Combine(artifactsDir, dllName + ".dll");
            if (!File.Exists(dllFile))
            {
                string msg = $"Предупреждение: файл DLL не найден для загрузки символов: {dllFile}";
                Console.WriteLine(msg);
                warnings.Add(msg);
                return;
            }

            Console.WriteLine($"Загрузка символов для: {dllFile}");
            string dllDir = Path.GetDirectoryName(dllFile)!;

            if (TryDownloadSymbols(workingDir, dllFile, dllDir))
            {
                Console.WriteLine($"Символы для {dllFile} успешно загружены.");
            }
            else
            {
                Console.WriteLine($"Не найден PDB для {dllFile}. Пытаюсь nuget.org...");
                if (TryDownloadSymbols(workingDir, dllFile, dllDir, "https://symbols.nuget.org/download/symbols"))
                {
                    Console.WriteLine($"Символы для {dllFile} успешно загружены с nuget.org.");
                }
                else
                {
                    Console.WriteLine($"Предупреждение: символы не найдены. Пробую ILDasm/ILAsm для генерации PDB...");
                    if (RebuildDllWithSymbols(workingDir, dllFile, out string rebuiltDllFile))
                    {
                        Console.WriteLine($"Ребилд завершён. Сборка: {rebuiltDllFile}");
                    }
                    else
                    {
                        string msg = $"Предупреждение: не удалось загрузить или сгенерировать символы для {dllFile}";
                        Console.WriteLine(msg);
                        warnings.Add(msg);
                    }
                }
            }
        }

        private static bool RebuildDllWithSymbols(string workingDir, string dllFile, out string rebuiltDllFile)
        {
            rebuiltDllFile = Path.Combine(
                Path.GetDirectoryName(dllFile)!,
                Path.GetFileNameWithoutExtension(dllFile) + ".rebuilt" + Path.GetExtension(dllFile));

            string ildasmPath = @"C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe";
            string ilasmPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe";

            string ilFile = Path.ChangeExtension(dllFile, ".il");

            int ildasmExit = RunProcess(ildasmPath, $"\"{dllFile}\" /out:\"{ilFile}\"", workingDir);
            if (ildasmExit != 0 || !File.Exists(ilFile))
            {
                Console.WriteLine("Ошибка дизассемблирования DLL через ILDasm.");
                return false;
            }

            string ilText = File.ReadAllText(ilFile);
            ilText = ilText.Replace("ldc.r8     -inf", "ldc.i8 0xfff0000000000000\r\n            conv.r8");
            ilText = ilText.Replace("ldc.r8     inf", "ldc.i8 0x7ff0000000000000\r\n            conv.r8");
            ilText = ilText.Replace("ldc.r8     -nan(ind)", "ldc.i8 0xfff8000000000000\r\n            conv.r8");
            ilText = ilText.Replace("ldc.r4     -inf", "ldc.i4 0xff800000\r\n            conv.r4");
            ilText = ilText.Replace("ldc.r4     inf", "ldc.i4 0x7f800000\r\n            conv.r4");
            ilText = ilText.Replace("ldc.r4     -nan(ind)", "ldc.i4 0xffc00000\r\n            conv.r4");

            ilText = Regex.Replace(
                ilText,
                @"(?<=\.method\s+)(?:private|family|assembly)",
                "public",
                RegexOptions.Multiline);

            File.WriteAllText(ilFile, ilText);

            int ilasmExit = RunProcess(ilasmPath, $"\"{ilFile}\" /dll /debug /output:\"{rebuiltDllFile}\"", workingDir);
            if (ilasmExit != 0)
            {
                Console.WriteLine("Ошибка сборки IL файла через ILAsm.");
            }

            string rebuiltPdbFile = Path.ChangeExtension(rebuiltDllFile, ".pdb");
            string targetPdbFile = Path.ChangeExtension(dllFile, ".pdb");

            if (File.Exists(rebuiltPdbFile))
            {
                try
                {
                    if (File.Exists(targetPdbFile))
                    {
                        File.Delete(targetPdbFile);
                    }

                    File.Move(rebuiltPdbFile, targetPdbFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка переименования PDB файла: " + ex.Message);
                    return false;
                }
            }
            else
            {
                Console.WriteLine("PDB файл не создан ILAsm.");
                return false;
            }

            File.Delete(ilFile);
            return File.Exists(targetPdbFile);
        }

        // ------------------ тесты и покрытие ------------------

        private static void PrepareAndValidateTestFile(string solutionDir, string artifactsDir, string dllName,
            string projectName, List<string> warnings)
        {
            string sourceTestFile = Path.Combine(AppContext.BaseDirectory, "NugetDllAnalyzerTests.cs.txt");
            if (!File.Exists(sourceTestFile))
                throw new FileNotFoundException(
                    "Исходный файл тестов NugetDllAnalyzerTests.cs.txt не найден в папке с исполняемым файлом.");

            string testProjectDir = Path.Combine(solutionDir, projectName);
            string targetTestFile = Path.Combine(testProjectDir, "NugetDllAnalyzerTests.cs");

            string testContent = File.ReadAllText(sourceTestFile);
            string newDllName = dllName + ".dll";
            testContent = testContent.Replace("TestingNugetLibrary.dll", newDllName);
            File.WriteAllText(targetTestFile, testContent);
            Console.WriteLine($"[{projectName}] Тестовый файл скопирован и обновлён: {targetTestFile}");

            string dllPath = Path.Combine(artifactsDir, newDllName);
            if (!File.Exists(dllPath))
                throw new Exception($"Файл {newDllName} не найден в папке сборки: {artifactsDir}");

            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (!File.Exists(pdbPath))
            {
                string msg = $"Предупреждение: PDB файл для {newDllName} не найден в {artifactsDir}";
                Console.WriteLine(msg);
                warnings.Add(msg);
            }
            else
            {
                Console.WriteLine($"[{projectName}] Проверка файлов завершена: {dllPath} и {pdbPath} существуют.");
            }
        }

        private static void ConsolidateTestResults(string solutionDir)
        {
            string testResultsDir = Path.Combine(solutionDir, "TestResults");
            if (!Directory.Exists(testResultsDir))
            {
                Console.WriteLine("Папка TestResults не найдена.");
                return;
            }

            string[] subDirs = Directory.GetDirectories(testResultsDir);
            foreach (var subDir in subDirs)
            {
                string[] files = Directory.GetFiles(subDir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string destFile = Path.Combine(testResultsDir, Path.GetFileName(file));
                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }

                    File.Move(file, destFile);
                }

                Directory.Delete(subDir, true);
            }

            Console.WriteLine("Результаты тестирования перемещены в папку TestResults, подпапки удалены.");
        }

        private static void DisplayCoverageResults(string testResultsDir, string dllName)
        {
            string coverageFile = Path.Combine(testResultsDir, "outfile.xml");
            if (!File.Exists(coverageFile))
            {
                Console.WriteLine("Файл outfile.xml не найден в папке TestResults.");
                return;
            }

            try
            {
                var xdoc = XDocument.Load(coverageFile);
                string targetModuleName = dllName + ".dll";
                var module = xdoc.Descendants("module")
                    .FirstOrDefault(m => string.Equals((string) m.Attribute("name"), targetModuleName,
                        StringComparison.OrdinalIgnoreCase));
                if (module != null)
                {
                    string blockCoverage = module.Attribute("block_coverage")?.Value;
                    string lineCoverage = module.Attribute("line_coverage")?.Value;
                    if (!string.IsNullOrEmpty(blockCoverage) && !string.IsNullOrEmpty(lineCoverage))
                    {
                        Console.WriteLine($"Покрытие (блоки) для {targetModuleName}: {blockCoverage}%");
                        Console.WriteLine($"Покрытие (строки) для {targetModuleName}: {lineCoverage}%");
                    }
                    else
                    {
                        Console.WriteLine($"Не найдены атрибуты block_coverage/line_coverage для {targetModuleName}.");
                    }
                }
                else
                {
                    Console.WriteLine($"Модуль {targetModuleName} не найден в outfile.xml.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка при обработке outfile.xml: " + ex.Message);
            }
        }

        private static void WriteReport(
            string solutionDir,
            string dotnetVersion,
            List<(string Package, string Version, string Project, string DllName)> successes,
            List<(string Package, string Version, string Reason)> failures,
            List<string> warnings)
        {
            string reportPath = Path.Combine(solutionDir, "TestPackagesReport.txt");
            using var sw = new StreamWriter(reportPath, false, System.Text.Encoding.UTF8);

            sw.WriteLine($"Отчёт по подготовке тестового решения");
            sw.WriteLine($"TFM: {dotnetVersion}");
            sw.WriteLine($"Дата/время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine(new string('-', 60));

            sw.WriteLine($"Успешно добавлены проекты: {successes.Count}");
            foreach (var s in successes.OrderBy(x => x.Project))
                sw.WriteLine($"  - {s.Project}: {s.Package} {s.Version} → {s.DllName}.dll");

            sw.WriteLine();
            sw.WriteLine($"Пропущенные пакеты: {failures.Count}");
            foreach (var f in failures.OrderBy(x => x.Package))
                sw.WriteLine($"  - {f.Package} {f.Version}: {f.Reason}");

            if (warnings.Count > 0)
            {
                sw.WriteLine();
                sw.WriteLine($"Предупреждения: {warnings.Count}");
                foreach (var w in warnings)
                    sw.WriteLine("  - " + w);
            }

            sw.Flush();
            Console.WriteLine($"Отчёт сформирован: {reportPath}");
        }

        // ------------------ запуск процессов ------------------

        private static void RunProcessOrThrow(string fileName, string arguments, string workingDirectory,
            string stepDescription, int timeoutMs = 300000)
        {
            int exitCode = RunProcess(fileName, arguments, workingDirectory, timeoutMs);
            if (exitCode != 0)
                throw new Exception($"Ошибка при выполнении шага '{stepDescription}'. Код выхода: {exitCode}");
        }

        private static int RunProcess(string fileName, string arguments, string? workingDirectory = null,
            int timeoutMs = 300000)
        {
            Console.WriteLine($"> {fileName} {arguments}");
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
            };
            using (var proc = new Process())
            {
                proc.StartInfo = psi;
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null) Console.WriteLine(e.Data);
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null) Console.WriteLine(e.Data);
                };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                bool exited = proc.WaitForExit(timeoutMs);
                if (!exited)
                {
                    Console.WriteLine("Процесс не завершился в установленное время. Завершаю процесс...");
                    try
                    {
                        proc.Kill();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ошибка при завершении процесса: " + ex.Message);
                    }

                    return -1;
                }

                return proc.ExitCode;
            }
        }

        private static void WriteReadme(
            string solutionDir,
            string dotnetVersion,
            List<(string Package, string Version, string Project, string DllName)> successes,
            List<(string Package, string Version, string Reason)> failures)
        {
            string readmeFile = Path.Combine(solutionDir, "README.md");
            using var sw = new StreamWriter(readmeFile, false, System.Text.Encoding.UTF8);

            sw.WriteLine("# Тестовое решение по пакетам NuGet");
            sw.WriteLine();
            sw.WriteLine($"**Target Framework:** `{dotnetVersion}`");
            sw.WriteLine();
            sw.WriteLine("## Как запустить");
            sw.WriteLine();
            sw.WriteLine("```bash");
            sw.WriteLine("dotnet test --blame --collect:\"Code Coverage\" --results-directory \"TestResults\"");
            sw.WriteLine("```");
            sw.WriteLine();
            sw.WriteLine("Результаты покрытия консолидируются в `TestResults/outfile.xml` (создаётся программой).");
            sw.WriteLine();
            sw.WriteLine("## Состав тестируемых проектов");
            sw.WriteLine();
            if (successes.Count == 0)
            {
                sw.WriteLine("_Нет добавленных тест-проектов. См. раздел ниже про пропущенные пакеты._");
            }
            else
            {
                sw.WriteLine("| Проект | Пакет | Версия | DLL |");
                sw.WriteLine("|---|---|---|---|");
                foreach (var s in successes.OrderBy(x => x.Project))
                    sw.WriteLine($"| `{s.Project}` | `{s.Package}` | `{s.Version}` | `{s.DllName}.dll` |");
            }

            sw.WriteLine();
            sw.WriteLine("## Пропущенные пакеты");
            sw.WriteLine();
            if (failures.Count == 0)
            {
                sw.WriteLine("_Нет пропусков._");
            }
            else
            {
                foreach (var f in failures.OrderBy(x => x.Package))
                    sw.WriteLine($"- `{f.Package}` {f.Version}: {f.Reason}");
            }

            sw.WriteLine();
            sw.WriteLine("## Примечания");
            sw.WriteLine(
                "- Предупреждения (например, отсутствие PDB) не останавливают выполнение; детали см. в `TestPackagesReport.txt`.");
            sw.WriteLine("- Предупреждения NuGet типа `NU1605` приглушены для `restore/build/test`.");
        }

        // ------------------ аргументы ------------------

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                if (arg.StartsWith("--"))
                {
                    var parts = arg.Substring(2).Split('=', 2);
                    if (parts.Length == 2)
                        dict[parts[0]] = parts[1];
                }
            }

            return dict;
        }
    }
}