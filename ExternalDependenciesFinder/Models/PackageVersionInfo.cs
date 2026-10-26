using NuGet.Versioning;

namespace ExternalDependenciesFinder.Models;

public sealed class PackageVersionInfo
{
    public PackageVersionInfo(string originalVersion, NuGetVersion parsedVersion)
    {
        OriginalVersion = originalVersion;
        ParsedVersion = parsedVersion;
    }

    public string OriginalVersion { get; }
    public NuGetVersion ParsedVersion { get; }
}
