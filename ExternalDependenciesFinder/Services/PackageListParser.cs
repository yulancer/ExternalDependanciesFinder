using NuGet.Versioning;

namespace ExternalDependenciesFinder.Services;

public sealed class PackageListParser
{
    public IReadOnlyList<ParsedPackageLine> Parse(
        string stdout,
        Func<string, bool>? isExcluded = null,
        Action<string, string>? onUnparseableLine = null)
    {
        var packages = new List<ParsedPackageLine>();
        using var reader = new StringReader(stdout);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('>'))
                continue;

            var afterArrow = trimmed.TrimStart('>').TrimStart();
            if (string.IsNullOrWhiteSpace(afterArrow))
                continue;

            var tokens = PathUtilities.SplitTokens(afterArrow);
            if (tokens.Count == 0)
                continue;

            var packageId = tokens[0];
            if (isExcluded is not null && isExcluded(packageId))
                continue;

            var version = tokens.LastOrDefault(x => NuGetVersion.TryParse(x, out _));
            if (version is null)
            {
                onUnparseableLine?.Invoke(line, packageId);
                continue;
            }

            packages.Add(new ParsedPackageLine(packageId, version));
        }

        return packages;
    }
}

public sealed record ParsedPackageLine(string PackageId, string Version);
