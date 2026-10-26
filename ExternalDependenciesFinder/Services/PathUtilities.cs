namespace ExternalDependenciesFinder.Services;

public static class PathUtilities
{
    public static bool IsInsideDirectory(string filePath, string directoryPath)
    {
        var fullFilePath = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullFilePath.StartsWith(fullDirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> SplitTokens(string value)
    {
        var result = new List<string>();
        var i = 0;

        while (i < value.Length)
        {
            while (i < value.Length && char.IsWhiteSpace(value[i]))
                i++;

            if (i >= value.Length)
                break;

            var start = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i]))
                i++;

            result.Add(value[start..i]);
        }

        return result;
    }

    public static void RecreateDirectory(string path, Action<string>? logInfo = null)
    {
        if (Directory.Exists(path))
        {
            logInfo?.Invoke($"Deleting existing output directory: {path}");
            Directory.Delete(path, recursive: true);

            for (var i = 0; i < 10 && Directory.Exists(path); i++)
                Thread.Sleep(50);
        }

        Directory.CreateDirectory(path);
        logInfo?.Invoke($"Output directory created: {path}");
    }
}
