using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string NuGetFlat = "https://api.nuget.org/v3-flatcontainer";

static bool IsNet6Tfm(string tfm) => tfm.StartsWith("net6.0", StringComparison.OrdinalIgnoreCase);
static bool IsNet8Tfm(string tfm) => tfm.StartsWith("net8.0", StringComparison.OrdinalIgnoreCase);
static bool IsNet9Tfm(string tfm) => tfm.StartsWith("net9.0", StringComparison.OrdinalIgnoreCase);
static bool IsStableVersion(string v) => v.IndexOf('-') < 0; // semver: без пререлиза

if (args.Length == 0)
{
    Console.Error.WriteLine("Укажите путь к .csproj. Пример:\n  Net8Fixer.exe \"F:\\Projects\\App\\App.csproj\"");
    return;
}

var csprojPath = args[0];
if (!File.Exists(csprojPath))
{
    Console.Error.WriteLine($"Файл не найден: {csprojPath}");
    return;
}

var http = CreateHttpClient();
var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
var ns = doc.Root!.Name.Namespace;

// берем только прямые PackageReference с явной версией
var packages = doc.Descendants(ns + "PackageReference")
    .Select(pr => new
    {
        Node = pr,
        Id = pr.Attribute("Include")?.Value?.Trim(),
        Version = pr.Attribute("Version")?.Value?.Trim()
                  ?? pr.Element(ns + "Version")?.Value?.Trim()
    })
    .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Version))
    .ToList();

if (packages.Count == 0)
{
    Console.WriteLine("Не найдено ни одного PackageReference с явной версией.");
    return;
}

bool modified = false;
var updated = new List<string>();
var errors = new List<string>();

foreach (var p in packages)
{
    var id = p.Id!;
    var current = p.Version!;
    Console.WriteLine($"\n=== {id} {current} ===");

    var currentTfms = await GetSupportedTfmsAsync(http, id, current);
    if (currentTfms is null || currentTfms.Count == 0)
    {
        Console.WriteLine("INFO: Не удалось определить TFM текущей версии (нет lib/ref или nupkg недоступен). Пропускаю.");
        continue;
    }

    bool hasNet6 = currentTfms.Any(IsNet6Tfm);
    bool hasNet8 = currentTfms.Any(IsNet8Tfm);

    // Повышаем только те, у кого есть net6 и НЕТ net8
    if (!hasNet6)
    {
        Console.WriteLine("OK/SKIP: текущая версия не имеет net6.* — правило повышения не применяется.");
        continue;
    }
    if (hasNet8)
    {
        Console.WriteLine("OK: текущая версия уже имеет net8.* — без изменений.");
        continue;
    }

    Console.WriteLine("NEEDS UPGRADE: текущая версия имеет net6.*, но не имеет net8.* — ищу стабильную версию с net8.*");

    var (best, reason) = await FindBestStableNet8WithoutNet9Async(http, id);
    if (best is null)
    {
        var msg = $"{id} {current}: не удалось обновить — {reason}";
        Console.WriteLine("WARN: " + msg);
        errors.Add(msg);
        continue;
    }

    // Обновляем csproj
    var versionAttr = p.Node.Attribute("Version");
    if (versionAttr != null) versionAttr.Value = best;
    else
    {
        var verEl = p.Node.Element(ns + "Version");
        if (verEl != null) verEl.Value = best;
        else p.Node.Add(new XElement(ns + "Version", best));
    }

    updated.Add($"{id}: {current} → {best} (stable, net8.*)");
    Console.WriteLine($"SELECTED: {id} {best}");
    modified = true;
}

// Сохраняем изменения и отчёт
var projDir = Path.GetDirectoryName(Path.GetFullPath(csprojPath))!;
var reportPath = Path.Combine(projDir, "net8fixer_report.txt");

if (modified)
{
    doc.Save(csprojPath);
    Console.WriteLine($"\nФайл обновлён: {csprojPath}");
}

// Пишем отчёт всегда, чтобы были ошибки, даже если не обновляли
using (var sw = new StreamWriter(reportPath, false, Encoding.UTF8))
{
    sw.WriteLine("Updated packages:");
    if (updated.Count == 0) sw.WriteLine("(none)");
    else foreach (var u in updated) sw.WriteLine(u);

    sw.WriteLine();
    sw.WriteLine("Errors:");
    if (errors.Count == 0) sw.WriteLine("(none)");
    else foreach (var e in errors) sw.WriteLine(e);
}
Console.WriteLine($"Отчёт сохранён: {reportPath}");

