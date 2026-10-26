namespace ExternalDependenciesFinder.Services;

public sealed class ScanTargetFinder
{
    public IReadOnlyList<ScanTarget> FindTargets(string scanRoot, string outputDirectoryToExclude)
    {
        var solutionFiles = Directory
            .EnumerateFiles(scanRoot, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (solutionFiles.Length > 0)
        {
            var solution = solutionFiles[0];
            var ignored = solutionFiles.Skip(1).ToArray();
            return
            [
                new ScanTarget(
                    WorkingDirectory: scanRoot,
                    TargetPath: solution,
                    Kind: ScanTargetKind.Solution,
                    IgnoredSolutions: ignored)
            ];
        }

        var projectFiles = Directory
            .EnumerateFiles(scanRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(x => !PathUtilities.IsInsideDirectory(x, outputDirectoryToExclude))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(project => new ScanTarget(
                WorkingDirectory: Path.GetDirectoryName(project)!,
                TargetPath: project,
                Kind: ScanTargetKind.Project,
                IgnoredSolutions: Array.Empty<string>()))
            .ToArray();

        return projectFiles;
    }
}

public enum ScanTargetKind
{
    Solution,
    Project
}

public sealed record ScanTarget(
    string WorkingDirectory,
    string TargetPath,
    ScanTargetKind Kind,
    IReadOnlyList<string> IgnoredSolutions);
