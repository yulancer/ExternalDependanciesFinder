using NuGet.Versioning;

namespace NuGetAccessChecker.Models;

public sealed class PackageAccessResult
{
    public string PackageId { get; init; } = string.Empty;
    public NuGetVersion RequiredVersion { get; init; } = new NuGetVersion(0, 0, 0);
    public bool IsAvailable { get; init; }
    public NuGetVersion? FoundVersion { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public IReadOnlyList<string> SourceMessages { get; init; } = Array.Empty<string>();

    public bool IsExactMatch =>
        IsAvailable && FoundVersion is not null && FoundVersion == RequiredVersion;

    public string DisplayRequired => $"{PackageId} {RequiredVersion}";

    public string DisplayFound =>
        FoundVersion is null
            ? DisplayRequired
            : IsExactMatch
                ? DisplayRequired
                : $"{PackageId} {RequiredVersion} (найдена {FoundVersion})";
}
