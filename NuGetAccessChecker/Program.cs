using System.Text.Json;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --project <path-to-csproj-or-folder> [--output <output-file>]");
    Console.WriteLine();
}

string? projectPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length) projectPath = args[++i];
    else if (args[i] == "--output" && i + 1 < args.Length) outputPath = args[++i];
}

if (string.IsNullOrWhiteSpace(projectPath))
{
    PrintUsage();
    return;
}

// Resolve project file
if (Directory.Exists(projectPath))
{
    var csproj = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
    if (csproj is null)
        throw new Exception($"В папке '{projectPath}' не найден .csproj");
    projectPath = csproj;
}
else if (!File.Exists(projectPath))
{
    throw new Exception($"Файл проекта '{projectPath}' не найден");
}

var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
outputPath ??= Path.Combine(projectDir, "unavailable-packages.txt");

// 1) Список пакетов и версий
var packages = await CollectPackagesWithVersionsAsync(projectDir, projectPath);

// 2) Источники NuGet
var settings = Settings.LoadDefaultSettings(projectDir);
var sourceProvider = new PackageSourceProvider(settings);
var packageSources = sourceProvider.LoadPackageSources()
    .Where(s => s.IsEnabled)
    .ToList();

if (packageSources.Count == 0)
{
    Console.WriteLine("Включённых источников NuGet не найдено. Проверьте NuGet.config.");
    return;
}

var logger = new ConsoleLogger();
var cache = new SourceCacheContext();

// Для отчёта
var unavailable = new List<string>();
var unavailableDetails = new List<string>();
var availableDetails = new List<string>(); // "Id Ver -> SourceName (URL)"
var sourceHitCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

int totalChecked = 0, totalAvailable = 0, totalUnavailable = 0;

