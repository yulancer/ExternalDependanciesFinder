namespace NuGetAccessChecker.Models;

public sealed class AccessCheckReport
{
    public string ProjectPath { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public IReadOnlyList<PackageAccessResult> Results { get; init; } = Array.Empty<PackageAccessResult>();

    public int TotalChecked => Results.Count;
    public int TotalAvailable => Results.Count(r => r.IsAvailable);
    public int TotalUnavailable => Results.Count(r => !r.IsAvailable);

    public IEnumerable<PackageAccessResult> Available => Results.Where(r => r.IsAvailable);
    public IEnumerable<PackageAccessResult> Unavailable => Results.Where(r => !r.IsAvailable);

    public IReadOnlyDictionary<string, int> SourceHitCounters =>
        Available
            .Where(r => !string.IsNullOrWhiteSpace(r.SourceName))
            .GroupBy(r => r.SourceName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
