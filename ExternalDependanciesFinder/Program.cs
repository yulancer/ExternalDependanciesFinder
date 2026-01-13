using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var scanRoot = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var maxByPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

// 1) Собираем макс. версии по всем проектам/решению
var sln = Directory.EnumerateFiles(scanRoot, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
if (sln is not null)
{
    Console.WriteLine($"Found solution: {Path.GetFileName(sln)}");
    ProcessOneTarget(scanRoot, "dotnet", $"list \"{sln}\" package --include-transitive", maxByPackage);
}
else
{
    Console.WriteLine("No .sln found, scanning *.csproj ...");
    foreach (var csproj in Directory.EnumerateFiles(scanRoot, "*.csproj", SearchOption.AllDirectories))
    {
        Console.WriteLine($"Project: {Path.GetFileName(csproj)}");
        ProcessOneTarget(Path.GetDirectoryName(csproj)!, "dotnet", $"list \"{csproj}\" package --include-transitive", maxByPackage);
    }
}

// 2) Сохраняем результаты
var outputPath = Path.Combine(scanRoot, "packages_max.txt");
using (var sw = new StreamWriter(outputPath, false, Encoding.UTF8))
{
    foreach (var kv in maxByPackage.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
        var line = $"{kv.Key} => {kv.Value}";
        Console.WriteLine(line);
        sw.WriteLine(line);
    }
}
Console.WriteLine($"\nРезультат сохранён в {outputPath}");

// 3) Создаём NugetUsungSolution с единственным консольным проектом и добавляем все пакеты
var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var genRoot = Path.Combine(baseDir, "NugetUsungSolution");
var projName = "NugetUsungProject";
var slnName = "NugetUsungSolution";

RecreateDirectory(genRoot);

RunOrThrow("dotnet", $"new sln -n {slnName}", genRoot);
RunOrThrow("dotnet", $"new console -n {projName} --framework net8.0", genRoot);
RunOrThrow("dotnet", $"sln {slnName}.sln add \"{Path.Combine(genRoot, projName, $"{projName}.csproj")}\"", genRoot);

// Добавляем пакеты в проект (без restore на каждом шаге)
var projDir = Path.Combine(genRoot, projName);
foreach (var kv in maxByPackage)
{
    var id = kv.Key;
    var ver = kv.Value;

    // На всякий случай пропустим локальные префиксы фильтра (двойная страховка)
    if (id.StartsWith("Rbp.", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("Psb.", StringComparison.OrdinalIgnoreCase))
        continue;

    // Бывают версии с суффиксами; мы их принимаем как есть
    Console.WriteLine($"Adding package: {id} ({ver})");
    var exit = Run("dotnet", $"add \"{projDir}\" package {EscapeArg(id)} --version {EscapeArg(ver)} --no-restore", genRoot, out var _, out var err);
    if (exit != 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"WARN: failed to add {id} {ver}. dotnet exited {exit}. stderr:\n{err}");
        Console.ResetColor();
    }
}

// 4) Restore и Build решения
Console.WriteLine("\nRunning dotnet restore...");
RunOrThrow("dotnet", $"restore \"{Path.Combine(genRoot, $"{slnName}.sln")}\"", genRoot);

Console.WriteLine("Running dotnet build -c Release...");
RunOrThrow("dotnet", $"build \"{Path.Combine(genRoot, $"{slnName}.sln")}\" -c Release --nologo", genRoot);

Console.WriteLine($"\nГотово. Решение: {Path.Combine(genRoot, $"{slnName}.sln")}");

// ----------------- helpers -----------------

static void ProcessOneTarget(string workingDir, string fileName, string arguments, Dictionary<string, string> maxByPackage)
{
    var output = RunAndRead(fileName, arguments, workingDir, out var exit, out var stderr);
    if (exit != 0)
        Console.Error.WriteLine($"dotnet exited with code {exit}:\n{stderr}");

    using var reader = new StringReader(output);
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (!line.TrimStart().StartsWith(">")) continue;

        var afterArrow = line.TrimStart().TrimStart('>').TrimStart();
        if (string.IsNullOrWhiteSpace(afterArrow)) continue;

        var tokens = SplitTokens(afterArrow);
        if (tokens.Count == 0) continue;

        var packageId = tokens[0];

        // Фильтр префиксов
        if (packageId.StartsWith("Rbp.", StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith("Psb.", StringComparison.OrdinalIgnoreCase))
            continue;

        // ищем последний токен похожий на версию
        string? version = tokens.LastOrDefault(LooksLikeVersion);
        if (version is null) continue;

        if (maxByPackage.TryGetValue(packageId, out var current))
        {
            if (CompareNuGetVersions(version, current) > 0)
                maxByPackage[packageId] = version;
        }
        else
        {
            maxByPackage[packageId] = version;
        }
    }
}

static void RecreateDirectory(string path)
{
    if (Directory.Exists(path))
    {
        // удаляем аккуратно
        Directory.Delete(path, recursive: true);
        // возможна задержка файловой системы
        for (int i = 0; i < 10 && Directory.Exists(path); i++)
            Thread.Sleep(50);
    }
    Directory.CreateDirectory(path);
}

static string EscapeArg(string s)
{
    // простой экранировщик для аргументов dotnet
    return s.Any(char.IsWhiteSpace) ? $"\"{s}\"" : s;
}

static string RunAndRead(string fileName, string arguments, string workingDir, out int exitCode, out string stderr)
{
    var psi = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    using var p = Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEnd();
    stderr = p.StandardError.ReadToEnd();
    p.WaitForExit();
    exitCode = p.ExitCode;
    return stdout;
}

static int Run(string fileName, string arguments, string workingDir, out string stdout, out string stderr)
{
    stdout = RunAndRead(fileName, arguments, workingDir, out var exit, out stderr);
    return exit;
}

static void RunOrThrow(string fileName, string arguments, string workingDir)
{
    var exit = Run(fileName, arguments, workingDir, out var stdout, out var stderr);
    if (exit != 0)
    {
        Console.Error.WriteLine(stdout);
        throw new Exception($"{fileName} {arguments} failed with exit code {exit}:\n{stderr}");
    }
}

static List<string> SplitTokens(string s)
{
    var list = new List<string>();
    int i = 0;
    while (i < s.Length)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i >= s.Length) break;
        int j = i;
        while (j < s.Length && !char.IsWhiteSpace(s[j])) j++;
        list.Add(s.Substring(i, j - i));
        i = j;
    }
    return list;
}