foreach (var (id, version) in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
{
    totalChecked++;
    bool ok = false;
    string? okSourceName = null;
    string? okSourceUrl = null;

    var perSourceMsgs = new List<string>();

    foreach (var src in packageSources)
    {
        try
        {
            // В NuGet 4.3.1 сюда нужно передавать строку (URL), а не PackageSource
            var repo = Repository.Factory.GetCoreV3(src.Source);
            var find = await repo.GetResourceAsync<FindPackageByIdResource>();

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var nupkgStream = new MemoryStream();

            bool found = await find.CopyNupkgToStreamAsync(
                id, NuGetVersion.Parse(version), nupkgStream, cache, logger, cts.Token);

            if (found && nupkgStream.Length > 0)
            {
                ok = true;
                okSourceName = src.Name;
                okSourceUrl = src.SourceUri?.ToString() ?? src.Source;
                perSourceMsgs.Add($"[{src.Name}] OK");
                break; // достаточно одного успешного источника
            }
            else
            {
                perSourceMsgs.Add($"[{src.Name}] not found (404?)");
            }
        }
        catch (Exception ex)
        {
            // В 4.3.1 статусы HTTP доставать неудобно — пишем тип и сообщение
            perSourceMsgs.Add($"[{src.Name}] ERROR: {ex.GetType().Name} - {ex.Message}");
        }
    }

    if (!ok)
    {
        totalUnavailable++;
        unavailable.Add($"{id} {version}");
        unavailableDetails.Add($"{id} {version}\n  " + string.Join("\n  ", perSourceMsgs));
        Console.WriteLine($"Недоступен: {id} {version}");
    }
    else
    {
        totalAvailable++;
        Console.WriteLine($"Доступен:   {id} {version}  (источник: {okSourceName})");
        availableDetails.Add($"{id} {version}  ->  {okSourceName} ({okSourceUrl})");
        sourceHitCounters[okSourceName!] = sourceHitCounters.TryGetValue(okSourceName!, out var c) ? c + 1 : 1;
    }
}

// 4) Отчёт
var lines = new List<string>();
lines.Add($"ПРОЕКТ: {projectPath}");
lines.Add($"ДАТА: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
lines.Add("");
lines.Add("СТАТИСТИКА:");
lines.Add($"  Всего проверено: {totalChecked}");
lines.Add($"  Доступно:        {totalAvailable}");
lines.Add($"  Недоступно:      {totalUnavailable}");
lines.Add("");
lines.Add("ПОПАДАНИЯ ПО ИСТОЧНИКАМ:");
if (sourceHitCounters.Count == 0) lines.Add("  (нет)");
else foreach (var kv in sourceHitCounters.OrderByDescending(k => k.Value))
    lines.Add($"  {kv.Key}: {kv.Value}");
lines.Add("");
lines.Add("ДОСТУПНЫЕ ПАКЕТЫ И ИСТОЧНИК:");
lines.AddRange(availableDetails.Count == 0 ? new[] { "(нет)" } : availableDetails.Select(x => " - " + x));
lines.Add("");
lines.Add("НЕДОСТУПНЫЕ ПАКЕТЫ:");
lines.AddRange(unavailable.Count == 0 ? new[] { "(нет)" } : unavailable.Select(x => " - " + x));
lines.Add("");
lines.Add("ДЕТАЛИ ПО ИСТОЧНИКАМ ДЛЯ НЕДОСТУПНЫХ:");
lines.AddRange(unavailableDetails.Count == 0 ? new[] { "(нет проблем)" } : unavailableDetails);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
await File.WriteAllLinesAsync(outputPath, lines);

Console.WriteLine();
Console.WriteLine($"Готово. Отчёт: {outputPath}");


// ================= helpers =================

static async Task<HashSet<(string Id, string Version)>> CollectPackagesWithVersionsAsync(string projectDir, string projectPath)
{
    // Приоритет: packages.lock.json -> PackageReference + Directory.Packages.props
    var result = new HashSet<(string, string)>(StringTupleComparer.OrdinalIgnoreCase);

    var lockFile = Path.Combine(projectDir, "packages.lock.json");
    if (File.Exists(lockFile))
    {
        try
        {
            using var stream = File.OpenRead(lockFile);
            var json = await JsonDocument.ParseAsync(stream);

            if (json.RootElement.TryGetProperty("targets", out var targetsEl))
            {
                foreach (var tfmProp in targetsEl.EnumerateObject())
                {
                    foreach (var pkgProp in tfmProp.Value.EnumerateObject())
                    {
                        // ключ вида "Newtonsoft.Json/13.0.3"
                        var parts = pkgProp.Name.Split('/');
                        if (parts.Length == 2)
                        {
                            var id = parts[0];
                            var ver = parts[1];
                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                                result.Add((id, ver));
                        }
                        else
                        {
                            if (pkgProp.Value.TryGetProperty("resolved", out var resolvedEl))
                            {
                                var id = pkgProp.Name;
                                var ver = resolvedEl.GetString();
                                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                                    result.Add((id, ver!));
                            }
                        }
                    }
                }
                if (result.Count > 0) return result;
            }
        }
        catch
        {
            // fallback ниже
        }
    }

    // Fallback: читаем .csproj и Directory.Packages.props
    var centralVersions = LoadCentralPackageVersions(projectDir);
    var csprojXml = XDocument.Load(projectPath);
    var ns = csprojXml.Root?.Name.Namespace ?? XNamespace.None;

    foreach (var pr in csprojXml.Descendants(ns + "PackageReference"))
    {
        var id = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
        if (string.IsNullOrWhiteSpace(id)) continue;

        var ver = pr.Attribute("Version")?.Value
                  ?? pr.Attribute("VersionOverride")?.Value
                  ?? pr.Element(ns + "Version")?.Value;

        if (string.IsNullOrWhiteSpace(ver))
        {
            if (centralVersions.TryGetValue(id!, out var v))
                ver = v;
        }

        if (!string.IsNullOrWhiteSpace(ver) && !ver.Contains('$') && NuGetVersion.TryParse(ver, out _))
            result.Add((id!, ver!));
    }

    return result;
}

static Dictionary<string, string> LoadCentralPackageVersions(string startDir)
{
    // Ищем Directory.Packages.props вверх по дереву
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var dir = new DirectoryInfo(startDir);
    while (dir != null)
    {
        var propsPath = Path.Combine(dir.FullName, "Directory.Packages.props");
        if (File.Exists(propsPath))
        {
            try
            {
                var xml = XDocument.Load(propsPath);
                var ns = xml.Root?.Name.Namespace ?? XNamespace.None;
                foreach (var pv in xml.Descendants(ns + "PackageVersion"))
                {
                    var id = pv.Attribute("Include")?.Value;
                    var ver = pv.Attribute("Version")?.Value;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(ver))
                        result[id!] = ver!;
                }
            }
            catch
            {
                // игнорируем ошибки чтения конкретного props
            }
        }
        dir = dir.Parent;
    }
    return result;
}

// === Логгер под NuGet 4.3.1 (ILogger) ===
sealed class ConsoleLogger : ILogger
{
    private static void Print(string level, string data)
    {
        Console.WriteLine($"[{level}] {data}");
    }

    public void LogDebug(string data) { /* подавляем шум */ }
    public void LogVerbose(string data) { /* подавляем шум */ }
    public void LogInformation(string data) { Print("Information", data); }
    public void LogMinimal(string data) { Print("Minimal", data); }
    public void LogWarning(string data) { Print("Warning", data); }
    public void LogError(string data) { Print("Error", data); }
    public void LogInformationSummary(string data) { Print("InfoSummary", data); }

    public void Log(LogLevel level, string data)
    {
        if (level == LogLevel.Debug || level == LogLevel.Verbose) return;
        Print(level.ToString(), data);
    }

    public async Task LogAsync(LogLevel level, string data)
    {
        Log(level, data);
    }

    public void Log(ILogMessage message)
    {
        Log(message.Level, message.Message);
    }

    public async Task LogAsync(ILogMessage message)
    {
        Log(message);
    }
}

sealed class StringTupleComparer : IEqualityComparer<(string, string)>
{
    public static readonly StringTupleComparer OrdinalIgnoreCase = new();
    public bool Equals((string, string) x, (string, string) y) =>
        string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string, string) obj) =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1) * 397 ^
        StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2);
}
