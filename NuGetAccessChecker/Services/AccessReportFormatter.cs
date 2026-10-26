using System.Text;
using NuGetAccessChecker.Models;

namespace NuGetAccessChecker.Services;

public sealed class AccessReportFormatter
{
    public string Format(AccessCheckReport report)
    {
        var lines = new List<string>
        {
            $"ПРОЕКТ: {report.ProjectPath}",
            $"ДАТА: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}",
            "",
            "СТАТИСТИКА:",
            $"  Всего проверено: {report.TotalChecked}",
            $"  Доступно:        {report.TotalAvailable}",
            $"  Недоступно:      {report.TotalUnavailable}",
            "",
            "ПОПАДАНИЯ ПО ИСТОЧНИКАМ:"
        };

        if (report.SourceHitCounters.Count == 0)
            lines.Add("  (нет)");
        else
        {
            foreach (var kv in report.SourceHitCounters.OrderByDescending(k => k.Value))
                lines.Add($"  {kv.Key}: {kv.Value}");
        }

        lines.Add("");
        lines.Add("ДОСТУПНЫЕ ПАКЕТЫ И ИСТОЧНИК:");
        var available = report.Available.ToList();
        if (available.Count == 0)
            lines.Add("(нет)");
        else
        {
            foreach (var item in available)
            {
                var versionNote = item.IsExactMatch
                    ? item.RequiredVersion.ToNormalizedString()
                    : $"{item.RequiredVersion.ToNormalizedString()} -> {item.FoundVersion!.ToNormalizedString()}";

                lines.Add($" - {item.PackageId} {versionNote}  ->  {item.SourceName} ({item.SourceUrl})");
            }
        }

        lines.Add("");
        lines.Add("НЕДОСТУПНЫЕ ПАКЕТЫ:");
        var unavailable = report.Unavailable.ToList();
        if (unavailable.Count == 0)
            lines.Add("(нет)");
        else
        {
            foreach (var item in unavailable)
                lines.Add($" - {item.PackageId} {item.RequiredVersion}");
        }

        lines.Add("");
        lines.Add("ДЕТАЛИ ПО ИСТОЧНИКАМ ДЛЯ НЕДОСТУПНЫХ:");
        if (unavailable.Count == 0)
        {
            lines.Add("(нет проблем)");
        }
        else
        {
            foreach (var item in unavailable)
            {
                lines.Add($"{item.PackageId} {item.RequiredVersion}");
                foreach (var msg in item.SourceMessages)
                    lines.Add($"  {msg}");
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public async Task WriteAsync(AccessCheckReport report, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, Format(report), Encoding.UTF8);
    }
}