static bool LooksLikeVersion(string token) =>
    Regex.IsMatch(token, @"^\d+(\.\d+){0,3}([\-+][0-9A-Za-z\.\+]+)?$");

// Сравнение версий похоже на NuGet: числа поразрядно, prerelease < release.
static int CompareNuGetVersions(string a, string b)
{
    ParseSemVer(a, out var anums, out var apre, out var apreParts);
    ParseSemVer(b, out var bnums, out var bpre, out var bpreParts);

    int maxLen = Math.Max(anums.Length, bnums.Length);
    for (int i = 0; i < maxLen; i++)
    {
        var ai = i < anums.Length ? anums[i] : 0;
        var bi = i < bnums.Length ? bnums[i] : 0;
        if (ai != bi) return ai.CompareTo(bi);
    }

    if (apre && !bpre) return -1;
    if (!apre && bpre) return 1;

    for (int i = 0; i < Math.Max(apreParts.Length, bpreParts.Length); i++)
    {
        if (i >= apreParts.Length) return -1;
        if (i >= bpreParts.Length) return 1;

        var x = apreParts[i];
        var y = bpreParts[i];

        var xIsNum = int.TryParse(x, out var xi);
        var yIsNum = int.TryParse(y, out var yi);

        if (xIsNum && yIsNum)
        {
            var cmp = xi.CompareTo(yi);
            if (cmp != 0) return cmp;
        }
        else if (xIsNum) return -1;
        else if (yIsNum) return 1;
        else
        {
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(x, y);
            if (cmp != 0) return cmp;
        }
    }
    return 0;
}

static void ParseSemVer(string v, out int[] nums, out bool isPrerelease, out string[] preParts)
{
    var plusIdx = v.IndexOf('+');
    if (plusIdx >= 0) v = v[..plusIdx];

    string main = v;
    string pre = "";
    var dashIdx = v.IndexOf('-');
    if (dashIdx >= 0)
    {
        main = v[..dashIdx];
        pre = v[(dashIdx + 1)..];
    }

    nums = main.Split('.', StringSplitOptions.RemoveEmptyEntries)
               .Select(s => int.TryParse(s, out var n) ? n : 0)
               .ToArray();

    isPrerelease = dashIdx >= 0;
    preParts = string.IsNullOrEmpty(pre)
        ? Array.Empty<string>()
        : pre.Split('.', StringSplitOptions.RemoveEmptyEntries);
}