// restore/build на случай обновлений
Console.WriteLine("\n-- dotnet restore --");
Run("dotnet", $"restore \"{csprojPath}\"", projDir);

Console.WriteLine("\n-- dotnet build -c Release --");
Run("dotnet", $"build \"{csprojPath}\" -c Release --nologo", projDir);

// ================= Helpers =================

static HttpClient CreateHttpClient()
{
    var h = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });
    h.Timeout = TimeSpan.FromSeconds(60);
    h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Net8Fixer", "1.3"));
    h.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
    h.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
    return h;
}

// Читает базовые TFM из lib/ и ref/ (без суффиксов RID/OS: net8.0, net6.0, netstandard2.1)
static async Task<HashSet<string>?> GetSupportedTfmsAsync(HttpClient http, string packageId, string version)
{
    try
    {
        var (ok, data) = await TryFetchNupkgAsync(http, packageId, version);
        if (!ok) return null;

        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        var tfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("ref/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = path.Split('/');
                if (parts.Length >= 3)
                {
                    var tfm = parts[1];               // e.g. net8.0-windows10.0.19041.0
                    var baseTfm = tfm.Split('-')[0];  // -> net8.0
                    if (!string.IsNullOrWhiteSpace(baseTfm))
                        tfms.Add(baseTfm);
                }
            }
        }
        return tfms;
    }
    catch { return null; }
}

// Ищет МАКСИМАЛЬНУЮ стабильную версию, которая:
//  - имеет артефакты для net8.*
//  - НЕ имеет артефактов для net9.*
// Возвращает (bestVersion, failReason) — если bestVersion == null, используйте failReason в отчёте.
static async Task<(string? best, string? reason)> FindBestStableNet8WithoutNet9Async(HttpClient http, string packageId)
{
    var lowerId = packageId.ToLowerInvariant();
    var indexUrl = $"{NuGetFlat}/{Uri.EscapeDataString(lowerId)}/index.json";
    var json = await SafeGetStringAsync(http, indexUrl);
    if (string.IsNullOrEmpty(json))
        return (null, "не удалось получить список версий с nuget.org");

    var versions = ExtractVersionsFromIndex(json);
    if (versions.Count == 0)
        return (null, "у пакета нет опубликованных версий");

    bool sawOnlyPrereleaseNet8 = false;

    for (int i = versions.Count - 1; i >= 0; i--)
    {
        var v = versions[i];
        var tfms = await GetSupportedTfmsAsync(http, packageId, v);
        if (tfms is null || tfms.Count == 0) continue;

        bool has8 = tfms.Any(IsNet8Tfm);
       
        if (!has8) continue;

        if (!IsStableVersion(v))
        {
            sawOnlyPrereleaseNet8 = true;
            continue;
        }

        // стабильная с net8.* найдена
        return (v, null);
    }


    if (sawOnlyPrereleaseNet8)
        return (null, "поддержка net8.* найдена только в prerelease-версиях (beta/rc)");

    return (null, "не найдено ни одной версии с поддержкой net8.*");
}

static List<string> ExtractVersionsFromIndex(string indexJson)
{
    var m = Regex.Match(indexJson, @"""versions""\s*:\s*\[(?<arr>[^\]]*)\]", RegexOptions.IgnoreCase);
    if (!m.Success) return new List<string>();
    var arr = m.Groups["arr"].Value;
    var matches = Regex.Matches(arr, @"""([^""]+)""");
    return matches.Select(mm => mm.Groups[1].Value).ToList();
}

static async Task<string?> SafeGetStringAsync(HttpClient http, string url)
{
    try
    {
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync();
    }
    catch { return null; }
}

static async Task<(bool ok, byte[] data)> TryFetchNupkgAsync(HttpClient http, string packageId, string version)
{
    try
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();
        var url = $"{NuGetFlat}/{Uri.EscapeDataString(id)}/{Uri.EscapeDataString(ver)}/{Uri.EscapeDataString(id)}.{Uri.EscapeDataString(ver)}.nupkg";
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return (false, Array.Empty<byte>());
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        return (true, bytes);
    }
    catch { return (false, Array.Empty<byte>()); }
}

static int Run(string fileName, string args, string workingDir)
{
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = fileName,
        Arguments = args,
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = System.Diagnostics.Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARN: {fileName} {args}\nexit={p.ExitCode}\n{stderr}");
        Console.ResetColor();
    }
    else
    {
        Console.WriteLine(stdout);
    }
    return p.ExitCode;
}
